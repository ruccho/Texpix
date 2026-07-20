namespace Texpix
{
    /// <summary>
    /// Glyph/metric provider consumed by the layout engine. Implemented by
    /// <see cref="TexpixFontAsset"/>; abstracted so layout can be unit-tested with
    /// deterministic fake fonts. All values are integer font pixels.
    /// </summary>
    public interface ITexpixFontSource
    {
        int Ascent { get; }
        int Descent { get; }
        int LineHeight { get; }
        bool TryGetGlyph(uint unicode, out TexpixGlyph glyph);
        /// <summary>Kerning x-advance adjustment for a glyph index pair.</summary>
        int GetKerning(uint leftGlyphIndex, uint rightGlyphIndex);
    }
}
