// Sample: custom Texpix shader using the public Texpix.hlsl include.
// The fill color cycles through hues along x and time; the outline color/mode come
// from the vertex stream. Drop this material into TexpixText.material.
Shader "Texpix/Samples/Rainbow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texpix Atlas", 2D) = "black" {}
        _HueScale ("Hue Cycle per Font Pixel", Float) = 0.02
        _HueSpeed ("Hue Cycle per Second", Float) = 0.5

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "False"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "Packages/com.ruccho.texpix/Runtime/Shaders/Texpix.hlsl"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                // xy = atlas font-pixel coords, zw = packed outline color/mode + atlas format.
                float4 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                // xy = atlas texel coords, zw = visibility/fill thresholds.
                float4 params : TEXCOORD0;
                float4 mask : TEXCOORD1;
                fixed4 outlineColor : TEXCOORD2;
                // Preserve font-pixel coordinates for the format-independent hue cycle.
                float fontPixelX : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _HueScale;
            float _HueSpeed;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            // Set globally by Unity; mirrors Canvas.vertexColorAlwaysGammaSpace.
            float _UIVertexColorAlwaysGammaSpace;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);

                #ifdef UNITY_UI_CLIP_RECT
                float2 pixelSize = o.vertex.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                o.mask = float4(
                    v.vertex.xy * 2.0 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * float2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize)));
                #endif

                float4 outlineColor;
                float outlineMode;
                float atlasFormat;
                TexpixUnpackOutline(v.texcoord.zw, outlineColor, outlineMode, atlasFormat);
                o.params = TexpixPrepareCoverage(v.texcoord.xy, outlineMode, atlasFormat);
                o.fontPixelX = v.texcoord.x;
                o.outlineColor = outlineColor;
                o.color = TexpixUIVertexColor(v.color, _UIVertexColorAlwaysGammaSpace);
                return o;
            }

            float3 HueToRgb(float h)
            {
                float r = abs(h * 6.0 - 3.0) - 1.0;
                float g = 2.0 - abs(h * 6.0 - 2.0);
                float b = 2.0 - abs(h * 6.0 - 4.0);
                return saturate(float3(r, g, b));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float hue = frac(i.fontPixelX * _HueScale + _Time.y * _HueSpeed);
                fixed4 fill = fixed4(HueToRgb(hue), 1.0) * i.color;
                fixed4 color = TexpixSampleCoverage_Tex2D(
                    _MainTex, _MainTex_TexelSize, i.params, fill, i.outlineColor);

                #ifdef UNITY_UI_CLIP_RECT
                fixed2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                color.a *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
