using System.Collections.Generic;
using NUnit.Framework;
using Texpix;

namespace Texpix.Tests
{
    /// <summary>
    /// Deterministic monospace-ish fake: ASCII advance 4, CJK advance 8,
    /// ascent 8, descent -2, line height 11. Spaces are advance-only.
    /// </summary>
    class FakeFontSource : ITexpixFontSource
    {
        public int Ascent => 8;
        public int Descent => -2;
        public int LineHeight => 11;

        public readonly HashSet<uint> missing = new();
        public readonly Dictionary<(uint, uint), int> kerning = new();
        public readonly Dictionary<uint, int> fontIndexOverride = new();
        public bool hasEllipsisGlyph = true;

        public bool TryGetGlyph(uint unicode, out TexpixGlyph glyph)
        {
            glyph = default;
            if (missing.Contains(unicode))
                return false;
            if (unicode == 0x2026 && !hasEllipsisGlyph)
                return false;

            int advance = unicode >= 0x2E80 && unicode < 0x20000 ? 8 : 4;
            bool whitespace = unicode == ' ' || unicode == 0x3000;
            glyph = new TexpixGlyph
            {
                unicode = unicode,
                glyphIndex = unicode,
                width = whitespace ? 0 : advance,
                height = whitespace ? 0 : 8,
                bearingX = 0,
                bearingY = 8,
                advance = advance,
                sourceFontIndex = fontIndexOverride.TryGetValue(unicode, out int fi) ? fi : 0,
                valid = true,
            };
            return true;
        }

        public int GetKerning(uint left, uint right) =>
            kerning.TryGetValue((left, right), out int value) ? value : 0;
    }

    public class TexpixTextGeneratorTests
    {
        static readonly List<TexpixQuad> quads = new();

        static TexpixTextMetrics Run(FakeFontSource font, string text, TexpixLayoutSettings settings) =>
            TexpixTextGenerator.Generate(font, text, in settings, quads);

        static TexpixLayoutSettings Box(int w, int h,
            TexpixWrapMode wrap = TexpixWrapMode.Wrap,
            TexpixOverflowMode overflow = TexpixOverflowMode.Overflow,
            TexpixHorizontalAlignment ha = TexpixHorizontalAlignment.Left,
            TexpixVerticalAlignment va = TexpixVerticalAlignment.Top) =>
            new()
            {
                maxWidthPx = w,
                maxHeightPx = h,
                wrapMode = wrap,
                overflow = overflow,
                horizontalAlignment = ha,
                verticalAlignment = va,
            };

        [Test]
        public void SingleLine_AppliesAdvanceAndKerning()
        {
            var font = new FakeFontSource();
            font.kerning[('A', 'V')] = -1;
            var m = Run(font, "AV", Box(100, 100, wrap: TexpixWrapMode.NoWrap));

            Assert.That(quads.Count, Is.EqualTo(2));
            Assert.That(quads[0].x, Is.EqualTo(0));
            Assert.That(quads[1].x, Is.EqualTo(3)); // 4 - 1 kerning
            Assert.That(m.widthPx, Is.EqualTo(7));
            Assert.That(m.lineCount, Is.EqualTo(1));
        }

        [Test]
        public void WordWrap_BreaksAfterSpace_AndTrimsTrailingSpace()
        {
            var font = new FakeFontSource();
            // "aaa bbb": 3*4 + 4(space) + 3*4 = 28; width 14 forces a break at "bbb".
            var m = Run(font, "aaa bbb", Box(14, 100));

            Assert.That(m.lineCount, Is.EqualTo(2));
            Assert.That(quads.Count, Is.EqualTo(6));
            // Line 2 starts back at x=0, second baseline 11px below the first.
            Assert.That(quads[3].x, Is.EqualTo(0));
            Assert.That(quads[3].y, Is.EqualTo(quads[0].y - 11));
            // Trimmed line widths: both 12.
            Assert.That(m.widthPx, Is.EqualTo(12));
        }

        [Test]
        public void Wrap_ForceBreaksLongWord()
        {
            var font = new FakeFontSource();
            var m = Run(font, "aaaaaa", Box(10, 100)); // 2 glyphs per 10px line

            Assert.That(m.lineCount, Is.EqualTo(3));
            Assert.That(quads.Count, Is.EqualTo(6));
        }

        [Test]
        public void Cjk_WrapsBetweenCharacters()
        {
            var font = new FakeFontSource();
            var m = Run(font, "ああああ", Box(16, 100)); // advance 8 → 2 per line

            Assert.That(m.lineCount, Is.EqualTo(2));
            Assert.That(quads.Count, Is.EqualTo(4));
            Assert.That(quads[2].x, Is.EqualTo(0));
        }

        [Test]
        public void Kinsoku_LineDoesNotStartWithProhibitedCharacter()
        {
            var font = new FakeFontSource();
            // "ああ。" width 16: "。" cannot start a line, so break moves before the 2nd "あ".
            var m = Run(font, "ああ。", Box(16, 100));

            Assert.That(m.lineCount, Is.EqualTo(2));
            Assert.That(quads.Count, Is.EqualTo(3));
            // Line 1: single "あ"; line 2: "あ。"
            Assert.That(quads[1].y, Is.EqualTo(quads[0].y - 11));
            Assert.That(quads[2].x, Is.EqualTo(8));
            Assert.That(quads[2].y, Is.EqualTo(quads[1].y));
        }

