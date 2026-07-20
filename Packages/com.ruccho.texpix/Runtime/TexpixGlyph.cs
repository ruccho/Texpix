namespace Texpix
{
    /// <summary>
    /// A glyph entry in a Texpix atlas. All values are integer font pixels.
    /// The bitmap rect includes the 1px outline padding on every side; bearings are
    /// already adjusted for that padding.
    /// </summary>
    public struct TexpixGlyph
    {
        public uint unicode;
        public uint glyphIndex;

        /// <summary>Bottom-left of the (padded) bitmap in the atlas, in font pixels.</summary>
        public int atlasX;
        public int atlasY;
        /// <summary>Padded bitmap size in font pixels; 0 for advance-only glyphs (spaces).</summary>
        public int width;
        public int height;

        /// <summary>Pen-relative offset from the pen position to the bitmap's left edge.</summary>
        public int bearingX;
        /// <summary>Baseline-relative offset to the bitmap's top edge.</summary>
        public int bearingY;
        public int advance;

        public int cellIndex;
        /// <summary>Index into the owning font's resolved fallback chain (0 = the primary font).</summary>
        public int sourceFontIndex;
        public bool valid;

        public bool HasBitmap => width > 0 && height > 0;
    }
}
