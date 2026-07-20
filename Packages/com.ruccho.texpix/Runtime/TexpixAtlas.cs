using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Texpix
{
    /// <summary>
    /// 2bpp glyph atlas backed by a single-channel R8 texture. One texel packs
    /// 4 horizontal font pixels (2 bits each, LSB first). Space is managed as a grid
    /// of fixed-size cells with O(1) free-list allocation. The texture's CPU pixel
    /// buffer is the authoritative storage; call <see cref="ApplyIfDirty"/> to upload.
    /// </summary>
    public sealed class TexpixAtlas : IDisposable
    {
        public const int PixelsPerTexel = 4;

        public Texture2D Texture { get; private set; }
        /// <summary>Atlas width in font pixels (texture width × 4).</summary>
        public int WidthPx { get; }
        public int HeightPx { get; private set; }
        public int CellWidthPx { get; }
        public int CellHeightPx { get; }
        public int Capacity => cols * rows;

        readonly int maxHeightPx;
        readonly int cols;
        int rows;
        int highWaterMark;
        readonly Stack<int> releasedCells = new();
        bool dirty;

        /// <summary>Raised when the texture object is replaced (atlas growth).</summary>
        public event Action TextureRecreated;

        public TexpixAtlas(int cellWidthPx, int cellHeightPx, int widthPx = 256, int initialHeightPx = 64, int maxHeightPx = 4096)
        {
            if (cellWidthPx <= 0 || cellHeightPx <= 0)
                throw new ArgumentOutOfRangeException(nameof(cellWidthPx), "Cell size must be positive.");

            CellWidthPx = AlignUp(cellWidthPx, PixelsPerTexel);
            CellHeightPx = cellHeightPx;
            WidthPx = AlignUp(Mathf.Max(widthPx, CellWidthPx), PixelsPerTexel);
            HeightPx = Mathf.Max(initialHeightPx, cellHeightPx);
            this.maxHeightPx = Mathf.Max(maxHeightPx, HeightPx);
            cols = WidthPx / CellWidthPx;
            rows = HeightPx / CellHeightPx;
            Texture = CreateTexture(WidthPx, HeightPx);
        }

        static int AlignUp(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

        static Texture2D CreateTexture(int widthPx, int heightPx)
        {
            var texture = new Texture2D(widthPx / PixelsPerTexel, heightPx, TextureFormat.R8, false, true)
            {
                name = "Texpix Atlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var data = texture.GetPixelData<byte>(0);
            unsafe
            {
                UnsafeClear(data);
            }
            texture.Apply(false);
            return texture;
        }

        static unsafe void UnsafeClear(NativeArray<byte> data)
        {
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(
                Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(data), data.Length);
        }

        /// <summary>
        /// Allocates a cell, growing the atlas height if necessary.
        /// Returns false when the atlas reached its maximum size.
        /// </summary>
        public bool TryAllocateCell(out int cellIndex, out Vector2Int originPx)
        {
            if (releasedCells.Count > 0)
            {
                cellIndex = releasedCells.Pop();
                originPx = CellOrigin(cellIndex);
                return true;
            }

            while (highWaterMark >= Capacity)
            {
                if (!Grow())
                {
                    cellIndex = -1;
                    originPx = default;
                    return false;
                }
            }

            cellIndex = highWaterMark++;
            originPx = CellOrigin(cellIndex);
            return true;
        }

        public void ReleaseCell(int cellIndex)
        {
            if (cellIndex < 0 || cellIndex >= highWaterMark)
                throw new ArgumentOutOfRangeException(nameof(cellIndex));
            releasedCells.Push(cellIndex);
        }

        Vector2Int CellOrigin(int index) => new(index % cols * CellWidthPx, index / cols * CellHeightPx);

        bool Grow()
        {
            int newHeightPx = Mathf.Min(HeightPx * 2, maxHeightPx);
            if (newHeightPx <= HeightPx)
                return false;

            var newTexture = CreateTexture(WidthPx, newHeightPx);
            var src = Texture.GetPixelData<byte>(0);
            var dst = newTexture.GetPixelData<byte>(0);
            NativeArray<byte>.Copy(src, dst, src.Length);
            DestroyTexture(Texture);

            Texture = newTexture;
            HeightPx = newHeightPx;
            rows = HeightPx / CellHeightPx;
            dirty = true;
            TextureRecreated?.Invoke();
            return true;
        }

        /// <summary>
        /// Writes a level bitmap (one byte per font pixel, values 0–3, row-major with
        /// y=0 at the bottom) into the given cell origin. The rest of the cell is cleared.
        /// </summary>
        public void WriteGlyph(Vector2Int originPx, ReadOnlySpan<byte> levels, int w, int h)
        {
            if (w > CellWidthPx || h > CellHeightPx)
                throw new ArgumentException($"Glyph {w}x{h} exceeds cell {CellWidthPx}x{CellHeightPx}.");
            if (levels.Length < w * h)
                throw new ArgumentException("Level bitmap too small.", nameof(levels));

            var data = Texture.GetPixelData<byte>(0);
            int rowStride = WidthPx / PixelsPerTexel;

            // Cell origins and widths are texel-aligned, so the cell spans whole bytes.
            int firstByteX = originPx.x / PixelsPerTexel;
            int cellByteWidth = CellWidthPx / PixelsPerTexel;
            for (int y = 0; y < CellHeightPx; y++)
            {
                int rowStart = (originPx.y + y) * rowStride + firstByteX;
                for (int b = 0; b < cellByteWidth; b++)
                    data[rowStart + b] = 0;
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    byte level = levels[y * w + x];
                    if (level == 0)
                        continue;
                    int px = originPx.x + x;
                    int py = originPx.y + y;
                    int byteIndex = py * rowStride + (px >> 2);
                    int shift = (px & 3) * 2;
                    data[byteIndex] = (byte)(data[byteIndex] | ((level & 3) << shift));
                }
            }

            dirty = true;
        }

        /// <summary>Reads back the level of a single font pixel (test/debug helper).</summary>
        public byte GetLevel(int px, int py)
        {
            var data = Texture.GetPixelData<byte>(0);
            int byteIndex = py * (WidthPx / PixelsPerTexel) + (px >> 2);
            return (byte)((data[byteIndex] >> ((px & 3) * 2)) & 3);
        }

        /// <summary>Releases every cell and zeroes the texture (atlas reset on exhaustion).</summary>
        public void Clear()
        {
            highWaterMark = 0;
            releasedCells.Clear();
            var data = Texture.GetPixelData<byte>(0);
            unsafe
            {
                UnsafeClear(data);
            }
            dirty = true;
        }

        public void ApplyIfDirty()
        {
            if (!dirty)
                return;
            Texture.Apply(false);
            dirty = false;
        }

        public void Dispose()
        {
            DestroyTexture(Texture);
            Texture = null;
        }

        static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
