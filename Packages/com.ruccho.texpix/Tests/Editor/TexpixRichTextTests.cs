using System.Collections.Generic;
using NUnit.Framework;
using Texpix;
using UnityEngine;

namespace Texpix.Tests
{
    public class TexpixRichTextTests
    {
        static readonly List<TexpixQuad> quads = new();
        static readonly List<TexpixQuad> spriteQuads = new();

        static TexpixSpriteAsset MakeSpriteAsset()
        {
            return TexpixSpriteAsset.Create(null, new[]
            {
                new TexpixSpriteAsset.Entry { name = "heart", x = 0, y = 0, width = 8, height = 8, bearingX = 0, bearingY = 8, advance = 9 },
                new TexpixSpriteAsset.Entry { name = "coin", x = 8, y = 0, width = 8, height = 8, bearingX = 0, bearingY = 8, advance = 9 },
            });
        }

        static TexpixTextMetrics Run(FakeFontSource font, string text, TexpixSpriteAsset sprites = null)
        {
            var settings = new TexpixLayoutSettings
            {
                maxWidthPx = 1000,
                maxHeightPx = 1000,
                wrapMode = TexpixWrapMode.NoWrap,
                richText = true,
                spriteAsset = sprites,
            };
            return TexpixTextGenerator.Generate(font, text, in settings, quads, spriteQuads);
        }

        [Test]
        public void ColorTag_AppliesToQuadsAndPops()
        {
            var font = new FakeFontSource();
            Run(font, "<color=#FF0000>a</color>b");

            Assert.That(quads.Count, Is.EqualTo(2));
            Assert.That(quads[0].color.r, Is.EqualTo(255));
            Assert.That(quads[0].color.g, Is.EqualTo(0));
            Assert.That(quads[1].color, Is.EqualTo(new Color32(255, 255, 255, 255)));
            // Tags take no layout space.
            Assert.That(quads[1].x, Is.EqualTo(4));
        }

        [Test]
        public void NestedColorTags_RestoreOuterColor()
        {
            var font = new FakeFontSource();
            Run(font, "<color=#FF0000>a<color=#00FF00>b</color>c</color>d");

            Assert.That(quads[0].color.r, Is.EqualTo(255));
            Assert.That(quads[1].color.g, Is.EqualTo(255));
            Assert.That(quads[1].color.r, Is.EqualTo(0));
            Assert.That(quads[2].color.r, Is.EqualTo(255)); // back to red
            Assert.That(quads[3].color, Is.EqualTo(new Color32(255, 255, 255, 255)));
        }

        [Test]
        public void SpriteTag_EmitsSpriteQuad_WithAdvance()
        {
            var font = new FakeFontSource();
            Run(font, "a<sprite=heart>b", MakeSpriteAsset());

            Assert.That(quads.Count, Is.EqualTo(2));
            Assert.That(spriteQuads.Count, Is.EqualTo(1));
            Assert.That(spriteQuads[0].x, Is.EqualTo(4));
            Assert.That(spriteQuads[0].atlasX, Is.EqualTo(0));
            Assert.That(quads[1].x, Is.EqualTo(4 + 9)); // after sprite advance
        }

        [Test]
        public void SpriteTag_TintAttribute_AppliesCurrentColor()
        {
            var font = new FakeFontSource();
            Run(font, "<color=#FF0000><sprite=heart tint=1><sprite=coin></color>", MakeSpriteAsset());

            Assert.That(spriteQuads.Count, Is.EqualTo(2));
            Assert.That(spriteQuads[0].color.r, Is.EqualTo(255));
            Assert.That(spriteQuads[0].color.g, Is.EqualTo(0));
            Assert.That(spriteQuads[1].color, Is.EqualTo(new Color32(255, 255, 255, 255)));
        }

        [Test]
        public void SpriteTag_ByIndex()
        {
            var font = new FakeFontSource();
            Run(font, "<sprite index=1>", MakeSpriteAsset());

            Assert.That(spriteQuads.Count, Is.EqualTo(1));
            Assert.That(spriteQuads[0].atlasX, Is.EqualTo(8)); // coin
        }

        [Test]
        public void UnknownSpriteName_RendersLiterally()
        {
            var font = new FakeFontSource();
            Run(font, "<sprite=nope>", MakeSpriteAsset());

            Assert.That(spriteQuads.Count, Is.EqualTo(0));
            Assert.That(quads.Count, Is.EqualTo("<sprite=nope>".Length));
        }

        [Test]
        public void Noparse_RendersTagsLiterally()
        {
            var font = new FakeFontSource();
            Run(font, "<noparse><color=#FF0000></noparse>a");

            // "<color=#FF0000>" rendered literally (15 glyphs) + 'a' in white.
            Assert.That(quads.Count, Is.EqualTo(16));
            Assert.That(quads[^1].color, Is.EqualTo(new Color32(255, 255, 255, 255)));
        }

        [Test]
        public void BrTag_BreaksLine()
        {
            var font = new FakeFontSource();
            var m = Run(font, "a<br>b");

            Assert.That(m.lineCount, Is.EqualTo(2));
            Assert.That(quads[1].y, Is.EqualTo(quads[0].y - 11));
        }

        [Test]
        public void UnknownTag_RendersLiterally()
        {
            var font = new FakeFontSource();
            Run(font, "<x>a");
            Assert.That(quads.Count, Is.EqualTo(4));
        }

        [Test]
        public void RichTextDisabled_TagsRenderLiterally()
        {
            var font = new FakeFontSource();
            var settings = new TexpixLayoutSettings { maxWidthPx = 1000, maxHeightPx = 1000, richText = false };
            TexpixTextGenerator.Generate(font, "<br>", in settings, quads, spriteQuads);
            Assert.That(quads.Count, Is.EqualTo(4));
        }

        [Test]
        public void SpriteQuads_ParticipateInWrapping()
        {
            var font = new FakeFontSource();
            var settings = new TexpixLayoutSettings
            {
                maxWidthPx = 12,
                maxHeightPx = 1000,
                wrapMode = TexpixWrapMode.Wrap,
                richText = true,
                spriteAsset = MakeSpriteAsset(),
            };
            // 'a' (4) + sprite (9) = 13 > 12 → sprite wraps to line 2.
            var m = TexpixTextGenerator.Generate(font, "a<sprite=heart>", in settings, quads, spriteQuads);

            Assert.That(m.lineCount, Is.EqualTo(2));
            Assert.That(spriteQuads[0].x, Is.EqualTo(0));
        }
    }
}
