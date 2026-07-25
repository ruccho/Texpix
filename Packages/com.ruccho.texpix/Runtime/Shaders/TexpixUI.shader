// Default uGUI shader for Texpix text. Structure mirrors UI/Default so that
// masking (RectMask2D / Mask) and canvas clipping behave identically.
Shader "Texpix/UI Default"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texpix Atlas", 2D) = "black" {}

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
            #include "Texpix.hlsl"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                // xy = atlas font-pixel coords, zw = packed outline color/mode.
                float4 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                // xy = atlas font-pixel coords, z = outline mode.
                float3 fontPx : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                fixed4 outlineColor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _ClipRect;
            // Set globally by Unity; mirrors Canvas.vertexColorAlwaysGammaSpace.
            float _UIVertexColorAlwaysGammaSpace;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);

                float4 outlineColor;
                float outlineMode;
                TexpixUnpackOutline(v.texcoord.zw, outlineColor, outlineMode);
                o.fontPx = float3(v.texcoord.xy, outlineMode);
                o.outlineColor = outlineColor;
                o.color = TexpixUIVertexColor(v.color, _UIVertexColorAlwaysGammaSpace);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float level = TexpixSampleLevel_Tex2D(_MainTex, _MainTex_TexelSize, i.fontPx.xy);
                fixed4 color = TexpixShade(level, i.color, i.outlineColor, i.fontPx.z);

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