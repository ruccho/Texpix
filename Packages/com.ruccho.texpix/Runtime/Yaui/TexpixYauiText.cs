using System;
using System.Collections.Generic;
using UnityEngine;
using Yaui;

namespace Texpix.Yaui
{
    /// <summary>
    ///     One glyph (or inline sprite) of a <see cref="TexpixYauiText" />, as given to a
    ///     <see cref="TexpixYauiGlyphModifier" />.
    /// </summary>
    public struct TexpixYauiGlyph
    {
        /// <summary>The box of the glyph in the element's box: canvas units, origin at the top-left, Y down.</summary>
        public Rect Rect;

        /// <summary>Mirrors the glyph horizontally within its box.</summary>
        public bool FlipX;

        /// <summary>Mirrors the glyph vertically within its box.</summary>
        public bool FlipY;

        /// <summary>Whether this is an inline sprite (<c>&lt;sprite=name&gt;</c>), which come after the glyphs.</summary>
        public bool IsSprite;
    }

    /// <summary>
    ///     Moves or mirrors the glyphs of a <see cref="TexpixYauiText" /> before they are drawn (text effects such as
    ///     shaking characters). The glyphs come in the order of the text, then the inline sprites.
    /// </summary>
    public delegate void TexpixYauiGlyphModifier(Span<TexpixYauiGlyph> glyphs);

