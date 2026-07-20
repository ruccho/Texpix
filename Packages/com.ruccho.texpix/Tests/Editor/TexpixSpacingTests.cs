using System.Collections.Generic;
using NUnit.Framework;

namespace Texpix.Tests
{
    public class TexpixSpacingTests
    {
        private static readonly List<TexpixQuad> Quads = new();

        private static TexpixTextMetrics Run(FakeFontSource font, string text, TexpixLayoutSettings settings)
        {
            return TexpixTextGenerator.Generate(font, text, in settings, Quads);
        }

        private static TexpixLayoutSettings Box(int w, int h,
            int letterSpacing = 0,
            int lineSpacing = 0,
            TexpixWrapMode wrap = TexpixWrapMode.Wrap,
            TexpixOverflowMode overflow = TexpixOverflowMode.Overflow)
        {
            return new TexpixLayoutSettings
            {
                MaxWidthPx = w,
                MaxHeightPx = h,
                LetterSpacingPx = letterSpacing,
                LineSpacingPx = lineSpacing,
                WrapMode = wrap,
                Overflow = overflow
            };
        }

        [Test]
        public void LetterSpacing_AppliedBetweenItems_NotBeforeFirst()
        {
            var font = new FakeFontSource();
            var m = Run(font, "abc", Box(100, 100, letterSpacing: 2, wrap: TexpixWrapMode.NoWrap));

            Assert.That(Quads[0].X, Is.EqualTo(0));
            Assert.That(Quads[1].X, Is.EqualTo(6));  // 4 + 2
            Assert.That(Quads[2].X, Is.EqualTo(12)); // 4 + 2 + 4 + 2
            Assert.That(m.WidthPx, Is.EqualTo(16));
        }

        [Test]
        public void LetterSpacing_Negative_TightensText()
        {
            var font = new FakeFontSource();
            var m = Run(font, "ab", Box(100, 100, letterSpacing: -1, wrap: TexpixWrapMode.NoWrap));

            Assert.That(Quads[1].X, Is.EqualTo(3));
            Assert.That(m.WidthPx, Is.EqualTo(7));
        }

        [Test]
        public void LetterSpacing_CombinesWithKerning()
        {
            var font = new FakeFontSource
            {
                Kerning =
                {
                    [('A', 'V')] = -1
                }
            };
            Run(font, "AV", Box(100, 100, letterSpacing: 2, wrap: TexpixWrapMode.NoWrap));

            Assert.That(Quads[1].X, Is.EqualTo(5)); // 4 + 2 - 1
        }

        [Test]
        public void LetterSpacing_ParticipatesInWrapping()
        {
            var font = new FakeFontSource();
            // "aa" = 8px fits a 9px box, but with +2 spacing (10px) it wraps.
            var m = Run(font, "aa", Box(9, 100, letterSpacing: 2));

            Assert.That(m.LineCount, Is.EqualTo(2));
            // No spacing at the start of the wrapped line.
            Assert.That(Quads[1].X, Is.EqualTo(0));
        }

        [Test]
        public void LetterSpacing_AppliesToEllipsisTrimming()
        {
            var font = new FakeFontSource();
            // "aaaa" with +2 spacing: pens 4, 10, 16, 22. Budget 15, ellipsis 4:
            // two glyphs (10) + 4 = 14 fits; three (16) + 4 doesn't.
            var m = Run(font, "aaaa", Box(15, 100, letterSpacing: 2,
                wrap: TexpixWrapMode.NoWrap, overflow: TexpixOverflowMode.Ellipsis));

            Assert.That(Quads.Count, Is.EqualTo(3)); // 2 glyphs + ellipsis
            Assert.That(Quads[2].X, Is.EqualTo(10));
            Assert.That(m.WidthPx, Is.EqualTo(14));
        }

        [Test]
        public void LineSpacing_AdjustsBaselineStride_AndBlockHeight()
        {
            var font = new FakeFontSource();
            var m = Run(font, "a\nb", Box(100, 100, lineSpacing: 3));
            Assert.That(Quads[1].Y, Is.EqualTo(Quads[0].Y - 14)); // 11 + 3
            Assert.That(m.HeightPx, Is.EqualTo(24));              // (11 + 3) + 10

            Run(font, "a\nb", Box(100, 100, lineSpacing: -3));
            Assert.That(Quads[1].Y, Is.EqualTo(Quads[0].Y - 8)); // 11 - 3
        }

        [Test]
        public void LineSpacing_AffectsVerticalCapacity()
        {
            var font = new FakeFontSource();
            // "aaaaaa" wraps to 3 lines of 2 glyphs in a 10px-wide box.
            var m = Run(font, "aaaaaa", Box(10, 32, overflow: TexpixOverflowMode.Truncate));
            Assert.That(m.LineCount, Is.EqualTo(3)); // capacity 1 + (32-10)/11 = 3

            m = Run(font, "aaaaaa", Box(10, 32, lineSpacing: 10, overflow: TexpixOverflowMode.Truncate));
            Assert.That(m.LineCount, Is.EqualTo(2)); // line height 21 → capacity 2
            Assert.That(Quads.Count, Is.EqualTo(4));
        }
    }
}
