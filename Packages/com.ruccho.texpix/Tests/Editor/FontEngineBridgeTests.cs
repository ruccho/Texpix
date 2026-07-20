using System.Collections.Generic;
using NUnit.Framework;
using Texpix;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace Texpix.Tests
{
    public class FontEngineBridgeTests
    {
        [Test]
        public void EnsureBound_ResolvesAllInternalMembers()
        {
            Assert.DoesNotThrow(FontEngineBridge.EnsureBound);
        }

        [Test]
        public void TryAddGlyphToTexture_RendersSystemFontGlyph()
        {
            FontEngine.InitializeFontEngine();
            if (FontEngine.LoadFontFace("Arial", "Regular", 16f) != FontEngineError.Success)
            {
                Assert.Ignore("Arial not available on this system.");
                return;
            }

            Assert.That(FontEngine.TryGetGlyphIndex('A', out uint glyphIndex), Is.True);

            var texture = new Texture2D(64, 64, TextureFormat.Alpha8, false, true);
            var data = texture.GetPixelData<byte>(0);
            for (int i = 0; i < data.Length; i++)
                data[i] = 0;
            var freeRects = new List<GlyphRect> { new(0, 0, 63, 63) };
            var usedRects = new List<GlyphRect>();

            bool added = FontEngineBridge.TryAddGlyphToTexture(glyphIndex, 0, GlyphPackingMode.BestShortSideFit,
                freeRects, usedRects, GlyphRenderMode.RASTER, texture, out Glyph glyph);

            Assert.That(added, Is.True);
            Assert.That(glyph, Is.Not.Null);
            Assert.That(glyph.glyphRect.width, Is.GreaterThan(0));

            bool anyPixel = false;
            foreach (byte b in texture.GetPixelData<byte>(0))
            {
                if (b != 0)
                {
                    anyPixel = true;
                    break;
                }
            }
            Assert.That(anyPixel, Is.True, "no pixels were rasterized");

            Object.DestroyImmediate(texture);
        }

        [Test]
        public void GetAllPairAdjustmentRecords_ReturnsRecordsForKernedFont()
        {
            FontEngine.InitializeFontEngine();
            if (FontEngine.LoadFontFace("Arial", "Regular", 16f) != FontEngineError.Success)
            {
                Assert.Ignore("Arial not available on this system.");
                return;
            }

            var records = FontEngineBridge.GetAllPairAdjustmentRecords();
            Assert.That(records, Is.Not.Null);
            Assert.That(records.Length, Is.GreaterThan(0));
        }
    }
}
