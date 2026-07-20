using System;

namespace Texpix
{
    /// <summary>
    /// Converts a binary glyph bitmap into the 4-level (2bpp) representation used by
    /// Texpix atlases. The output bitmap is 1px larger on every side to hold outline
    /// pixels.
    /// </summary>
    internal static class GlyphClassifier
    {
        public const byte Outside = 0;
        /// <summary>Outline pixel touching the glyph body only diagonally.</summary>
        public const byte DiagonalOutline = 1;
        /// <summary>Outline pixel orthogonally adjacent to the glyph body.</summary>
        public const byte EdgeOutline = 2;
        public const byte Fill = 3;

        /// <summary>
        /// Classifies a w×h binary bitmap (non-zero = inside) into a (w+2)×(h+2)
        /// level bitmap. Both bitmaps are row-major with the same row direction.
        /// </summary>
        public static void Classify(ReadOnlySpan<byte> src, int w, int h, Span<byte> dst)
        {
            int ow = w + 2;
            int oh = h + 2;
            if (src.Length < w * h)
                throw new ArgumentException("Source bitmap too small.", nameof(src));
            if (dst.Length < ow * oh)
                throw new ArgumentException("Destination bitmap too small.", nameof(dst));

            for (int oy = 0; oy < oh; oy++)
            {
                for (int ox = 0; ox < ow; ox++)
                {
                    int x = ox - 1;
                    int y = oy - 1;

                    byte level;
                    if (Inside(src, w, h, x, y))
                        level = Fill;
                    else if (Inside(src, w, h, x - 1, y) || Inside(src, w, h, x + 1, y) ||
                             Inside(src, w, h, x, y - 1) || Inside(src, w, h, x, y + 1))
                        level = EdgeOutline;
                    else if (Inside(src, w, h, x - 1, y - 1) || Inside(src, w, h, x + 1, y - 1) ||
                             Inside(src, w, h, x - 1, y + 1) || Inside(src, w, h, x + 1, y + 1))
                        level = DiagonalOutline;
                    else
                        level = Outside;

                    dst[oy * ow + ox] = level;
                }
            }
        }

        static bool Inside(ReadOnlySpan<byte> src, int w, int h, int x, int y)
        {
            return x >= 0 && x < w && y >= 0 && y < h && src[y * w + x] != 0;
        }
    }
}
