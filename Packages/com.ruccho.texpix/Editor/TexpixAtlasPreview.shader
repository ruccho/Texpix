// Editor-only preview for Texpix atlases. Decoded mode expands every texel into its
// font pixels (4 at 2bpp, 8 at 1bpp) and color-codes them by level (the same unpacking
// the runtime shader does, via Texpix.hlsl); raw mode shows the packed R8 bytes as
// grayscale. The quad is expected to already carry the right aspect ratio:
// pixelsPerTexel*width x height in decoded mode, width x height in raw mode.
Shader "Hidden/Texpix/Atlas Preview"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "black" {}
        _Raw ("Raw Mode", Float) = 0
        // TEXPIX_FORMAT_* of the previewed atlas.
        _AtlasFormat ("Atlas Format", Float) = 0
        // Atlas size in texels; xy = (width, height).
        _AtlasSize ("Atlas Size", Vector) = (1, 1, 0, 0)
        _FillColor ("Fill", Color) = (1, 1, 1, 1)
        _EdgeOutlineColor ("Edge Outline", Color) = (1, 0.4, 0.25, 1)
        _DiagonalOutlineColor ("Diagonal Outline", Color) = (0.25, 0.55, 1, 1)
        _OutsideColor ("Outside", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Preview"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "../Runtime/Shaders/Texpix.hlsl"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _AtlasSize;
            float _Raw;
            float _AtlasFormat;
            fixed4 _FillColor;
            fixed4 _EdgeOutlineColor;
            fixed4 _DiagonalOutlineColor;
            fixed4 _OutsideColor;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Both modes are evaluated unconditionally to keep the texture fetches out
                // of divergent flow control.
                float raw = tex2D(_MainTex, i.texcoord).r;

                // _TexelSize layout: (1/w, 1/h, w, h).
                float4 texelSize = float4(1.0 / _AtlasSize.x, 1.0 / _AtlasSize.y, _AtlasSize.x, _AtlasSize.y);
                float2 fontPx = float2(i.texcoord.x * _AtlasSize.x * TexpixPixelsPerTexel(_AtlasFormat),
                                       i.texcoord.y * _AtlasSize.y);
                float level = TexpixSampleLevel_Tex2D(_MainTex, texelSize, fontPx, _AtlasFormat);

                fixed4 decoded = _OutsideColor;
                decoded = level > TEXPIX_LEVEL_DIAGONAL_OUTLINE - 0.5 ? _DiagonalOutlineColor : decoded;
                decoded = level > TEXPIX_LEVEL_EDGE_OUTLINE - 0.5 ? _EdgeOutlineColor : decoded;
                decoded = level > TEXPIX_LEVEL_FILL - 0.5 ? _FillColor : decoded;

                return _Raw > 0.5 ? fixed4(raw, raw, raw, 1) : decoded;
            }
            ENDCG
        }
    }
}
