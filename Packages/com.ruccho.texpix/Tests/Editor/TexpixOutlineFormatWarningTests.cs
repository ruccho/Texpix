using NUnit.Framework;
using Texpix.Editor;
using UnityEditor;
using UnityEngine;

namespace Texpix.Tests
{
    public class TexpixOutlineFormatWarningTests
    {
        private static TexpixFontAsset CreateFont(string name, TexpixAtlasFormat format)
        {
            var asset = TexpixFontAsset.Create(null, 10, atlasFormat: format);
            asset.name = name;
            return asset;
        }

        /// <summary>Fallback fonts have no public setter; the inspector path writes them the same way.</summary>
        private static void SetFallbacks(TexpixFontAsset asset, params TexpixFontAsset[] fallbacks)
        {
            var so = new SerializedObject(asset);
            var array = so.FindProperty("fallbackFonts");
            array.arraySize = fallbacks.Length;
            for (var i = 0; i < fallbacks.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = fallbacks[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void NoWarning_WhenOutlineIsOff()
        {
            var font = CreateFont("FillOnlyFont", TexpixAtlasFormat.FillOnly);
            try
            {
                Assert.That(TexpixOutlineFormatWarning.For(TexpixOutlineMode.None, font), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(font);
            }
        }

        [Test]
        public void NoWarning_WhenFontIsMissing()
        {
            Assert.That(TexpixOutlineFormatWarning.For(TexpixOutlineMode.FourNeighbor, null), Is.Null);
        }

        [Test]
        public void NoWarning_ForAnOutlineFont()
        {
            var font = CreateFont("OutlineFont", TexpixAtlasFormat.Outline);
            try
            {
                Assert.That(TexpixOutlineFormatWarning.For(TexpixOutlineMode.EightNeighbor, font), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(font);
            }
        }

        [TestCase(TexpixOutlineMode.FourNeighbor)]
        [TestCase(TexpixOutlineMode.EightNeighbor)]
        public void Warns_WhenTheFontItselfIsFillOnly(TexpixOutlineMode mode)
        {
            var font = CreateFont("FillOnlyFont", TexpixAtlasFormat.FillOnly);
            try
            {
                var warning = TexpixOutlineFormatWarning.For(mode, font);

                Assert.That(warning, Is.Not.Null);
                Assert.That(warning, Does.Contain("FillOnlyFont"));
                Assert.That(warning, Does.Contain("no effect"));
            }
            finally
            {
                Object.DestroyImmediate(font);
            }
        }

        /// <summary>
        ///     An outline-capable primary font can still lose the outline on glyphs a fill-only
        ///     fallback supplies, which is a quieter failure than the primary case.
        /// </summary>
        [Test]
        public void Warns_WhenOnlyAFallbackIsFillOnly()
        {
            var primary = CreateFont("PrimaryOutline", TexpixAtlasFormat.Outline);
            var goodFallback = CreateFont("FallbackOutline", TexpixAtlasFormat.Outline);
            var badFallback = CreateFont("FallbackFillOnly", TexpixAtlasFormat.FillOnly);
            SetFallbacks(primary, goodFallback, badFallback);
            try
            {
                var warning = TexpixOutlineFormatWarning.For(TexpixOutlineMode.FourNeighbor, primary);

                Assert.That(warning, Is.Not.Null);
                Assert.That(warning, Does.Contain("FallbackFillOnly"));
                Assert.That(warning, Does.Not.Contain("FallbackOutline"));
                Assert.That(warning, Does.Not.Contain("PrimaryOutline"));
            }
            finally
            {
                Object.DestroyImmediate(primary);
                Object.DestroyImmediate(goodFallback);
                Object.DestroyImmediate(badFallback);
            }
        }

        /// <summary>A fill-only primary is the louder problem and must not be masked by a fallback note.</summary>
        [Test]
        public void PrimaryWarning_TakesPrecedenceOverFallbacks()
        {
            var primary = CreateFont("PrimaryFillOnly", TexpixAtlasFormat.FillOnly);
            var fallback = CreateFont("FallbackFillOnly", TexpixAtlasFormat.FillOnly);
            SetFallbacks(primary, fallback);
            try
            {
                var warning = TexpixOutlineFormatWarning.For(TexpixOutlineMode.FourNeighbor, primary);

                Assert.That(warning, Does.Contain("PrimaryFillOnly"));
                Assert.That(warning, Does.Not.Contain("FallbackFillOnly"));
            }
            finally
            {
                Object.DestroyImmediate(primary);
                Object.DestroyImmediate(fallback);
            }
        }

        /// <summary>A static asset renders from its baked format, so that is what the warning follows.</summary>
        [Test]
        public void FollowsTheBakedFormat_NotTheConfiguredOne()
        {
            var font = CreateFont("StaticOutlineBake", TexpixAtlasFormat.FillOnly);
            try
            {
                var so = new SerializedObject(font);
                so.FindProperty("atlasMode").enumValueIndex = (int)TexpixAtlasMode.Static;
                so.FindProperty("bakedAtlasFormat").enumValueIndex = (int)TexpixAtlasFormat.Outline;
                so.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(font.AtlasFormat, Is.EqualTo(TexpixAtlasFormat.Outline));
                Assert.That(TexpixOutlineFormatWarning.For(TexpixOutlineMode.FourNeighbor, font), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(font);
            }
        }
    }
}
