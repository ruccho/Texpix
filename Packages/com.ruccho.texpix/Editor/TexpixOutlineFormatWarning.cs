using System.Collections.Generic;

namespace Texpix.Editor
{
    /// <summary>
    ///     Detects outline settings that cannot take effect because a font in the chain uses
    ///     <see cref="TexpixAtlasFormat.FillOnly" />, which stores no outline pixels for the
    ///     shader to resolve. Kept free of GUI so the wording and the conditions can be tested.
    /// </summary>
    internal static class TexpixOutlineFormatWarning
    {
        /// <summary>The warning to show for these settings, or null when the outline works.</summary>
        internal static string For(TexpixOutlineMode outlineMode, TexpixFontAsset font)
        {
            if (outlineMode == TexpixOutlineMode.None || font == null)
                return null;

            if (font.AtlasFormat == TexpixAtlasFormat.FillOnly)
                return $"Outline Mode has no effect: font asset '{font.name}' uses the Fill Only atlas " +
                       "format, which stores no outline pixels. Set its Atlas Format to Outline, or set " +
                       "Outline Mode to None.";

            // The primary font can draw the outline but a fallback may still not be able to,
            // and the glyphs it supplies would silently lose it.
            List<string> fallbacks = null;
            var chain = font.ResolvedChain;
            for (var i = 1; i < chain.Count; i++)
            {
                var asset = chain[i];
                if (asset == null || asset.AtlasFormat != TexpixAtlasFormat.FillOnly)
                    continue;
                fallbacks ??= new List<string>();
                if (!fallbacks.Contains(asset.name))
                    fallbacks.Add(asset.name);
            }

            if (fallbacks == null)
                return null;

            return "The outline will be missing on glyphs supplied by these fallback fonts, which use the " +
                   $"Fill Only atlas format: '{string.Join("', '", fallbacks)}'. Glyphs from the primary " +
                   "font are unaffected.";
        }
    }
}
