#ifndef TEXPIX_YAUI_MODULE_INCLUDED
#define TEXPIX_YAUI_MODULE_INCLUDED

// The YAUI shader module of Texpix (TexpixYauiModule.asset): the glyphs of TexpixYauiText shaded inside the uber
// shader of YAUI, like Hidden/Texpix/Yaui. The generated uber shader includes Yaui.hlsl before this file.
//
// A glyph is an image primitive whose UV rect covers it in the atlas (normalized font-pixel coordinates). The border
// width encodes the outline mode plus 4 times the atlas format, or 32 for an inline sprite drawn from its texture as
// is; the border color is the outline color.

#include "Packages/com.ruccho.texpix/Runtime/Shaders/Texpix.hlsl"

half4 TexpixYauiModuleFrag(Varyings i, bool world)
{
    #if YAUI_HAS_UV
    float2 uv = i.uv;
    #else
    float2 uv = 0.0;
    #endif
    #if YAUI_HAS_BORDER_COLOR
    float4 outlineColor = i.borderColor;
    #else
    float4 outlineColor = 0.0;
    #endif

    uint slot = TextureSlot((uint)i.params.y);
    float encoded = round(i.params.z);
    // Inline sprites: the sprite texture as is, multiplied by the color.
    [branch] if (encoded >= 32.0)
    {
        return half4(Premultiply(SampleTexture(slot, uv) * i.color));
    }

    float atlasFormat = floor(encoded * 0.25);
    float outlineMode = encoded - atlasFormat * 4.0;

    // x: width, z: height of the atlas in texels.
    float4 atlas = _YauiAtlasParams[slot];
    float2 fontPx = uv * float2(atlas.x * TexpixPixelsPerTexel(atlasFormat), atlas.z);
    float4 texelSize = float4(1.0 / atlas.x, 1.0 / atlas.z, atlas.x, atlas.z);
    float r = SampleTexture(slot, TexpixAtlasUV(fontPx, texelSize, atlasFormat)).r;
    float level = TexpixExtractLevel(r, TexpixSubPixel(fontPx, atlasFormat), atlasFormat);
    return half4(Premultiply(TexpixShade(level, i.color, outlineColor, outlineMode)));
}

#endif
