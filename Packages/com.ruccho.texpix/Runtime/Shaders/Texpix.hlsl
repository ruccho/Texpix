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
// Vertices are expected to carry *font pixel* coordinates of the atlas in uv0
// (integers at quad corners). All functions use float arithmetic only, so they work
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
