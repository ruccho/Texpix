// Texpix atlas sampling helpers.
//
// A Texpix atlas is a single-channel (R8) texture where each texel packs
// 4 horizontal font pixels at 2 bits each (LSB first). Every font pixel stores a
// level:
//   3 = glyph fill
//   2 = outline pixel orthogonally adjacent to the fill (4-neighborhood)
//   1 = outline pixel only diagonally adjacent to the fill (8-neighborhood extra)
//   0 = outside
//
// Vertices are expected to carry *font pixel* coordinates of the atlas in uv0.xy
// (integers at quad corners) and the packed outline color/mode in uv0.zw (see
// TexpixUnpackOutline). All functions use float arithmetic only, so they work
// on shader model 2.x targets.

#ifndef TEXPIX_INCLUDED
#define TEXPIX_INCLUDED

#define TEXPIX_LEVEL_OUTSIDE 0.0
#define TEXPIX_LEVEL_DIAGONAL_OUTLINE 1.0
#define TEXPIX_LEVEL_EDGE_OUTLINE 2.0
#define TEXPIX_LEVEL_FILL 3.0

#define TEXPIX_OUTLINE_NONE 0.0
#define TEXPIX_OUTLINE_FOUR_NEIGHBOR 1.0
#define TEXPIX_OUTLINE_EIGHT_NEIGHBOR 2.0

// Converts a font-pixel coordinate to the UV of the texel containing it.
// atlasTexelSize is the standard Unity _TexelSize vector of the atlas (1/w, 1/h, w, h).
float2 TexpixAtlasUV(float2 fontPx, float4 atlasTexelSize)
{
    float texelX = floor(floor(fontPx.x) / 4.0) + 0.5;
    float texelY = floor(fontPx.y) + 0.5;
    return float2(texelX * atlasTexelSize.x, texelY * atlasTexelSize.y);
}

// Sub-pixel index (0..3) of a font-pixel coordinate within its texel.
float TexpixSubPixel(float2 fontPx)
{
    return fmod(floor(fontPx.x), 4.0);
}

// Extracts the 2-bit level of one font pixel from a sampled atlas value (R channel, 0..1).
float TexpixExtractLevel(float atlasR, float subPixel)
{
    float packedByte = floor(atlasR * 255.0 + 0.5);
    return fmod(floor(packedByte / exp2(subPixel * 2.0)), 4.0);
}

// Convenience: level of the font pixel at fontPx, sampled from a texture object.
// Usage (built-in pipeline): TexpixSampleLevel_Tex2D(_MainTex, _MainTex_TexelSize, i.fontPx)
#define TexpixSampleLevel_Tex2D(tex, texelSize, fontPx) \
    TexpixExtractLevel(tex2D((tex), TexpixAtlasUV((fontPx), (texelSize))).r, TexpixSubPixel(fontPx))

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

// Decodes the outline color and mode packed into uv0.zw by TexpixVertexFormat:
//   z = R * 256 + G
//   w = B * 1024 + A * 4 + mode
// Both are exact integers in float32, so interpolating them across a quad (whose
// corners all carry the same value) is lossless. Call this in the vertex shader and
// interpolate the results, not the packed values.
// The channels are 8-bit gamma-space values (like uGUI vertex colors), so the decoded
// color is converted to the working color space here.
void TexpixUnpackOutline(float2 packed, out float4 outlineColor, out float outlineMode)
{
    float rg = floor(packed.x + 0.5);
    float r = floor(rg / 256.0);
    float g = rg - r * 256.0;

    float rest = floor(packed.y + 0.5);
    float b = floor(rest / 1024.0);
    rest -= b * 1024.0;
    float a = floor(rest / 4.0);

    outlineMode = rest - a * 4.0;
    outlineColor = float4(TexpixUIGammaToWorkingSpace(float3(r, g, b) / 255.0), a / 255.0);
}

// Resolves a level into fill / outline / transparent using an outline mode
// (TEXPIX_OUTLINE_*). Returns straight-alpha color.
float4 TexpixShade(float level, float4 fillColor, float4 outlineColor, float outlineMode)
{
    float isFill = step(2.5, level);
    // 4-neighbor mode shows level 2 only; 8-neighbor mode shows levels 1 and 2.
    float outlineMinLevel = outlineMode >= 1.5 ? 0.5 : 1.5;
    float isOutline = outlineMode >= 0.5 ? (1.0 - isFill) * step(outlineMinLevel, level) : 0.0;
    return fillColor * isFill + outlineColor * isOutline;
}

#endif // TEXPIX_INCLUDED
