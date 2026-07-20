using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Texpix.Tests
{
    public class TexpixFaceStateTests
    {
        /// <summary>
        /// The FontEngine face is process-global: Unity's own text systems switch it
        /// between our calls. New glyphs added after such a switch must still
        /// rasterize against the asset's own face (regression for stale-face bug).
        /// </summary>
        [Test]
        public void ExternalFaceSwitch_DoesNotCorruptNewGlyphs()
        {
            var arial = Font.CreateDynamicFontFromOSFont("Arial", 16);
            if (arial == null)
            {
                Assert.Ignore("Arial not available on this system.");
                return;
            }

            var poisoned = TexpixFontAsset.Create(arial, 16);
            var clean = TexpixFontAsset.Create(arial, 16);
            try
            {
                // Expected metrics from an untouched asset.
                Assert.That(clean.TryGetGlyph('W', out var expected), Is.True);

                // Initialize the asset, then simulate another system switching the
                // global face to a very different font/size before a new glyph is added.
                Assert.That(poisoned.TryGetGlyph('A', out _), Is.True);
                if (FontEngine.LoadFontFace("Times New Roman", "Regular", 64f) != FontEngineError.Success &&
                    FontEngine.LoadFontFace("Courier New", "Regular", 64f) != FontEngineError.Success)
                {
                    Assert.Ignore("No second system font available for the face switch.");
                    return;
                }

                Assert.That(poisoned.TryGetGlyph('W', out var actual), Is.True);
                Assert.That(actual.Advance, Is.EqualTo(expected.Advance), "advance came from the wrong face");
                Assert.That(actual.Width, Is.EqualTo(expected.Width), "bitmap width came from the wrong face");
                Assert.That(actual.Height, Is.EqualTo(expected.Height), "bitmap height came from the wrong face");
            }
            finally
            {
                Object.DestroyImmediate(poisoned);
                Object.DestroyImmediate(clean);
                Object.DestroyImmediate(arial);
            }
        }
    }
}
