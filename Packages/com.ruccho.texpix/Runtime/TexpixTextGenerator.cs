using System;
using System.Collections.Generic;
using UnityEngine;

namespace Texpix
{
    public enum TexpixHorizontalAlignment
    {
        Left = 0,
        Center = 1,
        Right = 2
    }

    public enum TexpixVerticalAlignment
    {
        Top = 0,
        Middle = 1,
        Bottom = 2
    }

    public enum TexpixWrapMode
    {
        NoWrap = 0,
        Wrap = 1
    }

    public enum TexpixOverflowMode
    {
        Overflow = 0,
        Truncate = 1,
        Ellipsis = 2
    }

    /// <summary>Layout constraints in integer font pixels. Non-positive sizes mean unconstrained.</summary>
    public struct TexpixLayoutSettings
    {
        public int MaxWidthPx;
        public int MaxHeightPx;
        public TexpixHorizontalAlignment HorizontalAlignment;
        public TexpixVerticalAlignment VerticalAlignment;
        public TexpixWrapMode WrapMode;
        public TexpixOverflowMode Overflow;

        /// <summary>
        ///     Extra spacing inserted between adjacent items (glyphs and sprites) on a
        ///     line, in font pixels. May be negative. Participates in wrapping,
        ///     trimming and alignment like kerning does.
        /// </summary>
        public int LetterSpacingPx;

        /// <summary>
        ///     Adjustment added to the font's line height, in font pixels. May be
        ///     negative; the effective line height is clamped to 1.
        /// </summary>
        public int LineSpacingPx;

        /// <summary>Enables the minimal tag set: color, sprite, noparse, br.</summary>
        public bool RichText;

        /// <summary>Sprite source for &lt;sprite=...&gt; tags (may be null).</summary>
        public TexpixSpriteAsset SpriteAsset;
    }

    public struct TexpixTextMetrics
    {
        public int LineCount;
        public int WidthPx;
        public int HeightPx;
    }

    /// <summary>A positioned quad in font-pixel space (y-up, layout origin at the rect's top-left).</summary>
    public struct TexpixQuad
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int AtlasX;
        public int AtlasY;

        /// <summary>Per-quad tint (rich text color); multiplied with the component color.</summary>
        public Color32 Color;

