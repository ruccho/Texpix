using NUnit.Framework;
using UnityEngine;

namespace Texpix.Tests
{
    public class TexpixVertexFormatTests
    {
        [TestCase(0, 0, 0, 0, TexpixOutlineMode.None)]
        [TestCase(255, 255, 255, 255, TexpixOutlineMode.EightNeighbor)]
        [TestCase(1, 2, 3, 4, TexpixOutlineMode.FourNeighbor)]
        [TestCase(255, 0, 255, 0, TexpixOutlineMode.EightNeighbor)]
        [TestCase(0, 255, 0, 255, TexpixOutlineMode.None)]
        public void PackOutline_RoundTrips(int r, int g, int b, int a, TexpixOutlineMode mode)
        {
            var source = new Color32((byte)r, (byte)g, (byte)b, (byte)a);

            var packed = TexpixVertexFormat.PackOutline(source, mode);
            TexpixVertexFormat.UnpackOutline(packed, out var decoded, out var decodedMode);

            Assert.AreEqual(source, decoded);
            Assert.AreEqual(mode, decodedMode);
        }

        /// <summary>
        ///     The shader decodes with floor(v + 0.5), so both components must stay exact
        ///     integers in float32 (i.e. below 2^24) for every possible color.
        /// </summary>
        [Test]
        public void PackOutline_StaysExactInFloat32()
        {
            var packed = TexpixVertexFormat.PackOutline(new Color32(255, 255, 255, 255),
                TexpixOutlineMode.EightNeighbor);

            Assert.Less(packed.x, 1 << 24);
            Assert.Less(packed.y, 1 << 24);
            Assert.AreEqual(packed.x, Mathf.Floor(packed.x));
            Assert.AreEqual(packed.y, Mathf.Floor(packed.y));
        }
    }
}
