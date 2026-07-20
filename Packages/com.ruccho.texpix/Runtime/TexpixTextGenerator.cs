using System.Collections.Generic;
using UnityEngine;

namespace Texpix
{
    public enum TexpixHorizontalAlignment
    {
        Left = 0,
        Center = 1,
        Right = 2,
    }

    public enum TexpixVerticalAlignment
    {
        Top = 0,
        Middle = 1,
        Bottom = 2,
    }

    public enum TexpixWrapMode
    {
        NoWrap = 0,
        Wrap = 1,
    }

    public enum TexpixOverflowMode
    {
        Overflow = 0,
        Truncate = 1,
        Ellipsis = 2,
    }

    /// <summary>Layout constraints in integer font pixels. Non-positive sizes mean unconstrained.</summary>
    public struct TexpixLayoutSettings
    {
        public int maxWidthPx;
        public int maxHeightPx;
        public TexpixHorizontalAlignment horizontalAlignment;
        public TexpixVerticalAlignment verticalAlignment;
        public TexpixWrapMode wrapMode;
        public TexpixOverflowMode overflow;
        /// <summary>Enables the minimal tag set: color, sprite, noparse, br.</summary>
        public bool richText;
        /// <summary>Sprite source for &lt;sprite=...&gt; tags (may be null).</summary>
        public TexpixSpriteAsset spriteAsset;
    }

    public struct TexpixTextMetrics
    {
        public int lineCount;
        public int widthPx;
        public int heightPx;
    }

    /// <summary>A positioned quad in font-pixel space (y-up, layout origin at the rect's top-left).</summary>
    public struct TexpixQuad
    {
        public int x;
        public int y;
        public int width;
        public int height;
        public int atlasX;
        public int atlasY;
        /// <summary>Per-quad tint (rich text color); multiplied with the component color.</summary>
        public Color32 color;
        /// <summary>Fallback-chain index of the atlas this quad samples (0 = primary font).</summary>
        public int fontIndex;
    }

    /// <summary>
    /// Text layout in integer font-pixel space: word wrap (with CJK character wrap
    /// and basic kinsoku rules), 9-way alignment, overflow modes, kerning,
    /// surrogate pairs, replacement character for missing glyphs, and a minimal
    /// rich-text tag set (color / sprite / noparse / br).
    /// Glyph quads and sprite quads are emitted to separate lists because they
    /// sample different textures.
    /// Output coordinates are y-up with the origin at the layout rect's top-left
    /// corner; content extends toward negative y.
    /// </summary>
    public static class TexpixTextGenerator
    {
        const int EllipsisCodepoint = 0x2026;
        const uint ReplacementCodepoint = 0xFFFD;
        const int MaxTagLength = 100;

        // Basic kinsoku sets (BMP only).
        const string LineStartProhibited = "、。，．,.:;：；!！?？)]}）］｝〉》」』〕〗ゝゞ々ーぁぃぅぇぉっゃゅょゎゕゖァィゥェォッャュョヮヵヶ・…‥％%℃";
        const string LineEndProhibited = "([{（［｛〈《「『〔〖";

        static readonly Color32 White = new(255, 255, 255, 255);

        struct Item
        {
            public int codepoint;
            public TexpixGlyph glyph;
            public Color32 color;
            public bool whitespace;
            public bool newline;
            public bool sprite;
        }

        struct Line
        {
            public int start;
            public int end;
            /// <summary>Visual width used for alignment (trimmed; includes ellipsis when present).</summary>
            public int width;
            public bool ellipsis;
            /// <summary>Pen position where the ellipsis starts when <see cref="ellipsis"/> is set.</summary>
            public int ellipsisPen;
        }

        static readonly List<Item> s_Items = new();
        static readonly List<Line> s_Lines = new();
        static readonly List<TexpixGlyph> s_EllipsisGlyphs = new();
        static readonly List<Color32> s_ColorStack = new();

        public static TexpixTextMetrics Generate(ITexpixFontSource font, string text, in TexpixLayoutSettings settings, List<TexpixQuad> quads)
            => Generate(font, text, in settings, quads, null);

