using NUnit.Framework;
using UnityEngine;

namespace Texpix.Tests
{
    public class TexpixFontAssetBakeTests
    {
        [Test]
        public void Bake_ProducesStaticAssetServingGlyphs()
        {
            var osFont = Font.CreateDynamicFontFromOSFont("Arial", 16);
            if (osFont == null)
            {
                Assert.Ignore("Arial not available on this system.");
                return;
            }

            var asset = TexpixFontAsset.Create(osFont, 16);
            try
            {
                asset.Bake("AB");

                Assert.That(asset.AtlasMode, Is.EqualTo(TexpixAtlasMode.Static));
                Assert.That(asset.IsReady, Is.True);
                Assert.That(asset.AtlasTexture, Is.Not.Null);

                // Baked glyphs resolve without a live FontEngine face.
                Assert.That(asset.TryGetGlyph('A', out var a), Is.True);
                Assert.That(a.HasBitmap, Is.True);
                Assert.That(asset.TryGetGlyph('B', out _), Is.True);
                // Not baked and static → no dynamic fallback.
                Assert.That(asset.TryGetGlyph('Z', out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(osFont);
            }
        }
    }
}