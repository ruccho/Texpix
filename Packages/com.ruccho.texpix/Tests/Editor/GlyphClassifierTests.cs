using NUnit.Framework;

namespace Texpix.Tests
{
    public class GlyphClassifierTests
    {
        [Test]
        public void SinglePixel_ProducesCrossOutline()
        {
            var src = new byte[] { 1 };
            var dst = new byte[9];
            GlyphClassifier.Classify(src, 1, 1, dst);

            // 3x3 output, row-major:
            // 1 2 1
            // 2 3 2
            // 1 2 1
            var expected = new[]
            {
                GlyphClassifier.DiagonalOutline, GlyphClassifier.EdgeOutline, GlyphClassifier.DiagonalOutline,
                GlyphClassifier.EdgeOutline, GlyphClassifier.Fill, GlyphClassifier.EdgeOutline,
                GlyphClassifier.DiagonalOutline, GlyphClassifier.EdgeOutline, GlyphClassifier.DiagonalOutline
            };
            Assert.That(dst, Is.EqualTo(expected));
        }

        [Test]
        public void EmptyBitmap_ProducesAllOutside()
        {
            var src = new byte[4];
            var dst = new byte[16];
            GlyphClassifier.Classify(src, 2, 2, dst);
            Assert.That(dst, Is.All.EqualTo(GlyphClassifier.Outside));
        }

        [Test]
        public void DiagonalPixels_InnerCornersAreEdgeOutline()
        {
            // 2x2 checkerboard:
            // . X
            // X .
            var src = new byte[] { 0, 1, 1, 0 };
            var dst = new byte[16];
            GlyphClassifier.Classify(src, 2, 2, dst);

            // The two empty source pixels touch both set pixels orthogonally.
            Assert.That(dst[1 * 4 + 1], Is.EqualTo(GlyphClassifier.EdgeOutline)); // src(0,0)
            Assert.That(dst[2 * 4 + 2], Is.EqualTo(GlyphClassifier.EdgeOutline)); // src(1,1)
            // Set pixels stay fill.
            Assert.That(dst[1 * 4 + 2], Is.EqualTo(GlyphClassifier.Fill)); // src(1,0)
            Assert.That(dst[2 * 4 + 1], Is.EqualTo(GlyphClassifier.Fill)); // src(0,1)
            // Bottom-left output corner maps to src(-1,-1): its only diagonal inside
            // the bitmap is src(0,0), which is empty → outside.
            Assert.That(dst[0 * 4 + 0], Is.EqualTo(GlyphClassifier.Outside));
            // src(-1,0) touches src(0,1) diagonally and nothing orthogonally → level 1.
            Assert.That(dst[1 * 4 + 0], Is.EqualTo(GlyphClassifier.DiagonalOutline));
        }

        [Test]
        public void HorizontalBar_TopAndBottomAreEdgeOutline()
        {
            // 3x1 bar
            var src = new byte[] { 1, 1, 1 };
            var dst = new byte[15]; // 5x3
            GlyphClassifier.Classify(src, 3, 1, dst);

            // Middle row: 2 3 3 3 2
            Assert.That(dst[5 + 0], Is.EqualTo(GlyphClassifier.EdgeOutline));
            Assert.That(dst[5 + 1], Is.EqualTo(GlyphClassifier.Fill));
            Assert.That(dst[5 + 3], Is.EqualTo(GlyphClassifier.Fill));
            Assert.That(dst[5 + 4], Is.EqualTo(GlyphClassifier.EdgeOutline));
            // Top row: 1 2 2 2 1
            Assert.That(dst[10 + 0], Is.EqualTo(GlyphClassifier.DiagonalOutline));
            Assert.That(dst[10 + 2], Is.EqualTo(GlyphClassifier.EdgeOutline));
            Assert.That(dst[10 + 4], Is.EqualTo(GlyphClassifier.DiagonalOutline));
        }
    }
}