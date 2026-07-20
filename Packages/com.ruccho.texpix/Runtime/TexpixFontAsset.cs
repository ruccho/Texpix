using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace Texpix
{
    /// <summary>
    /// A pixel font with a dynamically populated 2bpp atlas. Glyphs are rasterized on
    /// demand with GlyphRenderMode.RASTER at the font's native pixel size, classified
    /// into fill/outline levels and packed into the grid atlas.
    /// </summary>
    public enum TexpixAtlasMode
    {
        Dynamic = 0,
        Static = 1,
    }

    [CreateAssetMenu(fileName = "TexpixFontAsset", menuName = "Texpix/Font Asset")]
    public sealed class TexpixFontAsset : ScriptableObject, ITexpixFontSource
    {
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

        [SerializeField] Font sourceFont;
        [SerializeField, Min(4)] int pixelSize = 10;
        [SerializeField] TexpixAtlasMode atlasMode = TexpixAtlasMode.Dynamic;
        [SerializeField] TexpixFontAsset[] fallbackFonts = Array.Empty<TexpixFontAsset>();
        [SerializeField] int atlasWidth = 256;
        [SerializeField] int atlasMaxHeight = 4096;
        [SerializeField, TextArea(2, 6)] string bakeCharacterSet =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

        [SerializeField, HideInInspector] Texture2D bakedAtlasTexture;
        [SerializeField, HideInInspector] BakedGlyph[] bakedGlyphs = Array.Empty<BakedGlyph>();
        [SerializeField, HideInInspector] BakedKerningPair[] bakedKerningPairs = Array.Empty<BakedKerningPair>();
        [SerializeField, HideInInspector] int bakedAscent;
        [SerializeField, HideInInspector] int bakedDescent;
        [SerializeField, HideInInspector] int bakedLineHeight;

        [NonSerialized] bool initialized;
        [NonSerialized] TexpixAtlas atlas;
        [NonSerialized] Dictionary<uint, TexpixGlyph> glyphs;
        [NonSerialized] Dictionary<ulong, int> kerning;
        [NonSerialized] Texture2D scratchTexture;
        [NonSerialized] List<GlyphRect> scratchFreeRects;
        [NonSerialized] List<GlyphRect> scratchUsedRects;
        [NonSerialized] List<TexpixFontAsset> resolvedChain;

        /// <summary>The font face currently loaded into the (global) FontEngine.</summary>
        static TexpixFontAsset s_ActiveFace;

        public Font SourceFont => sourceFont;
        public int PixelSize => pixelSize;
        public TexpixAtlasMode AtlasMode => atlasMode;
        public int Ascent { get; private set; }
        public int Descent { get; private set; }
        public int LineHeight { get; private set; }

        /// <summary>True when the asset can produce glyphs (source font present, or baked data in static mode).</summary>
        public bool IsReady => atlasMode == TexpixAtlasMode.Static ? bakedAtlasTexture != null : sourceFont != null;

        public Texture2D AtlasTexture
        {
            get
            {
                EnsureInitialized();
                return atlasMode == TexpixAtlasMode.Static ? bakedAtlasTexture : atlas.Texture;
            }
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

        /// <summary>
        /// This font followed by its fallback fonts, depth-first and cycle-free.
        /// Glyph lookups walk this list; <see cref="TexpixGlyph.sourceFontIndex"/>
        /// indexes into it.
        /// </summary>
        public IReadOnlyList<TexpixFontAsset> ResolvedChain
        {
            get
            {
                BuildChain();
                return resolvedChain;
            }
        }

        void BuildChain()
        {
            if (resolvedChain != null)
                return;
            resolvedChain = new List<TexpixFontAsset>();
            var visited = new HashSet<TexpixFontAsset>();
            void Add(TexpixFontAsset asset)
            {
                if (asset == null || !visited.Add(asset))
                    return;
                resolvedChain.Add(asset);
                foreach (TexpixFontAsset fallback in asset.fallbackFonts)
                    Add(fallback);
            }
            Add(this);
        }

        void EnsureInitialized()
        {
            if (atlasMode == TexpixAtlasMode.Static)
            {
                if (initialized)
                    return;
                if (bakedAtlasTexture == null)
                    throw new InvalidOperationException($"TexpixFontAsset '{name}' is static but has no baked atlas. Bake it from the inspector.");

                Ascent = bakedAscent;
                Descent = bakedDescent;
                LineHeight = bakedLineHeight;
                glyphs = new Dictionary<uint, TexpixGlyph>(bakedGlyphs.Length);
                foreach (BakedGlyph baked in bakedGlyphs)
                {
                    glyphs[baked.unicode] = new TexpixGlyph
                    {
                        unicode = baked.unicode,
                        glyphIndex = baked.glyphIndex,
                        atlasX = baked.atlasX,
                        atlasY = baked.atlasY,
                        width = baked.width,
                        height = baked.height,
                        bearingX = baked.bearingX,
                        bearingY = baked.bearingY,
                        advance = baked.advance,
                        cellIndex = -1,
                        valid = true,
                    };
                }
                kerning = new Dictionary<ulong, int>(bakedKerningPairs.Length);
                foreach (BakedKerningPair pair in bakedKerningPairs)
                    kerning[((ulong)pair.left << 32) | pair.right] = pair.xAdvance;
                initialized = true;
                return;
            }

            if (initialized && atlas != null)
                return;

            if (sourceFont == null)
                throw new InvalidOperationException($"TexpixFontAsset '{name}' has no source font.");

            FontEngine.InitializeFontEngine();
            LoadFace();

            FaceInfo face = FontEngine.GetFaceInfo();
            Ascent = Mathf.RoundToInt(face.ascentLine);
            Descent = Mathf.RoundToInt(face.descentLine);
            LineHeight = Mathf.Max(1, Mathf.RoundToInt(face.lineHeight));

            // 1px outline padding on each side; +2 vertical slack for glyphs that
            // exceed ascent-descent (rare but possible in stylized fonts).
            int cellWidth = pixelSize + 2;
            int cellHeight = Ascent - Descent + 4;
            atlas?.Dispose();
            atlas = new TexpixAtlas(cellWidth, cellHeight, atlasWidth, cellHeight * 4, atlasMaxHeight);
            atlas.TextureRecreated += () => AtlasChanged?.Invoke();

            glyphs = new Dictionary<uint, TexpixGlyph>();
            kerning = null;
            initialized = true;
        }

        void LoadFace()
        {
            var error = FontEngine.LoadFontFace(sourceFont, (float)pixelSize, 0);
            if (error != FontEngineError.Success && sourceFont.fontNames is { Length: > 0 })
            {
                // Dynamic OS fonts carry no font data; retry via the system family name.
                error = FontEngine.LoadFontFace(sourceFont.fontNames[0], "Regular", pixelSize);
            }
            if (error != FontEngineError.Success)
                throw new InvalidOperationException($"TexpixFontAsset '{name}': LoadFontFace failed with {error}.");
            s_ActiveFace = this;
        }

        void EnsureFaceLoaded()
        {
            if (s_ActiveFace != this)
                LoadFace();
        }

        /// <summary>Resolves a glyph through this font and its fallback chain.</summary>
        public bool TryGetGlyph(uint unicode, out TexpixGlyph glyph)
        {
            BuildChain();
            for (int i = 0; i < resolvedChain.Count; i++)
            {
                if (resolvedChain[i].TryGetGlyphLocal(unicode, out glyph))
                {
                    glyph.sourceFontIndex = i;
                    return true;
                }
            }
            glyph = default;
            return false;
        }

        bool TryGetGlyphLocal(uint unicode, out TexpixGlyph glyph)
        {
            glyph = default;
            if (!IsReady)
                return false;

            EnsureInitialized();
            if (glyphs.TryGetValue(unicode, out glyph))
                return glyph.valid;

            if (atlasMode == TexpixAtlasMode.Static)
                return false;

            // No AtlasChanged here: glyph writes update the texture in place, so
            // existing meshes stay valid. Only texture recreation and atlas resets notify.
            glyph = AddGlyph(unicode);
            glyphs[unicode] = glyph;
            return glyph.valid;
        }

        /// <summary>Kerning x-advance adjustment for a glyph index pair, in font pixels.</summary>
        public int GetKerning(uint leftGlyphIndex, uint rightGlyphIndex)
        {
            EnsureInitialized();
            if (kerning == null)
                LoadKerning();
            return kerning.TryGetValue(((ulong)leftGlyphIndex << 32) | rightGlyphIndex, out int value) ? value : 0;
        }

        void LoadKerning()
        {
            EnsureFaceLoaded();
            kerning = new Dictionary<ulong, int>();
            GlyphPairAdjustmentRecord[] records = FontEngineBridge.GetAllPairAdjustmentRecords();
            if (records == null)
                return;
            foreach (var record in records)
            {
                int xAdvance = Mathf.RoundToInt(record.firstAdjustmentRecord.glyphValueRecord.xAdvance);
                if (xAdvance == 0)
                    continue;
                ulong key = ((ulong)record.firstAdjustmentRecord.glyphIndex << 32) | record.secondAdjustmentRecord.glyphIndex;
                kerning[key] = xAdvance;
            }
        }

        TexpixGlyph AddGlyph(uint unicode)
        {
            EnsureFaceLoaded();

            if (!FontEngine.TryGetGlyphIndex(unicode, out uint glyphIndex) || glyphIndex == 0)
                return default;

            if (!FontEngine.TryGetGlyphWithIndexValue(glyphIndex, GlyphLoadFlags.LOAD_DEFAULT, out Glyph info))
                return default;

            var glyph = new TexpixGlyph
            {
                unicode = unicode,
                glyphIndex = glyphIndex,
                advance = Mathf.Max(0, Mathf.RoundToInt(info.metrics.horizontalAdvance)),
                cellIndex = -1,
                valid = true,
            };

            int glyphWidth = Mathf.RoundToInt(info.metrics.width);
            int glyphHeight = Mathf.RoundToInt(info.metrics.height);
            if (glyphWidth <= 0 || glyphHeight <= 0)
                return glyph; // advance-only glyph (e.g. space)

            EnsureScratch();
            var scratchData = scratchTexture.GetPixelData<byte>(0);
            for (int i = 0; i < scratchData.Length; i++)
                scratchData[i] = 0;
            scratchFreeRects.Clear();
            scratchFreeRects.Add(new GlyphRect(0, 0, scratchTexture.width - 1, scratchTexture.height - 1));
            scratchUsedRects.Clear();

            if (!FontEngineBridge.TryAddGlyphToTexture(glyphIndex, 0, GlyphPackingMode.BestShortSideFit,
                    scratchFreeRects, scratchUsedRects, GlyphRenderMode.RASTER, scratchTexture, out Glyph rendered)
                || rendered == null)
            {
                Debug.LogWarning($"TexpixFontAsset '{name}': failed to rasterize glyph U+{unicode:X4}.");
                return glyph;
            }

            GlyphRect rect = rendered.glyphRect;
            if (rect.width <= 0 || rect.height <= 0)
                return glyph;

            int paddedWidth = rect.width + 2;
            int paddedHeight = rect.height + 2;
            if (paddedWidth > atlas.CellWidthPx || paddedHeight > atlas.CellHeightPx)
            {
                Debug.LogWarning($"TexpixFontAsset '{name}': glyph U+{unicode:X4} ({rect.width}x{rect.height}) exceeds atlas cell size; skipped.");
                return glyph;
            }

            var binary = new byte[rect.width * rect.height];
            for (int y = 0; y < rect.height; y++)
            {
                int srcRow = (rect.y + y) * scratchTexture.width + rect.x;
                for (int x = 0; x < rect.width; x++)
                    binary[y * rect.width + x] = scratchData[srcRow + x] > 127 ? (byte)1 : (byte)0;
            }

            var levels = new byte[paddedWidth * paddedHeight];
            GlyphClassifier.Classify(binary, rect.width, rect.height, levels);

            if (!atlas.TryAllocateCell(out int cellIndex, out Vector2Int origin))
            {
                // Atlas exhausted: reset it and let referencing texts re-add the glyphs
                // they still need on the rebuild triggered by AtlasChanged. Thrashing
                // here means the max atlas size is too small for the visible text.
                Debug.LogWarning($"TexpixFontAsset '{name}': atlas exhausted (max height {atlasMaxHeight}px); resetting. Increase Atlas Max Height if this repeats.");
                atlas.Clear();
                glyphs.Clear();
                AtlasChanged?.Invoke();
                if (!atlas.TryAllocateCell(out cellIndex, out origin))
                {
                    Debug.LogError($"TexpixFontAsset '{name}': atlas cannot fit even a single glyph.");
                    return glyph;
                }
            }

            atlas.WriteGlyph(origin, levels, paddedWidth, paddedHeight);
            atlas.ApplyIfDirty();

            glyph.atlasX = origin.x;
            glyph.atlasY = origin.y;
            glyph.width = paddedWidth;
            glyph.height = paddedHeight;
            glyph.bearingX = Mathf.RoundToInt(rendered.metrics.horizontalBearingX) - 1;
            glyph.bearingY = Mathf.RoundToInt(rendered.metrics.horizontalBearingY) + 1;
            glyph.cellIndex = cellIndex;
            return glyph;
        }

        void EnsureScratch()
        {
            if (scratchTexture != null)
                return;
            int size = Mathf.NextPowerOfTwo(Mathf.Max(atlas.CellWidthPx, atlas.CellHeightPx, 32));
            scratchTexture = new Texture2D(size, size, TextureFormat.Alpha8, false, true)
            {
                name = "Texpix Scratch",
                hideFlags = HideFlags.HideAndDontSave,
            };
            scratchFreeRects = new List<GlyphRect>();
            scratchUsedRects = new List<GlyphRect>();
        }

        void ReleaseRuntimeState()
        {
            atlas?.Dispose();
            atlas = null;
            if (scratchTexture != null)
            {
                if (Application.isPlaying)
                    Destroy(scratchTexture);
                else
                    DestroyImmediate(scratchTexture);
                scratchTexture = null;
            }
            glyphs = null;
            kerning = null;
            resolvedChain = null;
            initialized = false;
            if (s_ActiveFace == this)
                s_ActiveFace = null;
        }

        void OnDisable()
        {
            ReleaseRuntimeState();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Bakes the character set into serialized static data (glyph table, kerning,
        /// atlas texture stored as a sub-asset) and switches the asset to Static mode.
        /// </summary>
        public void Bake(string characters = null)
        {
            characters ??= bakeCharacterSet;
            if (sourceFont == null)
                throw new InvalidOperationException($"TexpixFontAsset '{name}': cannot bake without a source font.");

            ReleaseRuntimeState();
            TexpixAtlasMode previousMode = atlasMode;
            atlasMode = TexpixAtlasMode.Dynamic;
            try
            {
                EnsureInitialized();

                var codepoints = new HashSet<uint> { 0xFFFD, 0x2026 };
                for (int i = 0; i < characters.Length; i++)
                {
                    char c = characters[i];
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
                foreach (uint cp in codepoints)
                {
                    if (!TryGetGlyph(cp, out TexpixGlyph glyph))
                        continue;
                    glyphList.Add(new BakedGlyph
                    {
                        unicode = glyph.unicode,
                        glyphIndex = glyph.glyphIndex,
                        atlasX = glyph.atlasX,
                        atlasY = glyph.atlasY,
                        width = glyph.width,
                        height = glyph.height,
                        bearingX = glyph.bearingX,
                        bearingY = glyph.bearingY,
                        advance = glyph.advance,
                    });
                    bakedIndexes.Add(glyph.glyphIndex);
                }

                if (kerning == null)
                    LoadKerning();
                var kerningList = new List<BakedKerningPair>();
                foreach (KeyValuePair<ulong, int> pair in kerning)
                {
                    uint left = (uint)(pair.Key >> 32);
                    uint right = (uint)pair.Key;
                    if (bakedIndexes.Contains(left) && bakedIndexes.Contains(right))
                        kerningList.Add(new BakedKerningPair { left = left, right = right, xAdvance = pair.Value });
                }

                Texture2D source = atlas.Texture;
                var copy = new Texture2D(source.width, source.height, TextureFormat.R8, false, true)
                {
                    name = name + " Atlas",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                copy.SetPixelData(source.GetPixelData<byte>(0), 0);
                copy.Apply(false);

                if (UnityEditor.EditorUtility.IsPersistent(this))
                {
                    if (bakedAtlasTexture != null)
                    {
                        UnityEditor.AssetDatabase.RemoveObjectFromAsset(bakedAtlasTexture);
                        DestroyImmediate(bakedAtlasTexture, true);
                    }
                    UnityEditor.AssetDatabase.AddObjectToAsset(copy, this);
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

            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.EditorUtility.IsPersistent(this))
                UnityEditor.AssetDatabase.SaveAssets();
            AtlasChanged?.Invoke();
        }

        /// <summary>Removes baked data and switches back to Dynamic mode.</summary>
        public void ClearBaked()
        {
            ReleaseRuntimeState();
            if (bakedAtlasTexture != null)
            {
                if (UnityEditor.EditorUtility.IsPersistent(this))
                    UnityEditor.AssetDatabase.RemoveObjectFromAsset(bakedAtlasTexture);
                DestroyImmediate(bakedAtlasTexture, true);
                bakedAtlasTexture = null;
            }
            bakedGlyphs = Array.Empty<BakedGlyph>();
            bakedKerningPairs = Array.Empty<BakedKerningPair>();
            atlasMode = TexpixAtlasMode.Dynamic;
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.EditorUtility.IsPersistent(this))
                UnityEditor.AssetDatabase.SaveAssets();
            AtlasChanged?.Invoke();
        }

        internal Texture2D BakedAtlasTextureForInspector => bakedAtlasTexture;
        internal int BakedGlyphCount => bakedGlyphs.Length;
        internal int BakedKerningCount => bakedKerningPairs.Length;
#endif

#if UNITY_EDITOR
        void OnValidate()
        {
            if (initialized)
            {
                ReleaseRuntimeState();
                AtlasChanged?.Invoke();
            }
        }
#endif
    }
}
