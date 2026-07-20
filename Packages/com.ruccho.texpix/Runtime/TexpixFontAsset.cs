using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace Texpix
{
    /// <summary>
    ///     A pixel font with a dynamically populated 2bpp atlas. Glyphs are rasterized on
    ///     demand with GlyphRenderMode.RASTER at the font's native pixel size, classified
    ///     into fill/outline levels and packed into the grid atlas.
    /// </summary>
    public enum TexpixAtlasMode
    {
        Dynamic = 0,
        Static = 1
    }

    [CreateAssetMenu(fileName = "TexpixFontAsset", menuName = "Texpix/Font Asset")]
    public sealed class TexpixFontAsset : ScriptableObject, ITexpixFontSource
    {
        /// <summary>The font face currently loaded into the (global) FontEngine.</summary>
        private static TexpixFontAsset _sActiveFace;

        [SerializeField] private Font sourceFont;
        [SerializeField] [Min(4)] private int pixelSize = 10;
        [SerializeField] private TexpixAtlasMode atlasMode = TexpixAtlasMode.Dynamic;
        [SerializeField] private TexpixFontAsset[] fallbackFonts = Array.Empty<TexpixFontAsset>();
        [SerializeField] private int atlasWidth = 256;
        [SerializeField] private int atlasMaxHeight = 4096;

        [SerializeField] [TextArea(2, 6)] private string bakeCharacterSet =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

        [SerializeField] [HideInInspector] private Texture2D bakedAtlasTexture;
        [SerializeField] [HideInInspector] private BakedGlyph[] bakedGlyphs = Array.Empty<BakedGlyph>();

        [SerializeField] [HideInInspector]
        private BakedKerningPair[] bakedKerningPairs = Array.Empty<BakedKerningPair>();

        [SerializeField] [HideInInspector] private int bakedAscent;
        [SerializeField] [HideInInspector] private int bakedDescent;
        [SerializeField] [HideInInspector] private int bakedLineHeight;
        [NonSerialized] private TexpixAtlas _atlas;
        [NonSerialized] private Dictionary<uint, TexpixGlyph> _glyphs;

        [NonSerialized] private bool _initialized;
        [NonSerialized] private Dictionary<ulong, int> _kerning;
        [NonSerialized] private List<TexpixFontAsset> _resolvedChain;
        [NonSerialized] private List<GlyphRect> _scratchFreeRects;
        [NonSerialized] private Texture2D _scratchTexture;
        [NonSerialized] private List<GlyphRect> _scratchUsedRects;

        public Font SourceFont => sourceFont;
        public int PixelSize => pixelSize;
        public TexpixAtlasMode AtlasMode => atlasMode;

        /// <summary>True when the asset can produce glyphs (source font present, or baked data in static mode).</summary>
        public bool IsReady => atlasMode == TexpixAtlasMode.Static ? bakedAtlasTexture != null : sourceFont != null;

        public Texture2D AtlasTexture
        {
            get
            {
                EnsureInitialized();
                return atlasMode == TexpixAtlasMode.Static ? bakedAtlasTexture : _atlas.Texture;
            }
        }

        /// <summary>
        ///     This font followed by its fallback fonts, depth-first and cycle-free.
        ///     Glyph lookups walk this list; <see cref="TexpixGlyph.SourceFontIndex" />
        ///     indexes into it.
        /// </summary>
        public IReadOnlyList<TexpixFontAsset> ResolvedChain
        {
            get
            {
                BuildChain();
                return _resolvedChain;
            }
        }

        private void OnDisable()
        {
            ReleaseRuntimeState();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_initialized)
            {
                ReleaseRuntimeState();
                AtlasChanged?.Invoke();
            }
        }
#endif
        public int Ascent { get; private set; }
        public int Descent { get; private set; }
        public int LineHeight { get; private set; }

        /// <summary>Resolves a glyph through this font and its fallback chain.</summary>
        public bool TryGetGlyph(uint unicode, out TexpixGlyph glyph)
        {
            BuildChain();
            for (var i = 0; i < _resolvedChain.Count; i++)
                if (_resolvedChain[i].TryGetGlyphLocal(unicode, out glyph))
                {
                    glyph.SourceFontIndex = i;
                    return true;
                }

            glyph = default;
            return false;
        }

        /// <summary>Kerning x-advance adjustment for a glyph index pair, in font pixels.</summary>
        public int GetKerning(uint leftGlyphIndex, uint rightGlyphIndex)
        {
            EnsureInitialized();
            if (_kerning == null)
                LoadKerning();
            return _kerning.GetValueOrDefault(((ulong)leftGlyphIndex << 32) | rightGlyphIndex, 0);
        }

        /// <summary>Raised when the atlas texture changes (new glyphs or texture recreation).</summary>
        public event Action AtlasChanged;

        public static TexpixFontAsset Create(Font font, int pixelSize, int atlasWidth = 256, int atlasMaxHeight = 4096)
        {
            var asset = CreateInstance<TexpixFontAsset>();
            asset.sourceFont = font;
            asset.pixelSize = pixelSize;
            asset.atlasWidth = atlasWidth;
            asset.atlasMaxHeight = atlasMaxHeight;
            return asset;
        }

        private void BuildChain()
        {
            if (_resolvedChain != null)
                return;
            _resolvedChain = new List<TexpixFontAsset>();
            var visited = new HashSet<TexpixFontAsset>();

            void Add(TexpixFontAsset asset)
            {
                if (asset == null || !visited.Add(asset))
                    return;
                _resolvedChain.Add(asset);
                foreach (var fallback in asset.fallbackFonts)
                    Add(fallback);
            }

            Add(this);
        }

        private void EnsureInitialized()
        {
            if (atlasMode == TexpixAtlasMode.Static)
            {
                if (_initialized)
                    return;
                if (bakedAtlasTexture == null)
                    throw new InvalidOperationException(
                        $"TexpixFontAsset '{name}' is static but has no baked atlas. Bake it from the inspector.");

                Ascent = bakedAscent;
                Descent = bakedDescent;
                LineHeight = bakedLineHeight;
                _glyphs = new Dictionary<uint, TexpixGlyph>(bakedGlyphs.Length);
                foreach (var baked in bakedGlyphs)
                    _glyphs[baked.unicode] = new TexpixGlyph
                    {
                        Unicode = baked.unicode,
                        GlyphIndex = baked.glyphIndex,
                        AtlasX = baked.atlasX,
                        AtlasY = baked.atlasY,
                        Width = baked.width,
                        Height = baked.height,
                        BearingX = baked.bearingX,
                        BearingY = baked.bearingY,
                        Advance = baked.advance,
                        CellIndex = -1,
                        Valid = true
                    };
                _kerning = new Dictionary<ulong, int>(bakedKerningPairs.Length);
                foreach (var pair in bakedKerningPairs)
                    _kerning[((ulong)pair.left << 32) | pair.right] = pair.xAdvance;
                _initialized = true;
                return;
            }

            if (_initialized && _atlas != null)
                return;

            if (sourceFont == null)
                throw new InvalidOperationException($"TexpixFontAsset '{name}' has no source font.");

            FontEngine.InitializeFontEngine();
            LoadFace();

            var face = FontEngine.GetFaceInfo();
            Ascent = Mathf.RoundToInt(face.ascentLine);
            Descent = Mathf.RoundToInt(face.descentLine);
            LineHeight = Mathf.Max(1, Mathf.RoundToInt(face.lineHeight));

            // 1px outline padding on each side; +2 vertical slack for glyphs that
            // exceed ascent-descent (rare but possible in stylized fonts).
            var cellWidth = pixelSize + 2;
            var cellHeight = Ascent - Descent + 4;
            _atlas?.Dispose();
            _atlas = new TexpixAtlas(cellWidth, cellHeight, atlasWidth, cellHeight * 4, atlasMaxHeight);
            _atlas.TextureRecreated += () => AtlasChanged?.Invoke();

            _glyphs = new Dictionary<uint, TexpixGlyph>();
            _kerning = null;
            _initialized = true;
        }

        private void LoadFace()
        {
            var error = FontEngine.LoadFontFace(sourceFont, pixelSize, 0);
            if (error != FontEngineError.Success && sourceFont.fontNames is { Length: > 0 })
                // Dynamic OS fonts carry no font data; retry via the system family name.
                error = FontEngine.LoadFontFace(sourceFont.fontNames[0], "Regular", pixelSize);
            if (error != FontEngineError.Success)
                throw new InvalidOperationException($"TexpixFontAsset '{name}': LoadFontFace failed with {error}.");
            _sActiveFace = this;
        }

        private void EnsureFaceLoaded()
        {
            if (_sActiveFace != this)
                LoadFace();
        }

        private bool TryGetGlyphLocal(uint unicode, out TexpixGlyph glyph)
        {
            glyph = default;
            if (!IsReady)
                return false;

            EnsureInitialized();
            if (_glyphs.TryGetValue(unicode, out glyph))
                return glyph.Valid;

            if (atlasMode == TexpixAtlasMode.Static)
                return false;

            // No AtlasChanged here: glyph writes update the texture in place, so
            // existing meshes stay valid. Only texture recreation and atlas resets notify.
            glyph = AddGlyph(unicode);
            _glyphs[unicode] = glyph;
            return glyph.Valid;
        }

        private void LoadKerning()
        {
            EnsureFaceLoaded();
            _kerning = new Dictionary<ulong, int>();
            var records = FontEngineBridge.GetAllPairAdjustmentRecords();
            if (records == null)
                return;
            foreach (var record in records)
            {
                var xAdvance = Mathf.RoundToInt(record.firstAdjustmentRecord.glyphValueRecord.xAdvance);
                if (xAdvance == 0)
                    continue;
                var key = ((ulong)record.firstAdjustmentRecord.glyphIndex << 32) |
                          record.secondAdjustmentRecord.glyphIndex;
                _kerning[key] = xAdvance;
            }
        }

        private TexpixGlyph AddGlyph(uint unicode)
        {
            EnsureFaceLoaded();

            if (!FontEngine.TryGetGlyphIndex(unicode, out var glyphIndex) || glyphIndex == 0)
                return default;

            if (!FontEngine.TryGetGlyphWithIndexValue(glyphIndex, GlyphLoadFlags.LOAD_DEFAULT, out var info))
                return default;

            var glyph = new TexpixGlyph
            {
                Unicode = unicode,
                GlyphIndex = glyphIndex,
                Advance = Mathf.Max(0, Mathf.RoundToInt(info.metrics.horizontalAdvance)),
                CellIndex = -1,
                Valid = true
            };

            var glyphWidth = Mathf.RoundToInt(info.metrics.width);
            var glyphHeight = Mathf.RoundToInt(info.metrics.height);
            if (glyphWidth <= 0 || glyphHeight <= 0)
                return glyph; // advance-only glyph (e.g. space)

            EnsureScratch();
            var scratchData = _scratchTexture.GetPixelData<byte>(0);
            for (var i = 0; i < scratchData.Length; i++)
                scratchData[i] = 0;
            _scratchFreeRects.Clear();
            _scratchFreeRects.Add(new GlyphRect(0, 0, _scratchTexture.width - 1, _scratchTexture.height - 1));
            _scratchUsedRects.Clear();

            if (!FontEngineBridge.TryAddGlyphToTexture(glyphIndex, 0, GlyphPackingMode.BestShortSideFit,
                    _scratchFreeRects, _scratchUsedRects, GlyphRenderMode.RASTER, _scratchTexture, out var rendered)
                || rendered == null)
            {
                Debug.LogWarning($"TexpixFontAsset '{name}': failed to rasterize glyph U+{unicode:X4}.");
                return glyph;
            }

            var rect = rendered.glyphRect;
            if (rect.width <= 0 || rect.height <= 0)
                return glyph;

            var paddedWidth = rect.width + 2;
            var paddedHeight = rect.height + 2;
            if (paddedWidth > _atlas.CellWidthPx || paddedHeight > _atlas.CellHeightPx)
            {
                Debug.LogWarning(
                    $"TexpixFontAsset '{name}': glyph U+{unicode:X4} ({rect.width}x{rect.height}) exceeds atlas cell size; skipped.");
                return glyph;
            }

            var binary = new byte[rect.width * rect.height];
            for (var y = 0; y < rect.height; y++)
            {
                var srcRow = (rect.y + y) * _scratchTexture.width + rect.x;
                for (var x = 0; x < rect.width; x++)
                    binary[y * rect.width + x] = scratchData[srcRow + x] > 127 ? (byte)1 : (byte)0;
            }

            var levels = new byte[paddedWidth * paddedHeight];
            GlyphClassifier.Classify(binary, rect.width, rect.height, levels);

            if (!_atlas.TryAllocateCell(out var cellIndex, out var origin))
            {
                // Atlas exhausted: reset it and let referencing texts re-add the glyphs
                // they still need on the rebuild triggered by AtlasChanged. Thrashing
                // here means the max atlas size is too small for the visible text.
                Debug.LogWarning(
                    $"TexpixFontAsset '{name}': atlas exhausted (max height {atlasMaxHeight}px); resetting. Increase Atlas Max Height if this repeats.");
                _atlas.Clear();
                _glyphs.Clear();
                AtlasChanged?.Invoke();
                if (!_atlas.TryAllocateCell(out cellIndex, out origin))
                {
                    Debug.LogError($"TexpixFontAsset '{name}': atlas cannot fit even a single glyph.");
                    return glyph;
                }
            }

            _atlas.WriteGlyph(origin, levels, paddedWidth, paddedHeight);
            _atlas.ApplyIfDirty();

            glyph.AtlasX = origin.x;
            glyph.AtlasY = origin.y;
            glyph.Width = paddedWidth;
            glyph.Height = paddedHeight;
            glyph.BearingX = Mathf.RoundToInt(rendered.metrics.horizontalBearingX) - 1;
            glyph.BearingY = Mathf.RoundToInt(rendered.metrics.horizontalBearingY) + 1;
            glyph.CellIndex = cellIndex;
            return glyph;
        }

        private void EnsureScratch()
        {
            if (_scratchTexture != null)
                return;
            var size = Mathf.NextPowerOfTwo(Mathf.Max(_atlas.CellWidthPx, _atlas.CellHeightPx, 32));
            _scratchTexture = new Texture2D(size, size, TextureFormat.Alpha8, false, true)
            {
                name = "Texpix Scratch",
                hideFlags = HideFlags.HideAndDontSave
            };
            _scratchFreeRects = new List<GlyphRect>();
            _scratchUsedRects = new List<GlyphRect>();
        }

        private void ReleaseRuntimeState()
        {
            _atlas?.Dispose();
            _atlas = null;
            if (_scratchTexture != null)
            {
                if (Application.isPlaying)
                    Destroy(_scratchTexture);
                else
                    DestroyImmediate(_scratchTexture);
                _scratchTexture = null;
            }

            _glyphs = null;
            _kerning = null;
            _resolvedChain = null;
            _initialized = false;
            if (_sActiveFace == this)
                _sActiveFace = null;
        }

        [Serializable]
        public struct BakedGlyph
        {
            public uint unicode;
            public uint glyphIndex;
            public int atlasX;
            public int atlasY;
            public int width;
            public int height;
            public int bearingX;
            public int bearingY;
            public int advance;
        }

        [Serializable]
        public struct BakedKerningPair
        {
            public uint left;
            public uint right;
            public int xAdvance;
        }

#if UNITY_EDITOR
        /// <summary>
        ///     Bakes the character set into serialized static data (glyph table, kerning,
        ///     atlas texture stored as a sub-asset) and switches the asset to Static mode.
        /// </summary>
        public void Bake(string characters = null)
        {
            characters ??= bakeCharacterSet;
            if (sourceFont == null)
                throw new InvalidOperationException($"TexpixFontAsset '{name}': cannot bake without a source font.");

            ReleaseRuntimeState();
            var previousMode = atlasMode;
            atlasMode = TexpixAtlasMode.Dynamic;
            try
            {
                EnsureInitialized();

                var codepoints = new HashSet<uint> { 0xFFFD, 0x2026 };
                for (var i = 0; i < characters.Length; i++)
                {
                    var c = characters[i];
                    uint cp = c;
                    if (char.IsHighSurrogate(c) && i + 1 < characters.Length && char.IsLowSurrogate(characters[i + 1]))
                    {
                        cp = (uint)char.ConvertToUtf32(c, characters[i + 1]);
                        i++;
                    }

                    codepoints.Add(cp);
                }

                var glyphList = new List<BakedGlyph>(codepoints.Count);
                var bakedIndexes = new HashSet<uint>();
                foreach (var cp in codepoints)
                {
                    if (!TryGetGlyph(cp, out var glyph))
                        continue;
                    glyphList.Add(new BakedGlyph
                    {
                        unicode = glyph.Unicode,
                        glyphIndex = glyph.GlyphIndex,
                        atlasX = glyph.AtlasX,
                        atlasY = glyph.AtlasY,
                        width = glyph.Width,
                        height = glyph.Height,
                        bearingX = glyph.BearingX,
                        bearingY = glyph.BearingY,
                        advance = glyph.Advance
                    });
                    bakedIndexes.Add(glyph.GlyphIndex);
                }

                if (_kerning == null)
                    LoadKerning();
                var kerningList = new List<BakedKerningPair>();
                foreach (var pair in _kerning)
                {
                    var left = (uint)(pair.Key >> 32);
                    var right = (uint)pair.Key;
                    if (bakedIndexes.Contains(left) && bakedIndexes.Contains(right))
                        kerningList.Add(new BakedKerningPair { left = left, right = right, xAdvance = pair.Value });
                }

                var source = _atlas.Texture;
                var copy = new Texture2D(source.width, source.height, TextureFormat.R8, false, true)
                {
                    name = name + " Atlas",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                copy.SetPixelData(source.GetPixelData<byte>(0), 0);
                copy.Apply(false);

                if (EditorUtility.IsPersistent(this))
                {
                    if (bakedAtlasTexture != null)
                    {
                        AssetDatabase.RemoveObjectFromAsset(bakedAtlasTexture);
                        DestroyImmediate(bakedAtlasTexture, true);
                    }

                    AssetDatabase.AddObjectToAsset(copy, this);
                }

                bakedAtlasTexture = copy;
                bakedGlyphs = glyphList.ToArray();
                bakedKerningPairs = kerningList.ToArray();
                bakedAscent = Ascent;
                bakedDescent = Descent;
                bakedLineHeight = LineHeight;
                atlasMode = TexpixAtlasMode.Static;
            }
            catch
            {
                atlasMode = previousMode;
                throw;
            }
            finally
            {
                ReleaseRuntimeState();
            }

            EditorUtility.SetDirty(this);
            if (EditorUtility.IsPersistent(this))
                AssetDatabase.SaveAssets();
            AtlasChanged?.Invoke();
        }

        /// <summary>Removes baked data and switches back to Dynamic mode.</summary>
        public void ClearBaked()
        {
            ReleaseRuntimeState();
            if (bakedAtlasTexture != null)
            {
                if (EditorUtility.IsPersistent(this))
                    AssetDatabase.RemoveObjectFromAsset(bakedAtlasTexture);
                DestroyImmediate(bakedAtlasTexture, true);
                bakedAtlasTexture = null;
            }

            bakedGlyphs = Array.Empty<BakedGlyph>();
            bakedKerningPairs = Array.Empty<BakedKerningPair>();
            atlasMode = TexpixAtlasMode.Dynamic;
            EditorUtility.SetDirty(this);
            if (EditorUtility.IsPersistent(this))
                AssetDatabase.SaveAssets();
            AtlasChanged?.Invoke();
        }

        internal Texture2D BakedAtlasTextureForInspector => bakedAtlasTexture;
        internal int BakedGlyphCount => bakedGlyphs.Length;
        internal int BakedKerningCount => bakedKerningPairs.Length;
#endif
    }
}