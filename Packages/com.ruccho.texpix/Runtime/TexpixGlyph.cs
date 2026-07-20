namespace Texpix
{
    /// <summary>
    ///     A glyph entry in a Texpix atlas. All values are integer font pixels.
    ///     The bitmap rect includes the 1px outline padding on every side; bearings are
    ///     already adjusted for that padding.
    /// </summary>
    public struct TexpixGlyph
    {
        public uint Unicode;
        public uint GlyphIndex;

        /// <summary>Bottom-left of the (padded) bitmap in the atlas, in font pixels.</summary>
        public int AtlasX;

        public int AtlasY;

        /// <summary>Padded bitmap size in font pixels; 0 for advance-only glyphs (spaces).</summary>
        public int Width;

        public int Height;

        /// <summary>Pen-relative offset from the pen position to the bitmap's left edge.</summary>
        public int BearingX;

        /// <summary>Baseline-relative offset to the bitmap's top edge.</summary>
        public int BearingY;

        public int Advance;

        public int CellIndex;

        /// <summary>Index into the owning font's resolved fallback chain (0 = the primary font).</summary>
        public int SourceFontIndex;

        public bool Valid;

        public bool HasBitmap => Width > 0 && Height > 0;
    }
}