using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Texpix
{
    /// <summary>
    ///     2bpp glyph atlas backed by a single-channel R8 texture. One texel packs
    ///     4 horizontal font pixels (2 bits each, LSB first). Space is managed as a grid
    ///     of fixed-size cells with O(1) free-list allocation. The texture's CPU pixel
    ///     buffer is the authoritative storage; call <see cref="ApplyIfDirty" /> to upload.
    /// </summary>
    public sealed class TexpixAtlas : IDisposable
    {
        private const int PixelsPerTexel = 4;
        private readonly int _cols;

        private readonly int _maxHeightPx;
        private readonly Stack<int> _releasedCells = new();
        private bool _dirty;
        private int _highWaterMark;
        private int _rows;

        public TexpixAtlas(int cellWidthPx, int cellHeightPx, int widthPx = 256, int initialHeightPx = 64,
            int maxHeightPx = 4096)
        {
            if (cellWidthPx <= 0 || cellHeightPx <= 0)
                throw new ArgumentOutOfRangeException(nameof(cellWidthPx), "Cell size must be positive.");

            CellWidthPx = AlignUp(cellWidthPx, PixelsPerTexel);
            CellHeightPx = cellHeightPx;
            WidthPx = AlignUp(Mathf.Max(widthPx, CellWidthPx), PixelsPerTexel);
            HeightPx = Mathf.Max(initialHeightPx, cellHeightPx);
            _maxHeightPx = Mathf.Max(maxHeightPx, HeightPx);
            _cols = WidthPx / CellWidthPx;
            _rows = HeightPx / CellHeightPx;
            Texture = CreateTexture(WidthPx, HeightPx);
        }

        public Texture2D Texture { get; private set; }

        /// <summary>Atlas width in font pixels (texture width × 4).</summary>
        public int WidthPx { get; }

        public int HeightPx { get; private set; }
        public int CellWidthPx { get; }
        public int CellHeightPx { get; }
        public int Capacity => _cols * _rows;

        public void Dispose()
        {
            DestroyTexture(Texture);
            Texture = null;
        }

        /// <summary>Raised when the texture object is replaced (atlas growth).</summary>
        public event Action TextureRecreated;

        private static int AlignUp(int value, int alignment)
        {
            return (value + alignment - 1) / alignment * alignment;
        }

        private static Texture2D CreateTexture(int widthPx, int heightPx)
        {
            var texture = new Texture2D(widthPx / PixelsPerTexel, heightPx, TextureFormat.R8, false, true)
            {
                name = "Texpix Atlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var data = texture.GetPixelData<byte>(0);
            UnsafeClear(data);
            texture.Apply(false);
            return texture;
        }

        private static unsafe void UnsafeClear(NativeArray<byte> data)
        {
            UnsafeUtility.MemClear(
                NativeArrayUnsafeUtility.GetUnsafePtr(data), data.Length);
        }

        /// <summary>
        ///     Allocates a cell, growing the atlas height if necessary.
        ///     Returns false when the atlas reached its maximum size.
        /// </summary>
        public bool TryAllocateCell(out int cellIndex, out Vector2Int originPx)
        {
            if (_releasedCells.Count > 0)
            {
                cellIndex = _releasedCells.Pop();
                originPx = CellOrigin(cellIndex);
                return true;
            }

            while (_highWaterMark >= Capacity)
                if (!Grow())
                {
                    cellIndex = -1;
                    originPx = default;
                    return false;
                }

            cellIndex = _highWaterMark++;
            originPx = CellOrigin(cellIndex);
            return true;
        }

        public void ReleaseCell(int cellIndex)
        {
            if (cellIndex < 0 || cellIndex >= _highWaterMark)
                throw new ArgumentOutOfRangeException(nameof(cellIndex));
            _releasedCells.Push(cellIndex);
        }

        private Vector2Int CellOrigin(int index)
        {
            return new Vector2Int(index % _cols * CellWidthPx, index / _cols * CellHeightPx);
        }

        private bool Grow()
        {
            var newHeightPx = Mathf.Min(HeightPx * 2, _maxHeightPx);
            if (newHeightPx <= HeightPx)
                return false;

            var newTexture = CreateTexture(WidthPx, newHeightPx);
            var src = Texture.GetPixelData<byte>(0);
            var dst = newTexture.GetPixelData<byte>(0);
            NativeArray<byte>.Copy(src, dst, src.Length);
            DestroyTexture(Texture);

            Texture = newTexture;
            HeightPx = newHeightPx;
            _rows = HeightPx / CellHeightPx;
            _dirty = true;
            TextureRecreated?.Invoke();
            return true;
        }

        /// <summary>
        ///     Writes a level bitmap (one byte per font pixel, values 0–3, row-major with
        ///     y=0 at the bottom) into the given cell origin. The rest of the cell is cleared.
        /// </summary>
        public void WriteGlyph(Vector2Int originPx, ReadOnlySpan<byte> levels, int w, int h)
        {
            if (w > CellWidthPx || h > CellHeightPx)
                throw new ArgumentException($"Glyph {w}x{h} exceeds cell {CellWidthPx}x{CellHeightPx}.");
            if (levels.Length < w * h)
                throw new ArgumentException("Level bitmap too small.", nameof(levels));

            var data = Texture.GetPixelData<byte>(0);
            var rowStride = WidthPx / PixelsPerTexel;

            // Cell origins and widths are texel-aligned, so the cell spans whole bytes.
            var firstByteX = originPx.x / PixelsPerTexel;
            var cellByteWidth = CellWidthPx / PixelsPerTexel;
            for (var y = 0; y < CellHeightPx; y++)
            {
                var rowStart = (originPx.y + y) * rowStride + firstByteX;
                for (var b = 0; b < cellByteWidth; b++)
                    data[rowStart + b] = 0;
            }

            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var level = levels[y * w + x];
                if (level == 0)
                    continue;
                var px = originPx.x + x;
                var py = originPx.y + y;
                var byteIndex = py * rowStride + (px >> 2);
                var shift = (px & 3) * 2;
                data[byteIndex] = (byte)(data[byteIndex] | ((level & 3) << shift));
            }

            _dirty = true;
        }

        /// <summary>Reads back the level of a single font pixel (test/debug helper).</summary>
        public byte GetLevel(int px, int py)
        {
            var data = Texture.GetPixelData<byte>(0);
            var byteIndex = py * (WidthPx / PixelsPerTexel) + (px >> 2);
            return (byte)((data[byteIndex] >> ((px & 3) * 2)) & 3);
        }

        /// <summary>Releases every cell and zeroes the texture (atlas reset on exhaustion).</summary>
        public void Clear()
        {
            _highWaterMark = 0;
            _releasedCells.Clear();
            var data = Texture.GetPixelData<byte>(0);
            UnsafeClear(data);
            _dirty = true;
        }

        public void ApplyIfDirty()
        {
            if (!_dirty)
                return;
            Texture.Apply(false);
            _dirty = false;
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(texture);
            else
                Object.DestroyImmediate(texture);
        }
    }
}