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
// in uv0.zw (see TexpixUnpackOutline). All functions use float arithmetic only, so
// they work on shader model 2.x targets.
//
// Precision rule: every integer decode below is written with multiplications by
// power-of-two constants, floor, and comparisons — never division, fmod, or exp2.
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

// Extracts the level of one font pixel from a sampled atlas value (R channel, 0..1).
// A fill-only atlas stores a single bit, expanded here to TEXPIX_LEVEL_FILL so that
// downstream shading does not need to know the format.
float TexpixExtractLevel(float atlasR, float subPixel, float atlasFormat)
{
    float packedByte = floor(atlasR * 255.0 + 0.5);
    float fillOnly = atlasFormat >= 0.5 ? 1.0 : 0.0;

    // Right-shift the byte by subPixel * bitsPerPixel (0..7), one binary digit of the
    // shift amount at a time: >>4, >>2, >>1. Each step is an exact multiply + floor.
    float shift = fillOnly > 0.5 ? subPixel : subPixel * 2.0;
    float q = packedByte;
    q = shift >= 3.5 ? floor(q * 0.0625) : q;
    shift = shift >= 3.5 ? shift - 4.0 : shift;
    q = shift >= 1.5 ? floor(q * 0.25) : q;
    shift = shift >= 1.5 ? shift - 2.0 : shift;
    q = shift >= 0.5 ? floor(q * 0.5) : q;

    // Keep the low 1 or 2 bits.
    float bit1 = q - floor(q * 0.5) * 2.0;
    float bits2 = q - floor(q * 0.25) * 4.0;
    return fillOnly > 0.5 ? bit1 * TEXPIX_LEVEL_FILL : bits2;
}

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
