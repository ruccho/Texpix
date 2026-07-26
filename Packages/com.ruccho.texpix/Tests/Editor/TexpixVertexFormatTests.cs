using NUnit.Framework;
using UnityEngine;

namespace Texpix.Tests
{
    public class TexpixVertexFormatTests
    {
        [TestCase(0, 0, 0, 0, TexpixOutlineMode.None, TexpixAtlasFormat.Outline)]
        [TestCase(255, 255, 255, 255, TexpixOutlineMode.EightNeighbor, TexpixAtlasFormat.Outline)]
        [TestCase(1, 2, 3, 4, TexpixOutlineMode.FourNeighbor, TexpixAtlasFormat.Outline)]
        [TestCase(255, 0, 255, 0, TexpixOutlineMode.EightNeighbor, TexpixAtlasFormat.FillOnly)]
        [TestCase(0, 255, 0, 255, TexpixOutlineMode.None, TexpixAtlasFormat.FillOnly)]
        [TestCase(255, 255, 255, 255, TexpixOutlineMode.EightNeighbor, TexpixAtlasFormat.FillOnly)]
        public void PackOutline_RoundTrips(int r, int g, int b, int a, TexpixOutlineMode mode,
            TexpixAtlasFormat format)
        {
            var source = new Color32((byte)r, (byte)g, (byte)b, (byte)a);

            var packed = TexpixVertexFormat.PackOutline(source, mode, format);
            TexpixVertexFormat.UnpackOutline(packed, out var decoded, out var decodedMode, out var decodedFormat);

            Assert.AreEqual(source, decoded);
            Assert.AreEqual(mode, decodedMode);
            Assert.AreEqual(format, decodedFormat);
        }

        /// <summary>The outline-only overload must ignore the format field, not trip over it.</summary>
        [Test]
        public void UnpackOutline_WithoutFormat_StillDecodesTheOutline()
        {
            var source = new Color32(12, 34, 56, 78);

            var packed = TexpixVertexFormat.PackOutline(source, TexpixOutlineMode.FourNeighbor,
                TexpixAtlasFormat.FillOnly);
            TexpixVertexFormat.UnpackOutline(packed, out var decoded, out var decodedMode);

            Assert.AreEqual(source, decoded);
            Assert.AreEqual(TexpixOutlineMode.FourNeighbor, decodedMode);
        }

        /// <summary>
        ///     The shader decodes with floor(v + 0.5), so both components must stay exact
        ///     integers in float32 (i.e. below 2^24) for every possible color.
        /// </summary>
        [Test]
        public void PackOutline_StaysExactInFloat32()
        {
            var packed = TexpixVertexFormat.PackOutline(new Color32(255, 255, 255, 255),
                TexpixOutlineMode.EightNeighbor, TexpixAtlasFormat.FillOnly);

            Assert.Less(packed.x, 1 << 24);
            Assert.Less(packed.y, 1 << 24);
            Assert.AreEqual(packed.x, Mathf.Floor(packed.x));
            Assert.AreEqual(packed.y, Mathf.Floor(packed.y));
        }
    }
}
