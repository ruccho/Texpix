// Texpix atlas sampling helpers.
//
// A Texpix atlas is a single-channel (R8) texture where each texel packs a run of
// horizontal font pixels (LSB first). How many, and what a pixel can hold, depends on
// the atlas format (TEXPIX_FORMAT_*):
//
//   Outline (2bpp, 4 pixels per texel) — every font pixel stores a level:
//     3 = glyph fill
//     2 = outline pixel orthogonally adjacent to the fill (4-neighborhood)
//     1 = outline pixel only diagonally adjacent to the fill (8-neighborhood extra)
//     0 = outside
//   Fill only (1bpp, 8 pixels per texel) — one bit per font pixel, set = fill.
//     The decode expands it to the fill level, so shading is format-agnostic and
//     outline modes simply have nothing to draw.
//
// Vertices are expected to carry *font pixel* coordinates of the atlas in uv0.xy
// (integers at quad corners) and the packed outline color/mode plus the atlas format
// in uv0.zw (see TexpixUnpackOutline). All functions use float arithmetic only
// and work on shader model 2.x targets.
//
// Precision rule: every integer decode below is written with multiplications by
// power-of-two constants, floor/frac, and comparisons — never division, fmod, or
// exp2. Keep coordinates and decode intermediates in float, not half/fixed.
// Adding and multiplying are correctly rounded on every GPU (so an integer times
// 0.25 is exact), whereas division is allowed several ULP of error (GLSL ES: 2.5 ULP,
// Metal fast-math: reciprocal approximation). With a division, an exact quotient such
// as 240 / 4 can come out as 59.99998, floor drops it to 59, and the extracted level
// changes (0 becomes 3: an outside pixel rendered as fill). Observed on Apple Silicon
// (Metal); desktop D3D11 happens to compute these exactly, which hid it.

#ifndef TEXPIX_INCLUDED
#define TEXPIX_INCLUDED

#define TEXPIX_LEVEL_OUTSIDE 0.0
#define TEXPIX_LEVEL_DIAGONAL_OUTLINE 1.0
#define TEXPIX_LEVEL_EDGE_OUTLINE 2.0
#define TEXPIX_LEVEL_FILL 3.0

#define TEXPIX_OUTLINE_NONE 0.0
#define TEXPIX_OUTLINE_FOUR_NEIGHBOR 1.0
#define TEXPIX_OUTLINE_EIGHT_NEIGHBOR 2.0

#define TEXPIX_FORMAT_OUTLINE 0.0
#define TEXPIX_FORMAT_FILL_ONLY 1.0

// Font pixels packed into one texel: 4 at 2bpp, 8 at 1bpp.
float TexpixPixelsPerTexel(float atlasFormat)
{
    return atlasFormat >= 0.5 ? 8.0 : 4.0;
}

// Reciprocal of TexpixPixelsPerTexel as an exact power-of-two constant.
float TexpixInvPixelsPerTexel(float atlasFormat)
{
    return atlasFormat >= 0.5 ? 0.125 : 0.25;
}

// Index of the texel holding font pixel column x (x already floored).
float TexpixTexelIndex(float x, float atlasFormat)
{
    return floor(x * TexpixInvPixelsPerTexel(atlasFormat));
}

// Converts a font-pixel coordinate to the UV of the texel containing it.
// atlasTexelSize is the standard Unity _TexelSize vector of the atlas (1/w, 1/h, w, h).
// The UV targets the texel center, so the rounding in the final multiply cannot move
// the point sample onto a neighboring texel.
float2 TexpixAtlasUV(float2 fontPx, float4 atlasTexelSize, float atlasFormat)
{
    float texelX = TexpixTexelIndex(floor(fontPx.x), atlasFormat) + 0.5;
    float texelY = floor(fontPx.y) + 0.5;
    return float2(texelX * atlasTexelSize.x, texelY * atlasTexelSize.y);
}

// Sub-pixel index of a font-pixel coordinate within its texel (0..3 or 0..7).
float TexpixSubPixel(float2 fontPx, float atlasFormat)
{
    float x = floor(fontPx.x);
    return x - TexpixTexelIndex(x, atlasFormat) * TexpixPixelsPerTexel(atlasFormat);
}