        public static TexpixTextMetrics Generate(ITexpixFontSource font, string text, in TexpixLayoutSettings settings,
            List<TexpixQuad> quads, List<TexpixQuad> spriteQuads)
        {
            quads.Clear();
            spriteQuads?.Clear();
            Shape(font, text ?? "", in settings);
            BreakLines(font, settings.maxWidthPx, settings.wrapMode == TexpixWrapMode.Wrap);

            int ascent = font.Ascent;
            int descent = font.Descent;
            int lineHeight = font.LineHeight;
            int singleLineHeight = ascent - descent;

            // Vertical overflow: number of lines that fit the rect (always at least one).
            int visibleLines = s_Lines.Count;
            if (settings.overflow != TexpixOverflowMode.Overflow && settings.maxHeightPx > 0)
            {
                int capacity = 1 + Mathf.Max(0, (settings.maxHeightPx - singleLineHeight) / Mathf.Max(1, lineHeight));
                visibleLines = Mathf.Min(visibleLines, Mathf.Max(1, capacity));
            }
            bool verticallyTruncated = visibleLines < s_Lines.Count;

            if (settings.overflow != TexpixOverflowMode.Overflow)
                TrimForOverflow(font, settings, visibleLines, verticallyTruncated);

            // Vertical alignment offset from the rect top (positive = downward).
            int blockHeight = (visibleLines - 1) * lineHeight + singleLineHeight;
            int top = 0;
            if (settings.maxHeightPx > 0)
            {
                switch (settings.verticalAlignment)
                {
                    case TexpixVerticalAlignment.Middle:
                        top = (settings.maxHeightPx - blockHeight) / 2;
                        break;
                    case TexpixVerticalAlignment.Bottom:
                        top = settings.maxHeightPx - blockHeight;
                        break;
                }
            }

            int maxLineWidth = 0;
            for (int k = 0; k < visibleLines; k++)
            {
                Line line = s_Lines[k];
                maxLineWidth = Mathf.Max(maxLineWidth, line.width);

                int xOffset = 0;
                if (settings.maxWidthPx > 0)
                {
                    switch (settings.horizontalAlignment)
                    {
                        case TexpixHorizontalAlignment.Center:
                            xOffset = (settings.maxWidthPx - line.width) / 2;
                            break;
                        case TexpixHorizontalAlignment.Right:
                            xOffset = settings.maxWidthPx - line.width;
                            break;
                    }
                }

                int baseline = -(top + ascent + k * lineHeight);
                EmitLine(font, line, xOffset, baseline, quads, spriteQuads);
            }

            return new TexpixTextMetrics
            {
                lineCount = visibleLines,
                widthPx = maxLineWidth,
                heightPx = blockHeight,
            };
        }

        static void Shape(ITexpixFontSource font, string text, in TexpixLayoutSettings settings)
        {
            s_Items.Clear();
            s_ColorStack.Clear();
            Color32 currentColor = White;
            bool noparse = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (settings.richText && c == '<' && TryHandleTag(text, ref i, ref currentColor, ref noparse, in settings))
                    continue;

                int cp = c;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(c, text[i + 1]);
                    i++;
                }

                if (cp == '\n')
                {
                    s_Items.Add(new Item { codepoint = cp, newline = true });
                    continue;
                }
                if (cp < 0x20 || cp == 0x7F)
                    continue;

                if (!font.TryGetGlyph((uint)cp, out TexpixGlyph glyph) &&
                    !font.TryGetGlyph(ReplacementCodepoint, out glyph))
                    continue;

