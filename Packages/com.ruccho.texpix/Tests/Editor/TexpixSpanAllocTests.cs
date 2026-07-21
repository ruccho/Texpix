using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace Texpix.Tests
{
    public class TexpixSpanAllocTests
    {
        private static readonly List<TexpixQuad> Quads = new();
        private static readonly List<TexpixQuad> SpriteQuads = new();

        private static TexpixSpriteAsset MakeSpriteAsset()
        {
            return TexpixSpriteAsset.Create(null, new[]
            {
                new TexpixSpriteAsset.Entry
                    { name = "heart", x = 0, y = 0, width = 8, height = 8, bearingY = 8, advance = 9 }
            });
        }

        private static TexpixLayoutSettings RichSettings(TexpixSpriteAsset sprites)
        {
            return new TexpixLayoutSettings
            {
                MaxWidthPx = 120,
                MaxHeightPx = 60,
                WrapMode = TexpixWrapMode.Wrap,
                Overflow = TexpixOverflowMode.Ellipsis,
                LetterSpacingPx = 1,
                RichText = true,
                SpriteAsset = sprites
            };
        }

        [Test]
        public void SpanOverload_MatchesStringOverload()
        {
            var font = new FakeFontSource();
            var settings = new TexpixLayoutSettings { MaxWidthPx = 100, MaxHeightPx = 100 };
            const string text = "aa bb\ncc";

            var fromString = TexpixTextGenerator.Generate(font, text, in settings, Quads);
            var stringQuadCount = Quads.Count;
            var firstX = Quads[0].X;

            var fromSpan = TexpixTextGenerator.Generate(font, text.AsSpan(), in settings, Quads);
            Assert.That(Quads.Count, Is.EqualTo(stringQuadCount));
            Assert.That(Quads[0].X, Is.EqualTo(firstX));
            Assert.That(fromSpan.LineCount, Is.EqualTo(fromString.LineCount));
            Assert.That(fromSpan.WidthPx, Is.EqualTo(fromString.WidthPx));
        }

        [Test]
        public void NamedColor_Parses()
        {
            var font = new FakeFontSource();
            var settings = new TexpixLayoutSettings { MaxWidthPx = 100, MaxHeightPx = 100, RichText = true };
            TexpixTextGenerator.Generate(font, "<color=red>a</color><color=teal>b</color>", in settings, Quads);

            Assert.That(Quads[0].Color, Is.EqualTo(new Color32(255, 0, 0, 255)));
            Assert.That(Quads[1].Color, Is.EqualTo(new Color32(0, 128, 128, 255)));
        }

        [Test]
        public void ShortAndAlphaHexColors_Parse()
        {
            var font = new FakeFontSource();
            var settings = new TexpixLayoutSettings { MaxWidthPx = 100, MaxHeightPx = 100, RichText = true };
            TexpixTextGenerator.Generate(font, "<color=#F00>a</color><color=#00FF0080>b</color>", in settings, Quads);

            Assert.That(Quads[0].Color, Is.EqualTo(new Color32(255, 0, 0, 255)));
            Assert.That(Quads[1].Color, Is.EqualTo(new Color32(0, 255, 0, 128)));
        }

        [Test]
        public void InvalidHexColor_RendersTagLiterally()
        {
            var font = new FakeFontSource();
            var settings = new TexpixLayoutSettings { MaxWidthPx = 1000, MaxHeightPx = 100, RichText = true };
            TexpixTextGenerator.Generate(font, "<color=#GG>a", in settings, Quads);

            Assert.That(Quads.Count, Is.EqualTo("<color=#GG>a".Length));
        }

        [TestCase("plain text abc とテキスト")]
        [TestCase("<color=#FF8800>a</color>")]
        [TestCase("<color=red>a</color>")]
        [TestCase("<sprite=heart tint=1>")]
        [TestCase("<noparse><x></noparse><br>")]
        [TestCase("折り返しと省略が発生する長さのテキストです。長い長い長い長い長い")]
        [TestCase("<color=#FF8800>色</color><color=red>名</color>付き <sprite=heart tint=1> 折り返しと省略が発生する長さのテキスト。ABC abc")]
        public void Generate_IsAllocationFree_AfterWarmup(string text)
        {
            var font = new FakeFontSource();
            var sprites = MakeSpriteAsset();
            var settings = RichSettings(sprites);

            try
            {
                for (var i = 0; i < 3; i++)
                    TexpixTextGenerator.Generate(font, text, in settings, Quads, SpriteQuads);

                Assert.That(() => { TexpixTextGenerator.Generate(font, text, in settings, Quads, SpriteQuads); },
                    Is.Not.AllocatingGCMemory());
            }
            finally
            {
                Object.DestroyImmediate(sprites);
            }
        }
    }
}