    /// <summary>
    ///     A YAUI element that draws a Texpix pixel-font text: the counterpart of <see cref="TexpixText" /> for
    ///     YAUI panels. Each glyph is an image primitive drawn by the <c>Hidden/Texpix/Yaui</c> shader, which decodes
    ///     the atlas per pixel like the uGUI shader.
    /// </summary>
    /// <remarks>
    ///     The generator of Texpix uses shared buffers and may rasterize glyphs, so it runs on the main thread only.
    ///     <see cref="OnPrepareMeasure" /> measures the natural size and the size at the width of the last layout, and
    ///     <see cref="MeasureContent" /> answers from these. A wrapped text whose width changed asks for a new measure
    ///     when the layout is applied, and YAUI lays its panel out again before rendering the same frame. Inline
    ///     sprites (<c>&lt;sprite=name&gt;</c> with a <see cref="TexpixSpriteAsset" />) are image primitives of the
    ///     same shader, marked to sample the sprite texture as is.
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("YAUI/Texpix Text")]
    public sealed class TexpixYauiText : YauiElement, ITexpixText
    {
        private static readonly List<TexpixQuad> SQuads = new();
        private static readonly List<TexpixQuad> SSpriteQuads = new();

        // The border width of sprite primitives: above every outline mode and atlas format (see TexpixYaui.shader).
        private const int SpriteEncoding = 32;

        /// <summary>The key of Texpix's YAUI shader module (TexpixYauiModule.asset).</summary>
        public const string ShaderModuleKey = "com.ruccho.texpix.glyph";
        private static Material s_Material;

        [SerializeField] private TexpixFontAsset font;
        [SerializeField] [TextArea(3, 10)] private string text = "";
        [SerializeField] private Color color = Color.white;
        [SerializeField] [Min(0.01f)] private float pixelScale = 1f;
        [SerializeField] private TexpixHorizontalAlignment horizontalAlignment = TexpixHorizontalAlignment.Left;
        [SerializeField] private TexpixVerticalAlignment verticalAlignment = TexpixVerticalAlignment.Top;
        [SerializeField] private TexpixWrapMode wrapMode = TexpixWrapMode.Wrap;
        [SerializeField] private TexpixOverflowMode overflow = TexpixOverflowMode.Overflow;
        [SerializeField] private int letterSpacing;
        [SerializeField] private int lineSpacing;
        [SerializeField] private bool snapToPixelGrid = true;
        [SerializeField] private bool richText = true;
        [SerializeField] private TexpixOutlineMode outlineMode = TexpixOutlineMode.None;
        [SerializeField] private Color outlineColor = Color.black;
        [SerializeField] private TexpixSpriteAsset spriteAsset;

        private readonly List<TexpixFontAsset> _subscribedFonts = new();
        private readonly List<YauiTexture> _textures = new();
        private YauiTexture _spriteTexture;
        private YauiPrimitive[] _buffer = Array.Empty<YauiPrimitive>();

        // The glyphs of the last generation, kept to apply the modifier again without generating the text.
        private GlyphSource[] _sources = Array.Empty<GlyphSource>();
        private TexpixYauiGlyph[] _glyphs = Array.Empty<TexpixYauiGlyph>();
        private int _sourceCount;
        private TexpixYauiGlyphModifier _glyphModifier;
        private bool _generating;
        private bool _atlasDirty;

        // Captured on the main thread for the measure on the layout thread, in font pixels.
        private Vector2Int _naturalSize;
        private int _measuredWidthPx = -1;
        private Vector2Int _measuredSize;

        public TexpixFontAsset Font
        {
            get => font;
            set
            {
                if (font == value) return;
                UnsubscribeFont();
                font = value;
                SubscribeFont();
                MarkTextDirty();
            }
        }

        public string Text
        {
            get => text;
            set
            {
                value ??= "";
                if (text == value) return;
                text = value;
                MarkTextDirty();
            }
        }

        /// <summary>The fill color of the glyphs. Rich text colors multiply it.</summary>
        public Color Color
        {
            get => color;
            set
            {
                if (color == value) return;
                color = value;
                Rebuild();
            }
        }

        public TexpixHorizontalAlignment HorizontalAlignment
        {
            get => horizontalAlignment;
            set
            {
                horizontalAlignment = value;
                Rebuild();
            }
        }

        public TexpixVerticalAlignment VerticalAlignment
        {
            get => verticalAlignment;
            set
            {
                verticalAlignment = value;
                Rebuild();
            }
        }

        public TexpixWrapMode WrapMode
        {
            get => wrapMode;
            set
            {
                wrapMode = value;
                MarkTextDirty();
            }
        }

        public TexpixOutlineMode OutlineMode
        {
            get => outlineMode;
            set
            {
                outlineMode = value;
                Rebuild();
            }
        }

        /// <summary>
        ///     Moves or mirrors the glyphs before they are drawn, or null. It runs when the text is generated and on
        ///     <see cref="RefreshGlyphs" />.
        /// </summary>
        public TexpixYauiGlyphModifier GlyphModifier
        {
            get => _glyphModifier;
            set
            {
                if (_glyphModifier == value) return;
                _glyphModifier = value;
                RefreshGlyphs();
            }
        }

        /// <summary>
        ///     Applies the <see cref="GlyphModifier" /> again to the glyphs of the last generation (an animated effect
        ///     calls it every frame). The text is not generated again.
        /// </summary>
        public void RefreshGlyphs()
        {
            if (!IsRegistered) return;

            ApplyGlyphs();
        }

        /// <summary>The sprites of <c>&lt;sprite=name&gt;</c> tags, or null to show them as missing.</summary>
        public TexpixSpriteAsset SpriteAsset
        {
            get => spriteAsset;
            set
            {
                if (spriteAsset == value) return;
                spriteAsset = value;
                MarkTextDirty();
            }
        }

        public Color OutlineColor
        {
            get => outlineColor;
            set
            {
                outlineColor = value;
                Rebuild();
            }
        }

        protected override bool AcceptsChildren => false;
        protected override bool HasVisibleContent => !string.IsNullOrEmpty(text);
        protected override bool MeasuresContent => true;

        /// <summary>
        /// The id of Texpix's shader module in the project's uber shader (Yaui.YauiShaderModules), or 0. With the
        /// module, glyphs draw with the uber shader, in the draws of the boxes and images around them; without it,
        /// with the material of Hidden/Texpix/Yaui.
        /// </summary>
        private static int ModuleId => YauiShaderModules.IdOf(ShaderModuleKey);

        private int _moduleId;

        /// <summary>Uses the module of the uber shader if the project has it, else Texpix's material.</summary>
        private void ApplyMaterial()
        {
            _moduleId = ModuleId;
            if (_moduleId != 0)
            {
                if (Material == s_Material) Material = null;
            }
            else if (Material == null)
            {
                Material = SharedMaterial;
            }
        }

        private static Material SharedMaterial
        {
            get
            {
                if (s_Material != null) return s_Material;

                var shader = Shader.Find("Hidden/Texpix/Yaui");
                if (shader == null)
                {
                    Debug.LogError("Texpix: shader 'Hidden/Texpix/Yaui' not found.");
                    return null;
                }

                s_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000 };
                return s_Material;
            }
        }

        protected override void OnEnable()
        {
            ApplyMaterial();
            SubscribeFont();
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            UnsubscribeFont();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            pixelScale = Mathf.Max(0.01f, pixelScale);
            if (!isActiveAndEnabled) return;

            SubscribeFont();
            MarkTextDirty();
        }

        protected override void OnDidApplyAnimationProperties()
        {
            base.OnDidApplyAnimationProperties();
            Rebuild();
        }

        protected override void OnRegistered()
        {
            ApplyMaterial();
            Rebuild();
        }

        protected override void OnLayoutApplied()
        {
            // A wrapped text answered the layout with a size measured at another width: measure it again.
            if (wrapMode == TexpixWrapMode.Wrap && font != null && _naturalSize.x * pixelScale > ContentBox.width)
            {
                var widthPx = Mathf.FloorToInt(ContentBox.width / pixelScale);
                if (widthPx != _measuredWidthPx)
                {
                    _measuredWidthPx = widthPx;
                    MarkMeasureDirty();
                }
            }

            Rebuild();
        }

        protected override void OnUnregistering()
        {
            ReleaseTextures();
        }

        private void LateUpdate()
        {
            if (!_atlasDirty) return;

            _atlasDirty = false;
            ReleaseTextures();
            Rebuild();
        }

        protected override void OnPrepareMeasure()
        {
            _naturalSize = Measure(0);
            _measuredSize = _measuredWidthPx > 0 ? Measure(_measuredWidthPx) : _naturalSize;
        }

        protected override Vector2 MeasureContent(float width, YauiMeasureMode widthMode, float height,
            YauiMeasureMode heightMode)
        {
            var size = (Vector2)_naturalSize * pixelScale;
            if (widthMode != YauiMeasureMode.Undefined && size.x > width)
            {
                if (wrapMode == TexpixWrapMode.Wrap && _measuredWidthPx == Mathf.FloorToInt(width / pixelScale))
                    size = (Vector2)_measuredSize * pixelScale;
                else
                    size.x = width;
            }

            if (widthMode == YauiMeasureMode.Exactly) size.x = width;
            if (heightMode == YauiMeasureMode.Exactly) size.y = height;
            return size;
        }

        private Vector2Int Measure(int maxWidthPx)
        {
            if (font == null || !font.IsReady || string.IsNullOrEmpty(text)) return Vector2Int.zero;

            var settings = Settings(maxWidthPx, 0, TexpixOverflowMode.Overflow);
            _generating = true;
            try
            {
                var metrics = TexpixTextGenerator.Measure(font, text, in settings);
                return new Vector2Int(metrics.WidthPx, metrics.HeightPx);
            }
            finally
            {
                _generating = false;
            }
        }

        private TexpixLayoutSettings Settings(int maxWidthPx, int maxHeightPx, TexpixOverflowMode overflowMode)
        {
            return new TexpixLayoutSettings
            {
                MaxWidthPx = maxWidthPx,
                MaxHeightPx = maxHeightPx,
                HorizontalAlignment = horizontalAlignment,
                VerticalAlignment = verticalAlignment,
                WrapMode = wrapMode,
                Overflow = overflowMode,
                LetterSpacingPx = letterSpacing,
                LineSpacingPx = lineSpacing,
                RichText = richText,
                SpriteAsset = spriteAsset
            };
        }

        private void MarkTextDirty()
        {
            MarkMeasureDirty();
            Rebuild();
        }

        private void Rebuild()
        {
            if (!IsRegistered) return;

            SyncHittable();
            var box = ContentBox;
            if (font == null || !font.IsReady || string.IsNullOrEmpty(text) || box.width <= 0f || box.height <= 0f)
            {
                _sourceCount = 0;
                ClearContent();
                return;
            }

            var settings = Settings(Mathf.FloorToInt(box.width / pixelScale), Mathf.FloorToInt(box.height / pixelScale),
                overflow);
            _generating = true;
            try
            {
                TexpixTextGenerator.Generate(font, text, in settings, SQuads, spriteAsset != null ? SSpriteQuads : null);
            }
            finally
            {
                _generating = false;
            }

            var chain = font.ResolvedChain;
            EnsureTextures(chain);

            // Canvas space is Y down; the generator is Y up from the top-left of the rect.
            var originX = box.x;
            var originY = box.y;
            if (snapToPixelGrid)
            {
                originX = Mathf.Round(originX / pixelScale) * pixelScale;
                originY = Mathf.Round(originY / pixelScale) * pixelScale;
            }

            var sprites = spriteAsset != null ? SSpriteQuads.Count : 0;
            if (_sources.Length < SQuads.Count + sprites)
                _sources = new GlyphSource[Mathf.NextPowerOfTwo(SQuads.Count + sprites)];

            var count = 0;
            foreach (var quad in SQuads)
            {
                if (quad.FontIndex >= chain.Count) continue;

                var chainFont = chain[quad.FontIndex];
                var texture = _textures[quad.FontIndex];
                if (!texture.IsValid) continue;

                var atlasTexture = chainFont.AtlasTexture;
                var widthPx = atlasTexture.width * (chainFont.AtlasFormat == TexpixAtlasFormat.FillOnly ? 8f : 4f);
                float heightPx = atlasTexture.height;
                var rect = new Rect(originX + quad.X * pixelScale, originY - (quad.Y + quad.Height) * pixelScale,
                    quad.Width * pixelScale, quad.Height * pixelScale);
                var uv = new Rect(quad.AtlasX / widthPx, quad.AtlasY / heightPx, quad.Width / widthPx,
                    quad.Height / heightPx);
                var fill = color * (Color)quad.Color;
                // The border width carries the outline mode and the atlas format (see TexpixYaui.shader).
                var encoded = (int)outlineMode + (int)chainFont.AtlasFormat * 4;
                _sources[count++] = new GlyphSource(rect, texture, uv, fill, encoded, outlineColor, false);
            }

            if (sprites > 0 && EnsureSpriteTexture())
            {
                var spriteTexture = spriteAsset.Texture;
                var invW = 1f / spriteTexture.width;
                var invH = 1f / spriteTexture.height;
                foreach (var quad in SSpriteQuads)
                {
                    var rect = new Rect(originX + quad.X * pixelScale, originY - (quad.Y + quad.Height) * pixelScale,
                        quad.Width * pixelScale, quad.Height * pixelScale);
                    // The sprite rect in the texture, Y up like the uGUI text.
                    var uv = new Rect(quad.AtlasX * invW, quad.AtlasY * invH, quad.Width * invW, quad.Height * invH);
                    // Like TexpixText: the rich text color, with the alpha of the component color only.
                    Color tint = quad.Color;
                    tint.a *= color.a;
                    _sources[count++] = new GlyphSource(rect, _spriteTexture, uv, tint, SpriteEncoding, Color.clear, true);
                }
            }

            _sourceCount = count;
            ApplyGlyphs();
        }

        /// <summary>Makes the primitives from the glyphs of the last generation, through the modifier if any.</summary>
        private void ApplyGlyphs()
        {
            var count = _sourceCount;
            if (_buffer.Length < count) _buffer = new YauiPrimitive[Mathf.NextPowerOfTwo(count)];

            if (_glyphModifier == null)
            {
                for (var i = 0; i < count; i++)
                    _buffer[i] = _sources[i].ToPrimitive(_sources[i].Rect, false, false).WithModule(_moduleId);
                SetContent(_buffer.AsSpan(0, count));
                return;
            }

            if (_glyphs.Length < count) _glyphs = new TexpixYauiGlyph[Mathf.NextPowerOfTwo(count)];
            for (var i = 0; i < count; i++)
                _glyphs[i] = new TexpixYauiGlyph { Rect = _sources[i].Rect, IsSprite = _sources[i].IsSprite };

            _glyphModifier(_glyphs.AsSpan(0, count));
            for (var i = 0; i < count; i++)
            {
                var glyph = _glyphs[i];
                _buffer[i] = _sources[i].ToPrimitive(glyph.Rect, glyph.FlipX, glyph.FlipY).WithModule(_moduleId);
            }

            SetContent(_buffer.AsSpan(0, count));
        }

        private readonly struct GlyphSource
        {
            public readonly Rect Rect;
            public readonly bool IsSprite;
            private readonly YauiTexture _texture;
            private readonly Rect _uv;
            private readonly Color _color;
            private readonly int _encoded;
            private readonly Color _outlineColor;

            public GlyphSource(Rect rect, YauiTexture texture, Rect uv, Color color, int encoded, Color outlineColor,
                bool isSprite)
            {
                Rect = rect;
                _texture = texture;
                _uv = uv;
                _color = color;
                _encoded = encoded;
                _outlineColor = outlineColor;
                IsSprite = isSprite;
            }

            public YauiPrimitive ToPrimitive(Rect rect, bool flipX, bool flipY)
            {
                // A UV rect of negative size mirrors the glyph: the shader decodes the atlas at the interpolated UV.
                var uv = _uv;
                if (flipX) uv = new Rect(uv.xMax, uv.y, -uv.width, uv.height);
                if (flipY) uv = new Rect(uv.x, uv.yMax, uv.width, -uv.height);
                return YauiPrimitive.Image(rect, _texture, uv, _color).WithBorder(_encoded, _outlineColor);
            }
        }

        private void EnsureTextures(IReadOnlyList<TexpixFontAsset> chain)
        {
            for (var i = 0; i < chain.Count; i++)
            {
                var atlas = chain[i] != null && chain[i].IsReady ? chain[i].AtlasTexture : null;
                if (i < _textures.Count)
                {
                    if (_textures[i].IsValid && _textures[i].Texture == atlas) continue;

                    _textures[i].Release();
                    _textures[i] = atlas != null ? YauiTexture.Acquire(atlas) : default;
                }
                else
                {
                    _textures.Add(atlas != null ? YauiTexture.Acquire(atlas) : default);
                }
            }
        }

        private bool EnsureSpriteTexture()
        {
            var texture = spriteAsset != null ? spriteAsset.Texture : null;
            if (_spriteTexture.IsValid && _spriteTexture.Texture == texture) return true;

            if (_spriteTexture.IsValid) _spriteTexture.Release();
            _spriteTexture = texture != null ? YauiTexture.Acquire(texture) : default;
            return _spriteTexture.IsValid;
        }

        private void ReleaseTextures()
        {
            foreach (var texture in _textures) texture.Release();
            _textures.Clear();
            if (_spriteTexture.IsValid) _spriteTexture.Release();
            _spriteTexture = default;
        }

        private void SubscribeFont()
        {
            UnsubscribeFont();
            if (font == null) return;

            foreach (var chainFont in font.ResolvedChain)
            {
                chainFont.AtlasChanged += OnAtlasChanged;
                _subscribedFonts.Add(chainFont);
            }
        }

        private void UnsubscribeFont()
        {
            foreach (var subscribed in _subscribedFonts)
                if (subscribed != null)
                    subscribed.AtlasChanged -= OnAtlasChanged;

            _subscribedFonts.Clear();
        }

        private void OnAtlasChanged()
        {
            if (this == null) return;

            // The atlas texture may have been recreated (it grew): acquire it again before the next rebuild.
            if (_generating || !IsRegistered)
            {
                _atlasDirty = true;
                return;
            }

            ReleaseTextures();
            Rebuild();
        }
    }
}