// Selects 2^-(shift + bitsPerPixel) without a variable shift, division, exp2,
// lookup texture, or a serial floor/select ladder. texelPhase must be in [0, 1).
// Four exact constants handle pairs of bits; the optional low bit handles 1bpp.
// All of this is independent of the sampled byte, so it can overlap the fetch.
float TexpixResidueScale(float texelPhase, bool fillOnly)
{
    float scale = texelPhase < 0.5
        ? (texelPhase < 0.25 ? 0.25 : 0.0625)
        : (texelPhase < 0.75 ? 0.015625 : 0.00390625);
    return scale * ((fillOnly && frac(texelPhase * 4.0) < 0.5) ? 2.0 : 1.0);
}

// Extracts a level from a point-sampled, linear R8 byte. subPixel is an integer
// in [0, pixelsPerTexel). The public signature and 1bpp -> fill expansion are unchanged.
//
// Centered residue: for byte B, shift s and radix R = 2^bitsPerPixel,
//   floor(R * frac((B + 0.5) / (R * 2^s))) = (B >> s) & (R - 1).
// The half-byte bias keeps the input off every integer boundary. Consequently the
// initial byte-rounding floor and every intermediate right-shift floor are redundant.
// The scale is an exact power of two; this does NOT reintroduce approximate division.
float TexpixExtractLevel(float atlasR, float subPixel, float atlasFormat)
{
    bool fillOnly = atlasFormat >= 0.5;
    float phase = subPixel * (fillOnly ? 0.125 : 0.25);
    float residue = frac((atlasR * 255.0 + 0.5) * TexpixResidueScale(phase, fillOnly));
    float level = floor(residue * (fillOnly ? 2.0 : 4.0));
    return level * (fillOnly ? TEXPIX_LEVEL_FILL : 1.0);
}

// Vertex-stage partial evaluation for the default fill/outline palette. This is
// an internal vertex-to-fragment payload, NOT a change to the mesh's uv0 format:
//   xy = continuous atlas texel coordinates (not normalized UVs)
//   z  = first visible residue bucket, w = first fill residue bucket.
// Inputs are finite, nonnegative atlas coordinates, as emitted by Texpix.
// The x transform is affine and power-of-two; do not floor in the vertex shader.
// One float4 replaces the existing fontPx/mode/format interpolator, with no extra
// Canvas channels, materials, keywords, textures, or draw calls.
float4 TexpixPrepareCoverage(float2 fontPx, float outlineMode, float atlasFormat)
{
    bool fillOnly = atlasFormat >= 0.5;
    float fillMin = fillOnly ? 0.5 : 0.75;
    float visibleMin = outlineMode >= 1.5 ? 0.25 : (outlineMode >= 0.5 ? 0.5 : 0.75);
    visibleMin = fillOnly ? 0.5 : visibleMin;
    return float4(fontPx.x * (fillOnly ? 0.125 : 0.25), fontPx.y, visibleMin, fillMin);
}

float2 TexpixPreparedAtlasUV(float4 prepared, float4 atlasTexelSize)
{
    return float2((floor(prepared.x) + 0.5) * atlasTexelSize.x,
                  (floor(prepared.y) + 0.5) * atlasTexelSize.y);
}

// Fused extraction + palette evaluation. Do not reconstruct the integer level:
// thresholds classify the residue directly. visibleMin <= fillMin, so the two
// masks are disjoint and the selected straight-alpha color is preserved exactly.
// No lerp-based color cancellation, premultiplication, or change to blending.
float4 TexpixShadePrepared(float atlasR, float4 prepared, float4 fillColor, float4 outlineColor)
{
    // Use a midpoint comparison instead of treating an interpolated flag as exact.
    bool fillOnly = prepared.w < 0.625;
    float phase = frac(prepared.x);
    float residue = frac((atlasR * 255.0 + 0.5) * TexpixResidueScale(phase, fillOnly));
    float isFill = step(prepared.w, residue);
    float isOutline = step(prepared.z, residue) - isFill;
    return fillColor * isFill + outlineColor * isOutline;
}

// Usage: prepare once per vertex, then sample/shade once per fragment. The fetch
// remains unconditional: alpha clipping, masks and derivatives keep their ordering.
#define TexpixSampleCoverage_Tex2D(tex, texelSize, prepared, fillColor, outlineColor) \
    TexpixShadePrepared(tex2D((tex), TexpixPreparedAtlasUV((prepared), (texelSize))).r, \
                        (prepared), (fillColor), (outlineColor))