        [Test]
        public void Newline_SplitsLines()
        {
            var font = new FakeFontSource();
            var m = Run(font, "a\r\nb", Box(100, 100));

            Assert.That(m.lineCount, Is.EqualTo(2));
            Assert.That(quads.Count, Is.EqualTo(2));
            Assert.That(quads[1].x, Is.EqualTo(0));
            Assert.That(quads[1].y, Is.EqualTo(quads[0].y - 11));
        }

        [Test]
        public void HorizontalAlignment_CenterAndRight()
        {
            var font = new FakeFontSource();
            Run(font, "aa", Box(20, 100, ha: TexpixHorizontalAlignment.Center));
            Assert.That(quads[0].x, Is.EqualTo(6)); // (20 - 8) / 2

            Run(font, "aa", Box(20, 100, ha: TexpixHorizontalAlignment.Right));
            Assert.That(quads[0].x, Is.EqualTo(12));
        }

        [Test]
        public void VerticalAlignment_MiddleAndBottom()
        {
            var font = new FakeFontSource();
            // Single line block height = 10. Top: baseline -8 → quad.y = -8.
            Run(font, "a", Box(100, 32, va: TexpixVerticalAlignment.Top));
            Assert.That(quads[0].y, Is.EqualTo(-8));

            Run(font, "a", Box(100, 32, va: TexpixVerticalAlignment.Middle));
            Assert.That(quads[0].y, Is.EqualTo(-8 - 11)); // top offset (32-10)/2 = 11

            Run(font, "a", Box(100, 32, va: TexpixVerticalAlignment.Bottom));
            Assert.That(quads[0].y, Is.EqualTo(-8 - 22));
        }

        [Test]
        public void Truncate_DropsLinesBeyondHeight()
        {
            var font = new FakeFontSource();
            // 3 wrapped lines, but height 12 fits only one (10 + 11k).
            var m = Run(font, "aaaaaa", Box(10, 12, overflow: TexpixOverflowMode.Truncate));

            Assert.That(m.lineCount, Is.EqualTo(1));
            Assert.That(quads.Count, Is.EqualTo(2));
        }

        [Test]
        public void Ellipsis_TrimsAndAppends_Horizontally()
        {
            var font = new FakeFontSource();
            // "aaaa" = 16 > 14. Ellipsis advance 4 → 2 chars (8) + 4 = 12 fits.
            var m = Run(font, "aaaa", Box(14, 100, wrap: TexpixWrapMode.NoWrap, overflow: TexpixOverflowMode.Ellipsis));

            Assert.That(m.lineCount, Is.EqualTo(1));
            Assert.That(quads.Count, Is.EqualTo(3)); // 2 glyphs + ellipsis
            Assert.That(quads[2].x, Is.EqualTo(8));
            Assert.That(m.widthPx, Is.EqualTo(12));
        }

        [Test]
        public void Ellipsis_AppendedOnVerticalTruncation()
        {
            var font = new FakeFontSource();
            // 3 wrapped lines, only 1 visible → last visible line trimmed to fit "…".
            var m = Run(font, "aaaaaa", Box(10, 12, overflow: TexpixOverflowMode.Ellipsis));

            Assert.That(m.lineCount, Is.EqualTo(1));
            // Line had "aa" (8); 8 + 4 > 10 → 1 glyph + ellipsis.
            Assert.That(quads.Count, Is.EqualTo(2));
            Assert.That(quads[1].x, Is.EqualTo(4));
        }

        [Test]
        public void Ellipsis_FallsBackToThreeDots()
        {
            var font = new FakeFontSource { hasEllipsisGlyph = false };
            // "aaaaaa" = 24 > 20. Budget 20: 2 chars (8) + "..." (12) = 20 fits.
            var m = Run(font, "aaaaaa", Box(20, 100, wrap: TexpixWrapMode.NoWrap, overflow: TexpixOverflowMode.Ellipsis));

            Assert.That(quads.Count, Is.EqualTo(5)); // 2 glyphs + 3 dots
            Assert.That(m.widthPx, Is.EqualTo(20));
        }

        [Test]
        public void SurrogatePair_ProducesSingleQuad()
        {
            var font = new FakeFontSource();
            Run(font, "\U0001F600", Box(100, 100));
            Assert.That(quads.Count, Is.EqualTo(1));
        }

        [Test]
        public void MissingGlyph_UsesReplacementCharacter()
        {
            var font = new FakeFontSource();
            font.missing.Add('x');
            Run(font, "x", Box(100, 100));
            Assert.That(quads.Count, Is.EqualTo(1)); // U+FFFD stands in

            font.missing.Add(0xFFFD);
            Run(font, "x", Box(100, 100));
            Assert.That(quads.Count, Is.EqualTo(0)); // no replacement available → skipped
        }

        [Test]
        public void EmptyText_ProducesNoQuads()
        {
            var font = new FakeFontSource();
            var m = Run(font, "", Box(100, 100));
            Assert.That(quads.Count, Is.EqualTo(0));
            Assert.That(m.widthPx, Is.EqualTo(0));
        }
    }
}
