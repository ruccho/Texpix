using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Texpix.Tests
{
    public class TexpixRichTextTests
    {
        private static readonly List<TexpixQuad> Quads = new();
        private static readonly List<TexpixQuad> SpriteQuads = new();

        private static TexpixSpriteAsset MakeSpriteAsset()
        {
            return TexpixSpriteAsset.Create(null, new[]
            {
                new TexpixSpriteAsset.Entry
                    { name = "heart", x = 0, y = 0, width = 8, height = 8, bearingX = 0, bearingY = 8, advance = 9 },
                new TexpixSpriteAsset.Entry
                    { name = "coin", x = 8, y = 0, width = 8, height = 8, bearingX = 0, bearingY = 8, advance = 9 }
            });
        }

        private static TexpixTextMetrics Run(FakeFontSource font, string text, TexpixSpriteAsset sprites = null)
        {
            var settings = new TexpixLayoutSettings
            {
                MaxWidthPx = 1000,
                MaxHeightPx = 1000,
                WrapMode = TexpixWrapMode.NoWrap,
                RichText = true,
                SpriteAsset = sprites
            };
            return TexpixTextGenerator.Generate(font, text, in settings, Quads, SpriteQuads);
        }

        [Test]
        public void ColorTag_AppliesToQuadsAndPops()
        {
            var font = new FakeFontSource();
            Run(font, "<color=#FF0000>a</color>b");

            Assert.That(Quads.Count, Is.EqualTo(2));
            Assert.That(Quads[0].Color.r, Is.EqualTo(255));
            Assert.That(Quads[0].Color.g, Is.EqualTo(0));
            Assert.That(Quads[1].Color, Is.EqualTo(new Color32(255, 255, 255, 255)));
            // Tags take no layout space.
            Assert.That(Quads[1].X, Is.EqualTo(4));
        }

        [Test]
        public void NestedColorTags_RestoreOuterColor()
        {
            var font = new FakeFontSource();
            Run(font, "<color=#FF0000>a<color=#00FF00>b</color>c</color>d");

            Assert.That(Quads[0].Color.r, Is.EqualTo(255));
            Assert.That(Quads[1].Color.g, Is.EqualTo(255));
            Assert.That(Quads[1].Color.r, Is.EqualTo(0));
            Assert.That(Quads[2].Color.r, Is.EqualTo(255)); // back to red
            Assert.That(Quads[3].Color, Is.EqualTo(new Color32(255, 255, 255, 255)));
        }

        [Test]
        public void SpriteTag_EmitsSpriteQuad_WithAdvance()
        {
            var font = new FakeFontSource();
            Run(font, "a<sprite=heart>b", MakeSpriteAsset());

            Assert.That(Quads.Count, Is.EqualTo(2));
            Assert.That(SpriteQuads.Count, Is.EqualTo(1));
            Assert.That(SpriteQuads[0].X, Is.EqualTo(4));
            Assert.That(SpriteQuads[0].AtlasX, Is.EqualTo(0));
            Assert.That(Quads[1].X, Is.EqualTo(4 + 9)); // after sprite advance
        }

        [Test]
        public void SpriteTag_TintAttribute_AppliesCurrentColor()
        {
            var font = new FakeFontSource();
            Run(font, "<color=#FF0000><sprite=heart tint=1><sprite=coin></color>", MakeSpriteAsset());

            Assert.That(SpriteQuads.Count, Is.EqualTo(2));
            Assert.That(SpriteQuads[0].Color.r, Is.EqualTo(255));
            Assert.That(SpriteQuads[0].Color.g, Is.EqualTo(0));
            Assert.That(SpriteQuads[1].Color, Is.EqualTo(new Color32(255, 255, 255, 255)));
        }

        [Test]
        public void SpriteTag_ByIndex()
        {
            var font = new FakeFontSource();
            Run(font, "<sprite index=1>", MakeSpriteAsset());

            Assert.That(SpriteQuads.Count, Is.EqualTo(1));
            Assert.That(SpriteQuads[0].AtlasX, Is.EqualTo(8)); // coin
        }

        [Test]
        public void UnknownSpriteName_RendersLiterally()
        {
            var font = new FakeFontSource();
            Run(font, "<sprite=nope>", MakeSpriteAsset());

            Assert.That(SpriteQuads.Count, Is.EqualTo(0));
            Assert.That(Quads.Count, Is.EqualTo("<sprite=nope>".Length));
        }

        [Test]
        public void Noparse_RendersTagsLiterally()
        {
            var font = new FakeFontSource();
            Run(font, "<noparse><color=#FF0000></noparse>a");

            // "<color=#FF0000>" rendered literally (15 glyphs) + 'a' in white.
            Assert.That(Quads.Count, Is.EqualTo(16));
            Assert.That(Quads[^1].Color, Is.EqualTo(new Color32(255, 255, 255, 255)));
        }

        [Test]
        public void BrTag_BreaksLine()
        {
            var font = new FakeFontSource();
            var m = Run(font, "a<br>b");

            Assert.That(m.LineCount, Is.EqualTo(2));
            Assert.That(Quads[1].Y, Is.EqualTo(Quads[0].Y - 11));
        }

        [Test]
        public void UnknownTag_RendersLiterally()
        {
            var font = new FakeFontSource();
            Run(font, "<x>a");
            Assert.That(Quads.Count, Is.EqualTo(4));
        }

        [Test]
        public void RichTextDisabled_TagsRenderLiterally()
        {
            var font = new FakeFontSource();
            var settings = new TexpixLayoutSettings { MaxWidthPx = 1000, MaxHeightPx = 1000, RichText = false };
            TexpixTextGenerator.Generate(font, "<br>", in settings, Quads, SpriteQuads);
            Assert.That(Quads.Count, Is.EqualTo(4));
        }

        [Test]
        public void SpriteQuads_ParticipateInWrapping()
        {
            var font = new FakeFontSource();
            var settings = new TexpixLayoutSettings
            {
                MaxWidthPx = 12,
                MaxHeightPx = 1000,
                WrapMode = TexpixWrapMode.Wrap,
                RichText = true,
                SpriteAsset = MakeSpriteAsset()
            };
            // 'a' (4) + sprite (9) = 13 > 12 → sprite wraps to line 2.
            var m = TexpixTextGenerator.Generate(font, "a<sprite=heart>", in settings, Quads, SpriteQuads);

            Assert.That(m.LineCount, Is.EqualTo(2));
            Assert.That(SpriteQuads[0].X, Is.EqualTo(0));
        }
    }
}