// Convenience: level of the font pixel at fontPx, sampled from a texture object.
// Usage (built-in pipeline):
//   TexpixSampleLevel_Tex2D(_MainTex, _MainTex_TexelSize, i.fontPx, i.atlasFormat)
#define TexpixSampleLevel_Tex2D(tex, texelSize, fontPx, atlasFormat) \
    TexpixExtractLevel(tex2D((tex), TexpixAtlasUV((fontPx), (texelSize), (atlasFormat))).r, \
                       TexpixSubPixel((fontPx), (atlasFormat)), (atlasFormat))

// Converts an 8-bit gamma-space UI color to the shader's working color space.
// In linear projects this matches Unity's UIGammaToLinear (UnityUI.cginc): a piecewise
// approximation whose error stays below 0.5/255 in gamma space, so 8-bit values survive
// the round trip. Reimplemented here to keep this include free of UI-specific headers.
float3 TexpixUIGammaToWorkingSpace(float3 value)
{
    #ifdef UNITY_COLORSPACE_GAMMA
    return value;
    #else
    float3 low = 0.0849710 * value - 0.000163029;
    float3 high = value * (value * (value * 0.265885 + 0.736584) - 0.00980184) + 0.00319697;
    const float3 split = 0.0725490; // 18.5 / 255
    return (value < split) ? low : high;
    #endif
}

// Vertex colors reach the shader in gamma space when the canvas has
// "Vertex Color Always In Gamma Color Space" enabled (Unity recommends it in linear
// projects); otherwise uGUI has already converted them. Pass Unity's global
// _UIVertexColorAlwaysGammaSpace as the second argument. Alpha is never converted.
float4 TexpixUIVertexColor(float4 vertexColor, float alwaysGammaSpace)
{
    if (alwaysGammaSpace > 0.5)
        vertexColor.rgb = TexpixUIGammaToWorkingSpace(vertexColor.rgb);
    return vertexColor;
}

// Decodes the outline color/mode and the atlas format packed into uv0.zw by
// TexpixVertexFormat:
//   z = R * 256 + G
//   w = format * 262144 + B * 1024 + A * 4 + mode
// Both are exact integers in float32, so interpolating them across a quad (whose
// corners all carry the same value) is lossless. Call this in the vertex shader and
// interpolate the results, not the packed values.
// The channels are 8-bit gamma-space values (like uGUI vertex colors), so the decoded
// color is converted to the working color space here.
void TexpixUnpackOutline(float2 packed, out float4 outlineColor, out float outlineMode, out float atlasFormat)
{
    // Field extraction by multiplying with exact power-of-two reciprocals (see the
    // precision rule at the top of this file).
    float rg = floor(packed.x + 0.5);
    float r = floor(rg * (1.0 / 256.0));
    float g = rg - r * 256.0;

    float rest = floor(packed.y + 0.5);
    atlasFormat = floor(rest * (1.0 / 262144.0));
    rest -= atlasFormat * 262144.0;
    float b = floor(rest * (1.0 / 1024.0));
    rest -= b * 1024.0;
    float a = floor(rest * 0.25);

    outlineMode = rest - a * 4.0;
    outlineColor = float4(TexpixUIGammaToWorkingSpace(float3(r, g, b) / 255.0), a / 255.0);
}

// Resolves a level into fill / outline / transparent using an outline mode
// (TEXPIX_OUTLINE_*). Returns straight-alpha color. Format-agnostic: a fill-only
// atlas never produces an outline level, so the mode has no effect there.
float4 TexpixShade(float level, float4 fillColor, float4 outlineColor, float outlineMode)
{
    float isFill = step(2.5, level);
    // 4-neighbor mode shows level 2 only; 8-neighbor mode shows levels 1 and 2.
    float outlineMinLevel = outlineMode >= 1.5 ? 0.5 : 1.5;
    float isOutline = outlineMode >= 0.5 ? (1.0 - isFill) * step(outlineMinLevel, level) : 0.0;
    return fillColor * isFill + outlineColor * isOutline;
}

#endif // TEXPIX_INCLUDED
