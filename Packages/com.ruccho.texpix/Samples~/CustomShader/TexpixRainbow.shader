// Sample: custom Texpix shader using the public Texpix.hlsl include.
// The fill color cycles through hues along x and time; the outline uses the
// regular outline properties. Drop this material into TexpixText.material.
Shader "Texpix/Samples/Rainbow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texpix Atlas", 2D) = "black" {}
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineMode ("Outline Mode (0/1/2)", Float) = 0
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
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 fontPx : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _OutlineColor;
            float _OutlineMode;
            float _HueScale;
            float _HueSpeed;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.fontPx = v.texcoord;
                o.color = v.color;
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
                float level = TexpixSampleLevel_Tex2D(_MainTex, _MainTex_TexelSize, i.fontPx);

                float hue = frac(i.fontPx.x * _HueScale + _Time.y * _HueSpeed);
                fixed4 fill = fixed4(HueToRgb(hue), 1.0) * i.color;
                fixed4 color = TexpixShade(level, fill, _OutlineColor, _OutlineMode);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
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
