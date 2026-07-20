using System.Collections.Generic;
using NUnit.Framework;
using Texpix;
using UnityEngine;

namespace Texpix.Tests
{
    public class TexpixFallbackTests
    {
        static readonly List<TexpixQuad> quads = new();

        [Test]
        public void FallbackGlyphs_CarryFontIndex_AndBreakKerningPairs()
        {
            var font = new FakeFontSource();
            font.fontIndexOverride['b'] = 1;
            font.kerning[('a', 'b')] = -2;
            font.kerning[('b', 'c')] = -2;
            var settings = new TexpixLayoutSettings { maxWidthPx = 1000, maxHeightPx = 1000 };

            TexpixTextGenerator.Generate(font, "abc", in settings, quads);

            Assert.That(quads[0].fontIndex, Is.EqualTo(0));
            Assert.That(quads[1].fontIndex, Is.EqualTo(1));
            Assert.That(quads[2].fontIndex, Is.EqualTo(0));
            // Kerning must not apply across font boundaries: all advances plain.
            Assert.That(quads[1].x, Is.EqualTo(4));
            Assert.That(quads[2].x, Is.EqualTo(8));
        }

        [Test]
        public void FontAssetChain_ResolvesMissingGlyphsFromFallback()
        {
            Font latin = Font.CreateDynamicFontFromOSFont("Arial", 16);
            Font japanese = Font.CreateDynamicFontFromOSFont("MS Gothic", 16)
                            ?? Font.CreateDynamicFontFromOSFont("Yu Gothic", 16)
                            ?? Font.CreateDynamicFontFromOSFont("Meiryo", 16);
            if (latin == null || japanese == null)
            {
                Assert.Ignore("Required OS fonts not available.");
                return;
            }

            TexpixFontAsset primary = TexpixFontAsset.Create(latin, 16);
            TexpixFontAsset fallback = TexpixFontAsset.Create(japanese, 16);
            var so = new UnityEditor.SerializedObject(primary);
            var prop = so.FindProperty("fallbackFonts");
            prop.arraySize = 1;
            prop.GetArrayElementAtIndex(0).objectReferenceValue = fallback;
            so.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                Assert.That(primary.ResolvedChain.Count, Is.EqualTo(2));

                // Latin glyph from the primary font.
                Assert.That(primary.TryGetGlyph('A', out TexpixGlyph a), Is.True);
                Assert.That(a.sourceFontIndex, Is.EqualTo(0));

                // 'あ' is missing in Arial → resolved by the fallback.
                Assert.That(primary.TryGetGlyph(0x3042, out TexpixGlyph hira), Is.True);
                Assert.That(hira.sourceFontIndex, Is.EqualTo(1));
                Assert.That(hira.HasBitmap, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(primary);
                Object.DestroyImmediate(fallback);
                Object.DestroyImmediate(latin);
                Object.DestroyImmediate(japanese);
            }
        }

        [Test]
        public void AtlasExhaustion_ResetsAndKeepsServingGlyphs()
        {
            Font osFont = Font.CreateDynamicFontFromOSFont("Arial", 16);
            if (osFont == null)
            {
                Assert.Ignore("Arial not available on this system.");
                return;
            }

            // Tiny atlas: forces exhaustion after a handful of glyphs.
            TexpixFontAsset asset = TexpixFontAsset.Create(osFont, 16, atlasWidth: 32, atlasMaxHeight: 96);
            try
            {
                string chars = "ABCDEFGHIJK";
                foreach (char c in chars)
                    Assert.That(asset.TryGetGlyph(c, out _), Is.True, $"glyph '{c}' failed");

                // The last glyph must still be resolvable, and re-requesting an evicted
                // glyph re-rasterizes it (possibly triggering another reset).
                Assert.That(asset.TryGetGlyph('K', out TexpixGlyph k), Is.True);
                Assert.That(k.HasBitmap, Is.True);
                Assert.That(asset.TryGetGlyph('A', out TexpixGlyph a), Is.True);
                Assert.That(a.HasBitmap, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(osFont);
            }
        }
    }
}
