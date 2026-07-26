using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Texpix.Tests
{
    public class TexpixAtlasTests
    {
        [Test]
        public void CellWidth_IsAlignedToFourPixels()
        {
            using var atlas = new TexpixAtlas(10, 12, 64, 24);
            Assert.That(atlas.CellWidthPx, Is.EqualTo(12));
            Assert.That(atlas.Texture.width, Is.EqualTo(atlas.WidthPx / 4));
        }

        [Test]
        public void AllocatedCells_HaveDistinctAlignedOrigins()
        {
            using var atlas = new TexpixAtlas(8, 8, 32, 16);
            var seen = new HashSet<Vector2Int>();
            for (var i = 0; i < 8; i++)
            {
                Assert.That(atlas.TryAllocateCell(out _, out var origin), Is.True);
                Assert.That(origin.x % 4, Is.EqualTo(0));
                Assert.That(seen.Add(origin), Is.True, $"duplicate origin {origin}");
            }
        }

        [Test]
        public void Atlas_GrowsHeight_WhenFull()
        {
            using var atlas = new TexpixAtlas(8, 8, 16, 8, 64);
            var initialHeight = atlas.HeightPx;
            var initialTexture = atlas.Texture;

            // 16x8 atlas with 8x8 cells = 2 cells; the 3rd forces growth.
            Assert.That(atlas.TryAllocateCell(out _, out _), Is.True);
            Assert.That(atlas.TryAllocateCell(out _, out _), Is.True);
            Assert.That(atlas.TryAllocateCell(out _, out _), Is.True);

            Assert.That(atlas.HeightPx, Is.GreaterThan(initialHeight));
            Assert.That(atlas.Texture, Is.Not.SameAs(initialTexture));
        }

        [Test]
        public void Growth_PreservesWrittenData()
        {
            using var atlas = new TexpixAtlas(8, 8, 16, 8, 64);
            atlas.TryAllocateCell(out _, out var origin);
            var levels = new byte[] { 3, 2, 1, 0 };
            atlas.WriteGlyph(origin, levels, 2, 2);

            atlas.TryAllocateCell(out _, out _);
            atlas.TryAllocateCell(out _, out _); // forces growth

            Assert.That(atlas.GetLevel(origin.x + 0, origin.y + 0), Is.EqualTo(3));
            Assert.That(atlas.GetLevel(origin.x + 1, origin.y + 0), Is.EqualTo(2));
            Assert.That(atlas.GetLevel(origin.x + 0, origin.y + 1), Is.EqualTo(1));
            Assert.That(atlas.GetLevel(origin.x + 1, origin.y + 1), Is.EqualTo(0));
        }

        [Test]
        public void WriteGlyph_PacksTwoBitsPerPixel()
        {
            using var atlas = new TexpixAtlas(8, 8, 16, 8);
            atlas.TryAllocateCell(out _, out var origin);
            Assert.That(origin, Is.EqualTo(Vector2Int.zero));

            // One row: levels 3,2,1,0 → byte 0b00_01_10_11 = 0x1B.
            atlas.WriteGlyph(origin, new byte[] { 3, 2, 1, 0 }, 4, 1);
            var data = atlas.Texture.GetPixelData<byte>(0);
            Assert.That(data[0], Is.EqualTo(0x1B));
        }

        [Test]
        public void FillOnlyAtlas_PacksEightPixelsPerTexel()
        {
            using var atlas = new TexpixAtlas(8, 8, 16, 8, 4096, TexpixAtlasFormat.FillOnly);
            Assert.That(atlas.BitsPerPixel, Is.EqualTo(1));
            Assert.That(atlas.PixelsPerTexel, Is.EqualTo(8));
            Assert.That(atlas.Texture.width, Is.EqualTo(atlas.WidthPx / 8));

            atlas.TryAllocateCell(out _, out var origin);
            Assert.That(origin, Is.EqualTo(Vector2Int.zero));

            // One row of 8 pixels: set, clear, set, clear, … → byte 0b0101_0101 = 0x55.
            atlas.WriteGlyph(origin, new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 }, 8, 1);
            var data = atlas.Texture.GetPixelData<byte>(0);
            Assert.That(data[0], Is.EqualTo(0x55));

            Assert.That(atlas.GetLevel(0, 0), Is.EqualTo(1));
            Assert.That(atlas.GetLevel(1, 0), Is.EqualTo(0));
            Assert.That(atlas.GetLevel(6, 0), Is.EqualTo(1));
        }

        [Test]
        public void FillOnlyAtlas_AlignsCellWidthToEightPixels()
        {
            using var atlas = new TexpixAtlas(10, 12, 64, 24, 4096, TexpixAtlasFormat.FillOnly);
            Assert.That(atlas.CellWidthPx, Is.EqualTo(16));
            Assert.That(atlas.WidthPx % 8, Is.EqualTo(0));

            for (var i = 0; i < 4; i++)
            {
                Assert.That(atlas.TryAllocateCell(out _, out var origin), Is.True);
                Assert.That(origin.x % 8, Is.EqualTo(0));
            }
        }

        /// <summary>A fill-only atlas holds twice the font pixels per byte, so the same grid costs half.</summary>
        [Test]
        public void FillOnlyAtlas_HalvesTextureWidth()
        {
            using var outline = new TexpixAtlas(16, 12, 256, 24);
            using var fillOnly = new TexpixAtlas(16, 12, 256, 24, 4096, TexpixAtlasFormat.FillOnly);

            Assert.That(outline.WidthPx, Is.EqualTo(fillOnly.WidthPx));
            Assert.That(outline.Capacity, Is.EqualTo(fillOnly.Capacity));
            Assert.That(fillOnly.Texture.width * 2, Is.EqualTo(outline.Texture.width));
        }

        [Test]
        public void ReleasedCell_IsReused()
        {
            using var atlas = new TexpixAtlas(8, 8, 32, 16);
            atlas.TryAllocateCell(out var first, out var firstOrigin);
            atlas.TryAllocateCell(out _, out _);
            atlas.ReleaseCell(first);
            atlas.TryAllocateCell(out var reused, out var reusedOrigin);
            Assert.That(reused, Is.EqualTo(first));
            Assert.That(reusedOrigin, Is.EqualTo(firstOrigin));
        }

        [Test]
        public void TryAllocateCell_Fails_AtMaxHeight()
        {
            using var atlas = new TexpixAtlas(8, 8, 16, 8, 8);
            Assert.That(atlas.TryAllocateCell(out _, out _), Is.True);
            Assert.That(atlas.TryAllocateCell(out _, out _), Is.True);
            Assert.That(atlas.TryAllocateCell(out _, out _), Is.False);
        }
    }
}