        /// <summary>Fallback-chain index of the atlas this quad samples (0 = primary font).</summary>
        public int FontIndex;
    }

    /// <summary>
    ///     Text layout in integer font-pixel space: word wrap (with CJK character wrap
    ///     and basic kinsoku rules), 9-way alignment, overflow modes, kerning,
    ///     surrogate pairs, replacement character for missing glyphs, and a minimal
    ///     rich-text tag set (color / sprite / noparse / br).
    ///     Glyph quads and sprite quads are emitted to separate lists because they
    ///     sample different textures.
    ///     Output coordinates are y-up with the origin at the layout rect's top-left
    ///     corner; content extends toward negative y.
    /// </summary>
    public static class TexpixTextGenerator
    {
        private const int EllipsisCodepoint = 0x2026;
        private const uint ReplacementCodepoint = 0xFFFD;
        private const int MaxTagLength = 100;

        // Basic kinsoku sets (BMP only).
        private const string LineStartProhibited = "、。，．,.:;：；!！?？)]}）］｝〉》」』〕〗ゝゞ々ーぁぃぅぇぉっゃゅょゎゕゖァィゥェォッャュョヮヵヶ・…‥％%℃";
        private const string LineEndProhibited = "([{（［｛〈《「『〔〖";

        private static readonly Color32 White = new(255, 255, 255, 255);

        private static readonly List<Item> SItems = new();
        private static readonly List<Line> SLines = new();
        private static readonly List<TexpixGlyph> SEllipsisGlyphs = new();
        private static readonly List<Color32> SColorStack = new();

        public static TexpixTextMetrics Generate(ITexpixFontSource font, string text, in TexpixLayoutSettings settings,
            List<TexpixQuad> quads)
        {
            return Generate(font, (text ?? "").AsSpan(), in settings, quads, null);
        }

        public static TexpixTextMetrics Generate(ITexpixFontSource font, string text, in TexpixLayoutSettings settings,
            List<TexpixQuad> quads, List<TexpixQuad> spriteQuads)
        {
            return Generate(font, (text ?? "").AsSpan(), in settings, quads, spriteQuads);
        }

        public static TexpixTextMetrics Generate(ITexpixFontSource font, ReadOnlySpan<char> text,
            in TexpixLayoutSettings settings, List<TexpixQuad> quads)
        {
            return Generate(font, text, in settings, quads, null);
        }

        /// <summary>
        ///     Span-based entry point: lets callers feed text from reusable char buffers
        ///     (counters, timers, ...) without building strings. The text is fully
        ///     consumed during the call; nothing references it afterwards. Layout and
        ///     rich-text parsing are allocation-free after warm-up (the only exception
        ///     is a &lt;color&gt; name outside the built-in HTML set).
        /// </summary>
        public static TexpixTextMetrics Generate(ITexpixFontSource font, ReadOnlySpan<char> text,
            in TexpixLayoutSettings settings, List<TexpixQuad> quads, List<TexpixQuad> spriteQuads)
        {
            quads.Clear();
            spriteQuads?.Clear();
            Shape(font, text, in settings);
            BreakLines(font, settings.MaxWidthPx, settings.WrapMode == TexpixWrapMode.Wrap, settings.LetterSpacingPx);

            var ascent = font.Ascent;
            var descent = font.Descent;
            var lineHeight = Mathf.Max(1, font.LineHeight + settings.LineSpacingPx);
            var singleLineHeight = ascent - descent;

            // Vertical overflow: number of lines that fit the rect (always at least one).
            var visibleLines = SLines.Count;
            if (settings.Overflow != TexpixOverflowMode.Overflow && settings.MaxHeightPx > 0)
            {
                var capacity = 1 + Mathf.Max(0, (settings.MaxHeightPx - singleLineHeight) / Mathf.Max(1, lineHeight));
                visibleLines = Mathf.Min(visibleLines, Mathf.Max(1, capacity));
            }

            var verticallyTruncated = visibleLines < SLines.Count;

            if (settings.Overflow != TexpixOverflowMode.Overflow)
                TrimForOverflow(font, settings, visibleLines, verticallyTruncated);

            // Vertical alignment offset from the rect top (positive = downward).
            var blockHeight = (visibleLines - 1) * lineHeight + singleLineHeight;
            var top = 0;
            if (settings.MaxHeightPx > 0)
                switch (settings.VerticalAlignment)
                {
                    case TexpixVerticalAlignment.Middle:
                        top = (settings.MaxHeightPx - blockHeight) / 2;
                        break;
                    case TexpixVerticalAlignment.Bottom:
                        top = settings.MaxHeightPx - blockHeight;
                        break;
                }

            var maxLineWidth = 0;
            for (var k = 0; k < visibleLines; k++)
            {
                var line = SLines[k];
                maxLineWidth = Mathf.Max(maxLineWidth, line.Width);

                var xOffset = 0;
                if (settings.MaxWidthPx > 0)
                    switch (settings.HorizontalAlignment)
                    {
                        case TexpixHorizontalAlignment.Center:
                            xOffset = (settings.MaxWidthPx - line.Width) / 2;
                            break;
                        case TexpixHorizontalAlignment.Right:
                            xOffset = settings.MaxWidthPx - line.Width;
                            break;
                    }

                var baseline = -(top + ascent + k * lineHeight);
                EmitLine(font, line, xOffset, baseline, settings.LetterSpacingPx, quads, spriteQuads);
            }

            return new TexpixTextMetrics
            {
                LineCount = visibleLines,
                WidthPx = maxLineWidth,
                HeightPx = blockHeight
            };
        }

        private static void Shape(ITexpixFontSource font, ReadOnlySpan<char> text, in TexpixLayoutSettings settings)
        {
            SItems.Clear();
            SColorStack.Clear();
            var currentColor = White;
            var noparse = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (settings.RichText && c == '<' &&
                    TryHandleTag(text, ref i, ref currentColor, ref noparse, in settings))
                    continue;

                int cp = c;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(c, text[i + 1]);
                    i++;
                }

                if (cp == '\n')
                {
                    SItems.Add(new Item { Codepoint = cp, Newline = true });
                    continue;
                }

                if (cp is < 0x20 or 0x7F)
                    continue;

                if (!font.TryGetGlyph((uint)cp, out var glyph) &&
                    !font.TryGetGlyph(ReplacementCodepoint, out glyph))
                    continue;

                var whitespace = cp is ' ' or 0x3000;
                SItems.Add(new Item { Codepoint = cp, Glyph = glyph, Color = currentColor, Whitespace = whitespace });
            }
        }

        /// <summary>
        ///     Handles the tag starting at text[i] ('&lt;'). On success, i is advanced to
        ///     the closing '&gt;'. Returns false for unrecognized tags, which then render
        ///     literally. Parsing is span-based and allocation-free (except the
        ///     ColorUtility fallback for color names outside the built-in HTML set).
        /// </summary>
        private static bool TryHandleTag(ReadOnlySpan<char> text, ref int i, ref Color32 currentColor, ref bool noparse,
            in TexpixLayoutSettings settings)
        {
            var closeRelative = text.Slice(i + 1).IndexOf('>');
            if (closeRelative < 0 || closeRelative + 1 > MaxTagLength)
                return false;

            var close = i + 1 + closeRelative;
            var tag = text.Slice(i + 1, closeRelative);

            if (noparse)
            {
                if (!tag.SequenceEqual("/noparse"))
                    return false;
                noparse = false;
                i = close;
                return true;
            }

            if (tag.SequenceEqual("noparse"))
            {
                noparse = true;
                i = close;
                return true;
            }

            if (tag.SequenceEqual("br"))
            {
                SItems.Add(new Item { Codepoint = '\n', Newline = true });
                i = close;
                return true;
            }

            if (tag.SequenceEqual("/color"))
            {
                if (SColorStack.Count > 0)
                {
                    currentColor = SColorStack[^1];
                    SColorStack.RemoveAt(SColorStack.Count - 1);
                }

                i = close;
                return true;
            }

            if (tag.StartsWith("color=", StringComparison.Ordinal))
            {
                var value = tag.Slice(6).Trim('"');
                if (!TryParseColor(value, out var parsed))
                    return false;
                SColorStack.Add(currentColor);
                currentColor = parsed;
                i = close;
                return true;
            }

            if (tag.StartsWith("sprite", StringComparison.Ordinal))
            {
                if (settings.SpriteAsset == null)
                    return false;

                var spec = tag.Slice(6);

                // Optional trailing attribute: <sprite=name tint=1> tints with the
                // current rich-text color.
                var tint = false;
                if (spec.EndsWith(" tint=1", StringComparison.Ordinal))
                {
                    tint = true;
                    spec = spec.Slice(0, spec.Length - " tint=1".Length);
                }

                TexpixSpriteAsset.Entry entry;
                bool found;
                if (spec.Length > 0 && spec[0] == '=')
                    found = settings.SpriteAsset.TryGetEntry(spec.Slice(1).Trim('"'), out entry);
                else if (spec.StartsWith(" index=", StringComparison.Ordinal) &&
                         int.TryParse(spec.Slice(7), out var index))
                    found = settings.SpriteAsset.TryGetEntry(index, out entry);
                else
                    return false;

                if (!found)
                    return false;

                SItems.Add(new Item
                {
                    Codepoint = 0,
                    Sprite = true,
                    Color = tint ? currentColor : White,
                    Glyph = new TexpixGlyph
                    {
                        AtlasX = entry.x,
                        AtlasY = entry.y,
                        Width = entry.width,
                        Height = entry.height,
                        BearingX = entry.bearingX,
                        BearingY = entry.bearingY,
                        Advance = entry.advance,
                        Valid = true
                    }
                });
                i = close;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Span-based replacement for ColorUtility.TryParseHtmlString: hex forms
        ///     (#RGB / #RGBA / #RRGGBB / #RRGGBBAA) and the HTML color names that
        ///     ColorUtility supports, without allocating. Unknown names fall back to
        ///     ColorUtility (allocates; rare).
        /// </summary>
        private static bool TryParseColor(ReadOnlySpan<char> value, out Color color)
        {
            if (value.Length > 0 && value[0] == '#')
                return TryParseHexColor(value.Slice(1), out color);
            return TryParseNamedColor(value, out color);
        }

        private static bool TryParseHexColor(ReadOnlySpan<char> hex, out Color color)
        {
            color = default;
            int r, g, b;
            var a = 0xFF;
            switch (hex.Length)
            {
                case 3:
                case 4:
                    if (!TryParseNibble(hex[0], out r) || !TryParseNibble(hex[1], out g) ||
                        !TryParseNibble(hex[2], out b))
                        return false;
                    r *= 17;
                    g *= 17;
                    b *= 17;
                    if (hex.Length == 4)
                    {
                        if (!TryParseNibble(hex[3], out a))
                            return false;
                        a *= 17;
                    }

                    break;
                case 6:
                case 8:
                    if (!TryParseHexByte(hex, 0, out r) || !TryParseHexByte(hex, 2, out g) ||
                        !TryParseHexByte(hex, 4, out b))
                        return false;
                    if (hex.Length == 8 && !TryParseHexByte(hex, 6, out a))
                        return false;
                    break;
                default:
                    return false;
            }

            color = new Color32((byte)r, (byte)g, (byte)b, (byte)a);
            return true;
        }

        private static bool TryParseNibble(char c, out int value)
        {
            switch (c)
            {
                case >= '0' and <= '9':
                    value = c - '0';
                    return true;
                case >= 'a' and <= 'f':
                    value = c - 'a' + 10;
                    return true;
                case >= 'A' and <= 'F':
                    value = c - 'A' + 10;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        private static bool TryParseHexByte(ReadOnlySpan<char> hex, int offset, out int value)
        {
            value = 0;
            if (!TryParseNibble(hex[offset], out var high) || !TryParseNibble(hex[offset + 1], out var low))
                return false;
            value = (high << 4) | low;
            return true;
        }

        private static bool TryParseNamedColor(ReadOnlySpan<char> value, out Color color)
        {
            // The HTML names ColorUtility.TryParseHtmlString documents, same values.
            if (NameIs(value, "red")) return Rgb(0xFF0000, out color);
            if (NameIs(value, "cyan") || NameIs(value, "aqua")) return Rgb(0x00FFFF, out color);
            if (NameIs(value, "blue")) return Rgb(0x0000FF, out color);
            if (NameIs(value, "darkblue")) return Rgb(0x0000A0, out color);
            if (NameIs(value, "lightblue")) return Rgb(0xADD8E6, out color);
            if (NameIs(value, "purple")) return Rgb(0x800080, out color);
            if (NameIs(value, "yellow")) return Rgb(0xFFFF00, out color);
            if (NameIs(value, "lime")) return Rgb(0x00FF00, out color);
            if (NameIs(value, "fuchsia") || NameIs(value, "magenta")) return Rgb(0xFF00FF, out color);
            if (NameIs(value, "white")) return Rgb(0xFFFFFF, out color);
            if (NameIs(value, "silver")) return Rgb(0xC0C0C0, out color);
            if (NameIs(value, "grey") || NameIs(value, "gray")) return Rgb(0x808080, out color);
            if (NameIs(value, "black")) return Rgb(0x000000, out color);
            if (NameIs(value, "orange")) return Rgb(0xFFA500, out color);
            if (NameIs(value, "brown")) return Rgb(0xA52A2A, out color);
            if (NameIs(value, "maroon")) return Rgb(0x800000, out color);
            if (NameIs(value, "green")) return Rgb(0x008000, out color);
            if (NameIs(value, "olive")) return Rgb(0x808000, out color);
            if (NameIs(value, "navy")) return Rgb(0x000080, out color);
            if (NameIs(value, "teal")) return Rgb(0x008080, out color);

            // Unknown name: preserve full ColorUtility behavior at the cost of one
            // string allocation. This path should be rare in practice.
            return ColorUtility.TryParseHtmlString(value.ToString(), out color);
        }

        private static bool NameIs(ReadOnlySpan<char> value, string name)
        {
            return value.Equals(name.AsSpan(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Rgb(int rgb, out Color color)
        {
            color = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
            return true;
        }

        private static void BreakLines(ITexpixFontSource font, int maxWidth, bool wrap, int letterSpacing)
        {
            SLines.Clear();
            var count = SItems.Count;
            var lineStart = 0;
            var skipLeadingWhitespace = false;
            var lineHasContent = false;
            var pen = 0;
            var trimmedPen = 0; // pen at the end of the last non-whitespace item
            var breakIndex = -1;
            var breakPen = 0;
            uint previousGlyphIndex = 0;

            void EndLine(int end, int width, int nextStart, bool skipWhitespace)
            {
                SLines.Add(new Line { Start = lineStart, End = end, Width = width });
                lineStart = nextStart;
                pen = 0;
                trimmedPen = 0;
                breakIndex = -1;
                previousGlyphIndex = 0;
                lineHasContent = false;
                skipLeadingWhitespace = skipWhitespace;
            }

            var i = 0;
            while (i < count)
            {
                var item = SItems[i];

                if (item.Newline)
                {
                    EndLine(i, trimmedPen, i + 1, false);
                    i++;
                    continue;
                }

                if (skipLeadingWhitespace && item.Whitespace && !lineHasContent)
                {
                    lineStart = i + 1;
                    i++;
                    continue;
                }

                var kern = previousGlyphIndex != 0 && item.Glyph.GlyphIndex != 0 && item.Glyph.SourceFontIndex == 0
                    ? font.GetKerning(previousGlyphIndex, item.Glyph.GlyphIndex)
                    : 0;
                // Letter spacing behaves like a constant kern between adjacent items.
                var spacing = i > lineStart ? letterSpacing : 0;
                var newPen = pen + spacing + kern + item.Glyph.Advance;

                if (lineHasContent && !item.Whitespace && i > lineStart)
                {
                    var previous = SItems[i - 1];
                    var canBreak = previous.Whitespace ||
                                   ((previous.Sprite || item.Sprite || IsCjk(previous.Codepoint) ||
                                     IsCjk(item.Codepoint)) &&
                                    !IsLineEndProhibited(previous.Codepoint) &&
                                    !IsLineStartProhibited(item.Codepoint));
                    if (canBreak)
                    {
                        breakIndex = i;
                        breakPen = trimmedPen;
                    }
                }

                if (wrap && maxWidth > 0 && !item.Whitespace && newPen > maxWidth && lineHasContent)
                {
                    if (breakIndex > lineStart)
                    {
                        // EndLine resets breakIndex; keep the resume position first.
                        var resumeAt = breakIndex;
                        EndLine(resumeAt, breakPen, resumeAt, true);
                        i = resumeAt;
                    }
                    else
                    {
                        // No break opportunity: break mid-word before the current item.
                        EndLine(i, trimmedPen, i, true);
                    }

                    continue;
                }

                pen = newPen;
                if (!item.Whitespace)
                {
                    trimmedPen = pen;
                    lineHasContent = true;
                }

                // Kerning only applies between glyphs of the primary font; a fallback
                // glyph breaks the pair.
                previousGlyphIndex = item.Glyph.SourceFontIndex == 0 ? item.Glyph.GlyphIndex : 0;
                i++;
            }

            EndLine(count, trimmedPen, count, false);
        }

        private static void TrimForOverflow(ITexpixFontSource font, in TexpixLayoutSettings settings, int visibleLines,
            bool verticallyTruncated)
        {
            var ellipsisMode = settings.Overflow == TexpixOverflowMode.Ellipsis;
            var ellipsisWidth = ellipsisMode ? BuildEllipsis(font) : 0;

            for (var k = 0; k < visibleLines; k++)
            {
                var line = SLines[k];
                var exceedsWidth = settings.MaxWidthPx > 0 && line.Width > settings.MaxWidthPx;
                var isLastVisible = k == visibleLines - 1;
                var needsEllipsis = ellipsisMode && SEllipsisGlyphs.Count > 0 &&
                                    (exceedsWidth || (isLastVisible && verticallyTruncated));

                if (!exceedsWidth && !needsEllipsis)
                    continue;

                var budget = settings.MaxWidthPx > 0 ? settings.MaxWidthPx : int.MaxValue;
                var reserved = needsEllipsis ? ellipsisWidth : 0;

                // Longest prefix whose trimmed width plus the ellipsis fits the budget.
                var pen = 0;
                var cutEnd = line.Start;
                var cutPen = 0;
                uint previousGlyphIndex = 0;
                for (var i = line.Start; i < line.End; i++)
                {
                    var item = SItems[i];
                    var kern = previousGlyphIndex != 0 && item.Glyph.GlyphIndex != 0 && item.Glyph.SourceFontIndex == 0
                        ? font.GetKerning(previousGlyphIndex, item.Glyph.GlyphIndex)
                        : 0;
                    var spacing = i > line.Start ? settings.LetterSpacingPx : 0;
                    pen += spacing + kern + item.Glyph.Advance;
                    // Kerning only applies between glyphs of the primary font; a fallback
                    // glyph breaks the pair.
                    previousGlyphIndex = item.Glyph.SourceFontIndex == 0 ? item.Glyph.GlyphIndex : 0;
                    if (!item.Whitespace)
                    {
                        if (pen + reserved > budget)
                            break;
                        cutEnd = i + 1;
                        cutPen = pen;
                    }
                }

                line.End = cutEnd;
                line.Width = cutPen + reserved;
                line.Ellipsis = needsEllipsis;
                line.EllipsisPen = cutPen;
                SLines[k] = line;
            }
        }

        private static void EmitLine(ITexpixFontSource font, in Line line, int xOffset, int baseline,
            int letterSpacing, List<TexpixQuad> quads, List<TexpixQuad> spriteQuads)
        {
            var pen = 0;
            uint previousGlyphIndex = 0;
            for (var i = line.Start; i < line.End; i++)
            {
                var item = SItems[i];
                if (item.Newline)
                    continue;
                var kern = previousGlyphIndex != 0 && item.Glyph.GlyphIndex != 0 && item.Glyph.SourceFontIndex == 0
                    ? font.GetKerning(previousGlyphIndex, item.Glyph.GlyphIndex)
                    : 0;
                pen += (i > line.Start ? letterSpacing : 0) + kern;
                if (item.Glyph.HasBitmap)
                {
                    var target = item.Sprite ? spriteQuads : quads;
                    if (target != null)
                        EmitGlyph(item.Glyph, xOffset + pen, baseline, item.Color, target);
                }

                pen += item.Glyph.Advance;
                // Kerning only applies between glyphs of the primary font; a fallback
                // glyph breaks the pair.
                previousGlyphIndex = item.Glyph.SourceFontIndex == 0 ? item.Glyph.GlyphIndex : 0;
            }

            if (line.Ellipsis)
            {
                pen = line.EllipsisPen;
                foreach (var glyph in SEllipsisGlyphs)
                {
                    if (glyph.HasBitmap)
                        EmitGlyph(glyph, xOffset + pen, baseline, White, quads);
                    pen += glyph.Advance;
                }
            }
        }

        private static void EmitGlyph(in TexpixGlyph glyph, int penX, int baseline, Color32 color,
            List<TexpixQuad> quads)
        {
            quads.Add(new TexpixQuad
            {
                X = penX + glyph.BearingX,
                Y = baseline + glyph.BearingY - glyph.Height,
                Width = glyph.Width,
                Height = glyph.Height,
                AtlasX = glyph.AtlasX,
                AtlasY = glyph.AtlasY,
                Color = color,
                FontIndex = glyph.SourceFontIndex
            });
        }

        private static int BuildEllipsis(ITexpixFontSource font)
        {
            SEllipsisGlyphs.Clear();
            if (font.TryGetGlyph(EllipsisCodepoint, out var ellipsis))
            {
                SEllipsisGlyphs.Add(ellipsis);
                return ellipsis.Advance;
            }

            if (font.TryGetGlyph('.', out var dot))
            {
                SEllipsisGlyphs.Add(dot);
                SEllipsisGlyphs.Add(dot);
                SEllipsisGlyphs.Add(dot);
                return dot.Advance * 3;
            }

            return 0;
        }

        private static bool IsCjk(int cp)
        {
            return cp is >= 0x1100 and <= 0x11FF || // Hangul jamo
                   cp is >= 0x2E80 and <= 0x9FFF || // CJK radicals, kana, punctuation, unified ideographs
                   cp is >= 0xAC00 and <= 0xD7AF || // Hangul syllables
                   cp is >= 0xF900 and <= 0xFAFF || // CJK compatibility ideographs
                   cp is >= 0xFF00 and <= 0xFF60 || // fullwidth forms
                   cp is >= 0x20000 and <= 0x2FFFF;
            // ideograph extensions
        }

        private static bool IsLineStartProhibited(int cp)
        {
            return cp is > 0 and <= 0xFFFF && LineStartProhibited.IndexOf((char)cp) >= 0;
        }

        private static bool IsLineEndProhibited(int cp)
        {
            return cp is > 0 and <= 0xFFFF && LineEndProhibited.IndexOf((char)cp) >= 0;
        }

        private struct Item
        {
            public int Codepoint;
            public TexpixGlyph Glyph;
            public Color32 Color;
            public bool Whitespace;
            public bool Newline;
            public bool Sprite;
        }

        private struct Line
        {
            public int Start;
            public int End;

            /// <summary>Visual width used for alignment (trimmed; includes ellipsis when present).</summary>
            public int Width;

            public bool Ellipsis;

            /// <summary>Pen position where the ellipsis starts when <see cref="Ellipsis" /> is set.</summary>
            public int EllipsisPen;
        }
    }
}