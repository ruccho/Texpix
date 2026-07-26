using UnityEngine;

namespace Texpix
{
    public enum TexpixOutlineMode
    {
        None = 0,
        FourNeighbor = 1,
        EightNeighbor = 2
    }

    /// <summary>
    ///     Layout of the vertex stream Texpix shaders consume.
    ///     <para>
    ///         uv0.xy carries atlas font-pixel coordinates; uv0.zw carries the outline
    ///         color and mode plus the atlas format, packed as integers. Keeping them in
    ///         the vertex stream (instead of in material properties) lets every component
    ///         share one material, which is what uGUI batching requires.
    ///     </para>
    ///     <para>
    ///         Packing layout — both components are exact integers in float32
    ///         (&lt; 2^24), so they survive interpolation across a quad whose corners all
    ///         carry the same value:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>z = R * 256 + G</item>
    ///         <item>w = format * 262144 + B * 1024 + A * 4 + mode</item>
    ///     </list>
    ///     Channels are 8-bit and in the same color space as the vertex color, so the
    ///     outline and the fill receive identical treatment downstream.
    /// </summary>
    public static class TexpixVertexFormat
    {
        /// <summary>Scale of the atlas format field in the uv0.w payload; 2 bits above the outline data.</summary>
        private const int FormatScale = 262144;

        /// <summary>Encodes an outline color and mode plus the atlas format into the uv0.zw payload.</summary>
        public static Vector2 PackOutline(Color32 color, TexpixOutlineMode mode,
            TexpixAtlasFormat atlasFormat = TexpixAtlasFormat.Outline)
        {
            var modeValue = Mathf.Clamp((int)mode, 0, 3);
            var formatValue = Mathf.Clamp((int)atlasFormat, 0, 3);
            return new Vector2(
                color.r * 256 + color.g,
                formatValue * FormatScale + color.b * 1024 + color.a * 4 + modeValue);
        }

        /// <summary>Inverse of <see cref="PackOutline" />; mirrors the shader-side decode.</summary>
        public static void UnpackOutline(Vector2 packed, out Color32 color, out TexpixOutlineMode mode,
            out TexpixAtlasFormat atlasFormat)
        {
            var rg = Mathf.RoundToInt(packed.x);
            var payload = Mathf.RoundToInt(packed.y);
            atlasFormat = (TexpixAtlasFormat)(payload / FormatScale);
            payload %= FormatScale;
            var a = payload / 4 % 256;
            color = new Color32((byte)(rg / 256), (byte)(rg % 256), (byte)(payload / 1024), (byte)a);
            mode = (TexpixOutlineMode)(payload % 4);
        }

        /// <summary>Overload for callers that only need the outline part of the payload.</summary>
        public static void UnpackOutline(Vector2 packed, out Color32 color, out TexpixOutlineMode mode)
        {
            UnpackOutline(packed, out color, out mode, out _);
        }
    }
}