                bool whitespace = cp == ' ' || cp == 0x3000;
                s_Items.Add(new Item { codepoint = cp, glyph = glyph, color = currentColor, whitespace = whitespace });
            }
        }

        /// <summary>
        /// Handles the tag starting at text[i] ('&lt;'). On success, i is advanced to
        /// the closing '&gt;'. Returns false for unrecognized tags, which then render
        /// literally.
        /// </summary>
        static bool TryHandleTag(string text, ref int i, ref Color32 currentColor, ref bool noparse, in TexpixLayoutSettings settings)
        {
            int close = text.IndexOf('>', i + 1);
            if (close < 0 || close - i > MaxTagLength)
                return false;

            string tag = text.Substring(i + 1, close - i - 1);

            if (noparse)
            {
                if (tag != "/noparse")
                    return false;
                noparse = false;
                i = close;
                return true;
            }

            switch (tag)
            {
                case "noparse":
                    noparse = true;
                    i = close;
                    return true;
                case "br":
                    s_Items.Add(new Item { codepoint = '\n', newline = true });
                    i = close;
                    return true;
                case "/color":
                    if (s_ColorStack.Count > 0)
                    {
                        currentColor = s_ColorStack[^1];
                        s_ColorStack.RemoveAt(s_ColorStack.Count - 1);
                    }
                    i = close;
                    return true;
            }

            if (tag.StartsWith("color=", System.StringComparison.Ordinal))
            {
                string value = tag.Substring(6).Trim('"');
                if (!ColorUtility.TryParseHtmlString(value, out Color parsed))
                    return false;
                s_ColorStack.Add(currentColor);
                currentColor = parsed;
                i = close;
                return true;
            }

            if (tag.StartsWith("sprite", System.StringComparison.Ordinal))
            {
                if (settings.spriteAsset == null)
                    return false;

                // Optional attribute: <sprite=name tint=1> tints with the current rich-text color.
                string spec = tag;
                bool tint = false;
                int tintAt = spec.IndexOf(" tint=1", System.StringComparison.Ordinal);
                if (tintAt >= 0)
                {
                    tint = true;
                    spec = spec.Remove(tintAt, " tint=1".Length);
                }

                TexpixSpriteAsset.Entry entry;
                bool found;
                if (spec.StartsWith("sprite=", System.StringComparison.Ordinal))
                    found = settings.spriteAsset.TryGetEntry(spec.Substring(7).Trim('"'), out entry);
                else if (spec.StartsWith("sprite index=", System.StringComparison.Ordinal) &&
                         int.TryParse(spec.Substring(13), out int index))
                    found = settings.spriteAsset.TryGetEntry(index, out entry);
                else
                    return false;

                if (!found)
                    return false;

                s_Items.Add(new Item
                {
                    codepoint = 0,
                    sprite = true,
                    color = tint ? currentColor : White,
                    glyph = new TexpixGlyph
                    {
                        atlasX = entry.x,
                        atlasY = entry.y,
                        width = entry.width,
                        height = entry.height,
                        bearingX = entry.bearingX,
                        bearingY = entry.bearingY,
                        advance = entry.advance,
                        valid = true,
                    },
                });
                i = close;
                return true;
            }

            return false;
        }

        static void BreakLines(ITexpixFontSource font, int maxWidth, bool wrap)
        {
            s_Lines.Clear();
            int count = s_Items.Count;
            int lineStart = 0;
            bool skipLeadingWhitespace = false;
            bool lineHasContent = false;
            int pen = 0;
            int trimmedPen = 0; // pen at the end of the last non-whitespace item
            int breakIndex = -1;
            int breakPen = 0;
            uint previousGlyphIndex = 0;

            void EndLine(int end, int width, int nextStart, bool skipWhitespace)
            {
                s_Lines.Add(new Line { start = lineStart, end = end, width = width });
                lineStart = nextStart;
                pen = 0;
                trimmedPen = 0;
                breakIndex = -1;
                previousGlyphIndex = 0;
                lineHasContent = false;
                skipLeadingWhitespace = skipWhitespace;
            }

            int i = 0;
            while (i < count)
            {
                Item item = s_Items[i];

                if (item.newline)
                {
                    EndLine(i, trimmedPen, i + 1, false);
                    i++;
                    continue;
                }

                if (skipLeadingWhitespace && item.whitespace && !lineHasContent)
                {
                    lineStart = i + 1;
                    i++;
                    continue;
                }

                int kern = previousGlyphIndex != 0 && item.glyph.glyphIndex != 0 && item.glyph.sourceFontIndex == 0
                    ? font.GetKerning(previousGlyphIndex, item.glyph.glyphIndex)
                    : 0;
                int newPen = pen + kern + item.glyph.advance;

                if (lineHasContent && !item.whitespace && i > lineStart)
                {
                    Item previous = s_Items[i - 1];
                    bool canBreak = previous.whitespace ||
                        ((previous.sprite || item.sprite || IsCjk(previous.codepoint) || IsCjk(item.codepoint)) &&
                         !IsLineEndProhibited(previous.codepoint) &&
                         !IsLineStartProhibited(item.codepoint));
                    if (canBreak)
                    {
                        breakIndex = i;
                        breakPen = trimmedPen;
                    }
                }

                if (wrap && maxWidth > 0 && !item.whitespace && newPen > maxWidth && lineHasContent)
                {
                    if (breakIndex > lineStart)
                    {
                        // EndLine resets breakIndex; keep the resume position first.
                        int resumeAt = breakIndex;
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
                if (!item.whitespace)
                {
                    trimmedPen = pen;
                    lineHasContent = true;
                }
                // Kerning only applies between glyphs of the primary font; a fallback
                // glyph breaks the pair.
                previousGlyphIndex = item.glyph.sourceFontIndex == 0 ? item.glyph.glyphIndex : 0;
                i++;
            }

            EndLine(count, trimmedPen, count, false);
        }

        static void TrimForOverflow(ITexpixFontSource font, in TexpixLayoutSettings settings, int visibleLines, bool verticallyTruncated)
        {
            bool ellipsisMode = settings.overflow == TexpixOverflowMode.Ellipsis;
            int ellipsisWidth = ellipsisMode ? BuildEllipsis(font) : 0;

            for (int k = 0; k < visibleLines; k++)
            {
                Line line = s_Lines[k];
                bool exceedsWidth = settings.maxWidthPx > 0 && line.width > settings.maxWidthPx;
                bool isLastVisible = k == visibleLines - 1;
                bool needsEllipsis = ellipsisMode && s_EllipsisGlyphs.Count > 0 &&
                    (exceedsWidth || (isLastVisible && verticallyTruncated));

                if (!exceedsWidth && !needsEllipsis)
                    continue;

                int budget = settings.maxWidthPx > 0 ? settings.maxWidthPx : int.MaxValue;
                int reserved = needsEllipsis ? ellipsisWidth : 0;

                // Longest prefix whose trimmed width plus the ellipsis fits the budget.
                int pen = 0;
                int cutEnd = line.start;
                int cutPen = 0;
                uint previousGlyphIndex = 0;
                for (int i = line.start; i < line.end; i++)
                {
                    Item item = s_Items[i];
                    int kern = previousGlyphIndex != 0 && item.glyph.glyphIndex != 0 && item.glyph.sourceFontIndex == 0
                        ? font.GetKerning(previousGlyphIndex, item.glyph.glyphIndex)
                        : 0;
                    pen += kern + item.glyph.advance;
                    // Kerning only applies between glyphs of the primary font; a fallback
                // glyph breaks the pair.
                previousGlyphIndex = item.glyph.sourceFontIndex == 0 ? item.glyph.glyphIndex : 0;
                    if (!item.whitespace)
                    {
                        if (pen + reserved > budget)
                            break;
                        cutEnd = i + 1;
                        cutPen = pen;
                    }
                }

                line.end = cutEnd;
                line.width = cutPen + reserved;
                line.ellipsis = needsEllipsis;
                line.ellipsisPen = cutPen;
                s_Lines[k] = line;
            }
        }

        static void EmitLine(ITexpixFontSource font, in Line line, int xOffset, int baseline,
            List<TexpixQuad> quads, List<TexpixQuad> spriteQuads)
        {
            int pen = 0;
            uint previousGlyphIndex = 0;
            for (int i = line.start; i < line.end; i++)
            {
                Item item = s_Items[i];
                if (item.newline)
                    continue;
                int kern = previousGlyphIndex != 0 && item.glyph.glyphIndex != 0 && item.glyph.sourceFontIndex == 0
                    ? font.GetKerning(previousGlyphIndex, item.glyph.glyphIndex)
                    : 0;
                pen += kern;
                if (item.glyph.HasBitmap)
                {
                    var target = item.sprite ? spriteQuads : quads;
                    if (target != null)
                        EmitGlyph(item.glyph, xOffset + pen, baseline, item.color, target);
                }
                pen += item.glyph.advance;
                // Kerning only applies between glyphs of the primary font; a fallback
                // glyph breaks the pair.
                previousGlyphIndex = item.glyph.sourceFontIndex == 0 ? item.glyph.glyphIndex : 0;
            }

            if (line.ellipsis)
            {
                pen = line.ellipsisPen;
                foreach (TexpixGlyph glyph in s_EllipsisGlyphs)
                {
                    if (glyph.HasBitmap)
                        EmitGlyph(glyph, xOffset + pen, baseline, White, quads);
                    pen += glyph.advance;
                }
            }
        }

        static void EmitGlyph(in TexpixGlyph glyph, int penX, int baseline, Color32 color, List<TexpixQuad> quads)
        {
            quads.Add(new TexpixQuad
            {
                x = penX + glyph.bearingX,
                y = baseline + glyph.bearingY - glyph.height,
                width = glyph.width,
                height = glyph.height,
                atlasX = glyph.atlasX,
                atlasY = glyph.atlasY,
                color = color,
                fontIndex = glyph.sourceFontIndex,
            });
        }

        static int BuildEllipsis(ITexpixFontSource font)
        {
            s_EllipsisGlyphs.Clear();
            if (font.TryGetGlyph(EllipsisCodepoint, out TexpixGlyph ellipsis))
            {
                s_EllipsisGlyphs.Add(ellipsis);
                return ellipsis.advance;
            }
            if (font.TryGetGlyph('.', out TexpixGlyph dot))
            {
                s_EllipsisGlyphs.Add(dot);
                s_EllipsisGlyphs.Add(dot);
                s_EllipsisGlyphs.Add(dot);
                return dot.advance * 3;
            }
            return 0;
        }

        static bool IsCjk(int cp) =>
            (cp >= 0x1100 && cp <= 0x11FF) ||   // Hangul jamo
            (cp >= 0x2E80 && cp <= 0x9FFF) ||   // CJK radicals, kana, punctuation, unified ideographs
            (cp >= 0xAC00 && cp <= 0xD7AF) ||   // Hangul syllables
            (cp >= 0xF900 && cp <= 0xFAFF) ||   // CJK compatibility ideographs
            (cp >= 0xFF00 && cp <= 0xFF60) ||   // fullwidth forms
            (cp >= 0x20000 && cp <= 0x2FFFF);   // ideograph extensions

        static bool IsLineStartProhibited(int cp) => cp > 0 && cp <= 0xFFFF && LineStartProhibited.IndexOf((char)cp) >= 0;
        static bool IsLineEndProhibited(int cp) => cp > 0 && cp <= 0xFFFF && LineEndProhibited.IndexOf((char)cp) >= 0;
    }
}
