using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Texpix.Tests
{
    public class TexpixFallbackTests
    {
        private static readonly List<TexpixQuad> Quads = new();

        [Test]
        public void FallbackGlyphs_CarryFontIndex_AndBreakKerningPairs()
        {
            var font = new FakeFontSource
            {
                FontIndexOverride =
                {
                    ['b'] = 1
                },
                Kerning =
                {
                    [('a', 'b')] = -2,
                    [('b', 'c')] = -2
                }
            };
            var settings = new TexpixLayoutSettings { MaxWidthPx = 1000, MaxHeightPx = 1000 };

            TexpixTextGenerator.Generate(font, "abc", in settings, Quads);

            Assert.That(Quads[0].FontIndex, Is.EqualTo(0));
            Assert.That(Quads[1].FontIndex, Is.EqualTo(1));
            Assert.That(Quads[2].FontIndex, Is.EqualTo(0));
            // Kerning must not apply across font boundaries: all advances plain.
            Assert.That(Quads[1].X, Is.EqualTo(4));
            Assert.That(Quads[2].X, Is.EqualTo(8));
        }

        [Test]
        public void FontAssetChain_ResolvesMissingGlyphsFromFallback()
        {
            var latin = Font.CreateDynamicFontFromOSFont("Arial", 16);
            var japanese = Font.CreateDynamicFontFromOSFont("MS Gothic", 16)
                           ?? Font.CreateDynamicFontFromOSFont("Yu Gothic", 16)
                           ?? Font.CreateDynamicFontFromOSFont("Meiryo", 16);
            if (latin == null || japanese == null)
            {
                Assert.Ignore("Required OS fonts not available.");
                return;
            }

            var primary = TexpixFontAsset.Create(latin, 16);
            var fallback = TexpixFontAsset.Create(japanese, 16);
            var so = new SerializedObject(primary);
            var prop = so.FindProperty("fallbackFonts");
            prop.arraySize = 1;
            prop.GetArrayElementAtIndex(0).objectReferenceValue = fallback;
            so.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                Assert.That(primary.ResolvedChain.Count, Is.EqualTo(2));

                // Latin glyph from the primary font.
                Assert.That(primary.TryGetGlyph('A', out var a), Is.True);
                Assert.That(a.SourceFontIndex, Is.EqualTo(0));

                // 'あ' is missing in Arial → resolved by the fallback.
                Assert.That(primary.TryGetGlyph(0x3042, out var hira), Is.True);
                Assert.That(hira.SourceFontIndex, Is.EqualTo(1));
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
            var osFont = Font.CreateDynamicFontFromOSFont("Arial", 16);
            if (osFont == null)
            {
                Assert.Ignore("Arial not available on this system.");
                return;
            }

            // Tiny atlas: forces exhaustion after a handful of glyphs.
            var asset = TexpixFontAsset.Create(osFont, 16, 32, 96);
            try
            {
                var chars = "ABCDEFGHIJK";
                foreach (var c in chars)
                    Assert.That(asset.TryGetGlyph(c, out _), Is.True, $"glyph '{c}' failed");

                // The last glyph must still be resolvable, and re-requesting an evicted
                // glyph re-rasterizes it (possibly triggering another reset).
                Assert.That(asset.TryGetGlyph('K', out var k), Is.True);
                Assert.That(k.HasBitmap, Is.True);
                Assert.That(asset.TryGetGlyph('A', out var a), Is.True);
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