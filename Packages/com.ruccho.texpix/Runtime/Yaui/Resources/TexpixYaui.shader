// Texpix text for YAUI panels (TexpixYauiText). Each glyph is a YAUI image primitive whose UV rect covers the
// glyph in the atlas (normalized font-pixel coordinates). The outline travels in the primitive's border: the
// border color is the outline color, and the border width encodes the outline mode plus 4 times the atlas format, or
// 32 for an inline sprite, drawn from its texture as is.
Shader "Hidden/Texpix/Yaui"
{
    Properties
    {
        // Set by the renderer inside masks (YauiMask).
        [HideInInspector] _YauiStencilRef ("Stencil Ref", Float) = 0
        [HideInInspector] _YauiStencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _YauiStencilPass ("Stencil Pass", Float) = 0
    }

    SubShader
    {
        // Compiled only when YAUI is installed: without it the include below would fail to import. The C# side
        // (TexpixYauiText) is compiled under the same condition (TEXPIX_YAUI).
        PackageRequirements
        {
            "com.ruccho.yaui"
        }

        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Stencil
        {
            Ref [_YauiStencilRef]
            Comp [_YauiStencilComp]
            Pass [_YauiStencilPass]
        }

        HLSLINCLUDE
        #pragma target 4.5
        // Images (UVs) and borders (the outline color) only.
        #define YAUI_FEATURES 34
        #include "Packages/com.ruccho.yaui/Runtime/Shaders/Yaui.hlsl"
        #include "Packages/com.ruccho.texpix/Runtime/Shaders/Texpix.hlsl"

        half4 TexpixFrag(Varyings i)
        {
            uint slot = TextureSlot((uint)i.params.y);
            float encoded = round(i.params.z);
            // Inline sprites (TexpixYauiText.SpriteEncoding): the sprite texture as is, multiplied by the color.
            [branch] if (encoded >= 32.0)
            {
                return half4(Premultiply(SampleTexture(slot, i.uv) * i.color));
            }

            float atlasFormat = floor(encoded * 0.25);
            float outlineMode = encoded - atlasFormat * 4.0;

            // x: width, z: height of the atlas in texels.
            float4 atlas = _YauiAtlasParams[slot];
            float2 fontPx = i.uv * float2(atlas.x * TexpixPixelsPerTexel(atlasFormat), atlas.z);
            float4 texelSize = float4(1.0 / atlas.x, 1.0 / atlas.z, atlas.x, atlas.z);
            float r = SampleTexture(slot, TexpixAtlasUV(fontPx, texelSize, atlasFormat)).r;
            float level = TexpixExtractLevel(r, TexpixSubPixel(fontPx, atlasFormat), atlasFormat);
            float4 color = TexpixShade(level, i.color, i.borderColor, outlineMode);
            return half4(Premultiply(color));
        }

        half4 TexpixFragOverlay(Varyings i) : SV_Target { return TexpixFrag(i); }
        half4 TexpixFragWorld(Varyings i) : SV_Target { return TexpixFrag(i); }
        ENDHLSL

        Pass
        {
            Name "Overlay"
            Tags { "LightMode" = "YauiOverlay" }
            ZTest Always

            HLSLPROGRAM
            #pragma vertex VertOverlay
            #pragma fragment TexpixFragOverlay
            ENDHLSL
        }

        Pass
        {
            Name "World"
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertWorld
            #pragma fragment TexpixFragWorld
            ENDHLSL
        }
    }
}
