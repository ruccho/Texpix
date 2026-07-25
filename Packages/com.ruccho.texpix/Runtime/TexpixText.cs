using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Texpix
{
    /// <summary>
    ///     uGUI text component rendering a Texpix pixel-font atlas. Vertices carry atlas
    ///     font-pixel coordinates and the packed outline color/mode in uv0 (see
    ///     <see cref="TexpixVertexFormat" />); the shader decodes the 2bpp atlas per pixel.
    ///     Because nothing per-component lives in material properties, every component
    ///     shares one material and uGUI can batch them.
    /// </summary>
    // Note: since uGUI 2.0 (Unity 6), Graphic no longer requires CanvasRenderer
    // itself, so the component must declare it explicitly.
    [AddComponentMenu("UI/Texpix Text")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TexpixText : MaskableGraphic, ILayoutElement
    {
        private const string SpriteChildName = "Texpix Sprites";
        private const string FallbackChildPrefix = "Texpix Fallback ";

        private static readonly List<TexpixQuad> SQuads = new();
        private static readonly List<TexpixQuad> SSpriteQuads = new();
        private static readonly List<Vector3> SSpriteVerts = new();
        private static readonly List<Color32> SSpriteColors = new();
        private static readonly List<Vector4> SSpriteUvs = new();
        private static readonly List<int> SSpriteIndices = new();

        /// <summary>
        ///     Shared by every component: the Texpix shader takes all per-text state from
        ///     the vertex stream, so one instance is enough and uGUI batches across
        ///     components that use the same atlas texture.
        /// </summary>
        private static Material s_SharedMaterial;

        [SerializeField] private TexpixFontAsset font;
        [SerializeField] [TextArea(3, 10)] private string text = "";
        [SerializeField] [Min(0.01f)] private float pixelScale = 1f;
        [SerializeField] private TexpixHorizontalAlignment horizontalAlignment = TexpixHorizontalAlignment.Left;
        [SerializeField] private TexpixVerticalAlignment verticalAlignment = TexpixVerticalAlignment.Top;
        [SerializeField] private TexpixWrapMode wrapMode = TexpixWrapMode.Wrap;
        [SerializeField] private TexpixOverflowMode overflow = TexpixOverflowMode.Overflow;
        [SerializeField] private int letterSpacing;

        [SerializeField] private int lineSpacing;

        // [SerializeField] private bool snapToPixelGrid = true;
        [SerializeField] private bool richText = true;
        [SerializeField] private TexpixSpriteAsset spriteAsset;
        [SerializeField] private TexpixOutlineMode outlineMode = TexpixOutlineMode.None;
        [SerializeField] private Color outlineColor = Color.black;
        private readonly List<TexpixSubGraphic> _fallbackSubs = new();
        private readonly List<TexpixFontAsset> _subscribedFonts = new();

        /// <summary>Set while layout runs (measuring or populating), where dirtying is not allowed.</summary>
        private bool _generating;

        private bool _pendingAtlasDirty;
        private TexpixSubGraphic _spriteSubGraphic;

        public TexpixFontAsset Font
        {
            get => font;
            set
            {
                if (font == value)
                    return;
                UnsubscribeFont();
                font = value;
                SubscribeFont();
                EnsureFallbackSubGraphics();
                SetAllDirty();
            }
        }

        public string Text
        {
            get => text;
            set
            {
                value ??= "";
                if (text == value)
                    return;
                text = value;
                SetTextDirty();
            }
        }

        /// <summary>Canvas units per font pixel.</summary>
        public float PixelScale
        {
            get => pixelScale;
            set
            {
                pixelScale = Mathf.Max(0.01f, value);
                SetTextDirty();
            }
        }

        public TexpixHorizontalAlignment HorizontalAlignment
        {
            get => horizontalAlignment;
            set
            {
                if (horizontalAlignment == value)
                    return;
                horizontalAlignment = value;
                SetVerticesDirty();
            }
        }

        public TexpixVerticalAlignment VerticalAlignment
        {
            get => verticalAlignment;
            set
            {
                if (verticalAlignment == value)
                    return;
                verticalAlignment = value;
                SetVerticesDirty();
            }
        }

        public TexpixWrapMode WrapMode
        {
            get => wrapMode;
            set
            {
                if (wrapMode == value)
                    return;
                wrapMode = value;
                SetTextDirty();
            }
        }

        public TexpixOverflowMode Overflow
        {
            get => overflow;
            set
            {
                if (overflow == value)
                    return;
                overflow = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Extra spacing between characters/sprites in font pixels; may be negative.</summary>
        public int LetterSpacing
        {
            get => letterSpacing;
            set
            {
                if (letterSpacing == value)
                    return;
                letterSpacing = value;
                SetTextDirty();
            }
        }

        /// <summary>Adjustment to the font's line height in font pixels; may be negative.</summary>
        public int LineSpacing
        {
            get => lineSpacing;
            set
            {
                if (lineSpacing == value)
                    return;
                lineSpacing = value;
                SetTextDirty();
            }
        }

        /*
        public bool SnapToPixelGrid
        {
            get => snapToPixelGrid;
            set
            {
                if (snapToPixelGrid == value)
                    return;
                snapToPixelGrid = value;
                SetVerticesDirty();
            }
        }
        */

        public bool RichText
        {
            get => richText;
            set
            {
                if (richText == value)
                    return;
                richText = value;
                SetTextDirty();
            }
        }

        public TexpixSpriteAsset SpriteAsset
        {
            get => spriteAsset;
            set
            {
                if (spriteAsset == value)
                    return;
                spriteAsset = value;
                EnsureSpriteSubGraphic();
                SetTextDirty();
            }
        }

        public TexpixOutlineMode OutlineMode
        {
            get => outlineMode;
            set
            {
                if (outlineMode == value)
                    return;
                outlineMode = value;
                // Outline state lives in the vertex stream, so it is a mesh change.
                SetVerticesDirty();
            }
        }

        public Color OutlineColor
        {
            get => outlineColor;
            set
            {
                if (outlineColor == value)
                    return;
                outlineColor = value;
                SetVerticesDirty();
            }
        }

        public override Texture mainTexture =>
            font != null && font.IsReady ? font.AtlasTexture : Texture2D.whiteTexture;

        public override Material defaultMaterial
        {
            get
            {
                if (s_SharedMaterial == null)
                {
                    var shader = Shader.Find("Texpix/UI Default");
                    if (shader == null)
                    {
                        Debug.LogError("Texpix: shader 'Texpix/UI Default' not found.");
                        return base.defaultMaterial;
                    }

                    s_SharedMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }

                return s_SharedMaterial;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SubscribeFont();
            EnsureSpriteSubGraphic();
            EnsureFallbackSubGraphics();
        }

        protected override void OnDisable()
        {
            if (_pendingAtlasDirty)
            {
                Canvas.willRenderCanvases -= DeferredAtlasDirty;
                _pendingAtlasDirty = false;
            }

            UnsubscribeFont();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            DestroySubGraphic(ref _spriteSubGraphic);
            foreach (var t in _fallbackSubs)
            {
                var sub = t;
                DestroySubGraphic(ref sub);
            }

            _fallbackSubs.Clear();
            // s_SharedMaterial outlives every component and is not destroyed here.
            base.OnDestroy();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            pixelScale = Mathf.Max(0.01f, pixelScale);
            SubscribeFont(); // re-resolves the fallback chain
            // Object creation is not allowed inside OnValidate; defer sub-graphic setup.
            EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled)
                {
                    EnsureSpriteSubGraphic();
                    EnsureFallbackSubGraphics();
                }
            };
            SetAllDirty();
        }
#endif

        /// <summary>
        ///     Widest line when nothing constrains the text, in canvas units. Read by
        ///     ContentSizeFitter and the layout groups after
        ///     <see cref="CalculateLayoutInputHorizontal" />.
        /// </summary>
        public float preferredWidth { get; private set; }

        /// <summary>
        ///     Height the text needs at the width the layout system has already resolved,
        ///     in canvas units.
        /// </summary>
        public float preferredHeight { get; private set; }

        public float minWidth => 0f;
        public float flexibleWidth => -1f;
        public float minHeight => 0f;
        public float flexibleHeight => -1f;
        public int layoutPriority => 0;

        public void CalculateLayoutInputHorizontal()
        {
            preferredWidth = Measure(0).WidthPx * pixelScale;
        }

        public void CalculateLayoutInputVertical()
        {
            // Runs after SetLayoutHorizontal, so the rect already has its final width
            // and wrapping resolves against the same budget the mesh will use.
            preferredHeight = Measure(Mathf.FloorToInt(rectTransform.rect.width / pixelScale)).HeightPx * pixelScale;
        }

        /// <summary>
        ///     One sub-graphic per fallback font, sharing this component's Texpix material
        ///     but bound to the fallback font's atlas. Must run outside canvas rebuilds.
        /// </summary>
        private void EnsureFallbackSubGraphics()
        {
            var needed = font != null ? font.ResolvedChain.Count - 1 : 0;
            for (var i = _fallbackSubs.Count; i < needed; i++)
                _fallbackSubs.Add(FindOrCreateSubGraphic(FallbackChildPrefix + (i + 1)));
            for (var i = 0; i < _fallbackSubs.Count; i++)
            {
                var sub = _fallbackSubs[i];
                if (i < needed)
                {
                    var chainFont = font.ResolvedChain[i + 1];
                    sub.material = material;
                    sub.SetTexture(chainFont.IsReady ? chainFont.AtlasTexture : null);
                }
                else
                {
                    sub.ClearMesh();
                }
            }
        }

        private TexpixSubGraphic FindOrCreateSubGraphic(string childName)
        {
            foreach (Transform child in transform)
                if (child.name == childName && child.TryGetComponent(out TexpixSubGraphic existing))
                    return SetupSubGraphic(existing);
            var go = new GameObject(childName, typeof(RectTransform))
            {
                hideFlags = HideFlags.DontSave,
                layer = gameObject.layer
            };
            go.transform.SetParent(transform, false);
            var sub = go.AddComponent<TexpixSubGraphic>();
            sub.raycastTarget = false;
            return SetupSubGraphic(sub);
        }

        private TexpixSubGraphic SetupSubGraphic(TexpixSubGraphic sub)
        {
            // Anchor to the parent's pivot so the sub's local space equals the parent's.
            var subRect = sub.rectTransform;
            subRect.anchorMin = subRect.anchorMax = rectTransform.pivot;
            subRect.anchoredPosition = Vector2.zero;
            subRect.sizeDelta = Vector2.zero;
            return sub;
        }

        private void EnsureSpriteSubGraphic()
        {
            if (spriteAsset == null)
            {
                _spriteSubGraphic?.ClearMesh();
                return;
            }

            _spriteSubGraphic ??= FindOrCreateSubGraphic(SpriteChildName);
            SetupSubGraphic(_spriteSubGraphic);
            _spriteSubGraphic.SetTexture(spriteAsset.Texture);
        }

        private void SubscribeFont()
        {
            UnsubscribeFont();
            if (font == null)
                return;
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

        private static void DestroySubGraphic(ref TexpixSubGraphic sub)
        {
            if (sub == null)
                return;
            if (Application.isPlaying)
                Destroy(sub.gameObject);
            else
                DestroyImmediate(sub.gameObject);
            sub = null;
        }

        /// <summary>
        ///     Text content or a setting that affects the text's size changed: both the
        ///     mesh and any layout driven by <see cref="ILayoutElement" /> are now stale.
        /// </summary>
        private void SetTextDirty()
        {
            SetVerticesDirty();
            SetLayoutDirty();
        }

        /// <summary>
        ///     Lays the text out at the given width budget (0 = unconstrained) and returns
        ///     its size in font pixels, without producing geometry. Height and overflow are
        ///     deliberately unconstrained: the layout system asks for the text's natural
        ///     size, and trimming it here would let a fitter shrink-wrap its own truncation.
        /// </summary>
        private TexpixTextMetrics Measure(int maxWidthPx)
        {
            if (font == null || !font.IsReady || string.IsNullOrEmpty(text))
                return default;

            var settings = new TexpixLayoutSettings
            {
                MaxWidthPx = maxWidthPx,
                MaxHeightPx = 0,
                HorizontalAlignment = horizontalAlignment,
                VerticalAlignment = verticalAlignment,
                WrapMode = wrapMode,
                Overflow = TexpixOverflowMode.Overflow,
                LetterSpacingPx = letterSpacing,
                LineSpacingPx = lineSpacing,
                RichText = richText,
                SpriteAsset = spriteAsset
            };

            // Measuring rasterizes glyphs, which can grow the atlas; the guard routes the
            // resulting AtlasChanged through the deferred path instead of dirtying mid-layout.
            _generating = true;
            try
            {
                return TexpixTextGenerator.Measure(font, text, in settings);
            }
            finally
            {
                _generating = false;
            }
        }

        private void OnAtlasChanged()
        {
            // Fired on atlas texture recreation (growth) and atlas resets — both
            // require a repaint, including our own (a reset invalidates quads already
            // emitted during the current populate). Dirtying inside the canvas
            // rebuild loop is unsupported, so defer to the next canvas update.
            if (_generating || CanvasUpdateRegistry.IsRebuildingGraphics() || CanvasUpdateRegistry.IsRebuildingLayout())
            {
                if (!_pendingAtlasDirty)
                {
                    _pendingAtlasDirty = true;
                    Canvas.willRenderCanvases += DeferredAtlasDirty;
                }
            }
            else
            {
                SetAllDirty();
            }
        }

        private void DeferredAtlasDirty()
        {
            Canvas.willRenderCanvases -= DeferredAtlasDirty;
            _pendingAtlasDirty = false;
            if (this != null && isActiveAndEnabled)
                SetAllDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (font == null || !font.IsReady || string.IsNullOrEmpty(text))
            {
                _spriteSubGraphic?.ClearMesh();
                return;
            }

            var rect = rectTransform.rect;
            var settings = new TexpixLayoutSettings
            {
                MaxWidthPx = Mathf.FloorToInt(rect.width / pixelScale),
                MaxHeightPx = Mathf.FloorToInt(rect.height / pixelScale),
                HorizontalAlignment = horizontalAlignment,
                VerticalAlignment = verticalAlignment,
                WrapMode = wrapMode,
                Overflow = overflow,
                LetterSpacingPx = letterSpacing,
                LineSpacingPx = lineSpacing,
                RichText = richText,
                SpriteAsset = spriteAsset
            };

            _generating = true;
            try
            {
                TexpixTextGenerator.Generate(font, text, in settings, SQuads,
                    spriteAsset != null ? SSpriteQuads : null);
            }
            finally
            {
                _generating = false;
            }

            // Layout origin is the rect's top-left corner; content extends toward -y.
            // Snapping keeps glyph corners on multiples of pixelScale in local space so
            // a pixel-perfect canvas samples texels 1:1.
            Vector2 origin = new(rect.xMin, rect.yMax);
            /*
            if (snapToPixelGrid)
                origin = new Vector2(
                    Mathf.Round(origin.x / pixelScale) * pixelScale,
                    Mathf.Round(origin.y / pixelScale) * pixelScale);
                    */

            var componentColor = color;
            var packedOutline = TexpixVertexFormat.PackOutline(outlineColor, outlineMode);
            foreach (var quad in SQuads)
            {
                if (quad.FontIndex != 0)
                    continue;

                var x0 = origin.x + quad.X * pixelScale;
                var y0 = origin.y + quad.Y * pixelScale;
                var x1 = origin.x + (quad.X + quad.Width) * pixelScale;
                var y1 = origin.y + (quad.Y + quad.Height) * pixelScale;
                Color32 vertexColor = componentColor * quad.Color;

                var vertexIndex = vh.currentVertCount;
                vh.AddVert(new Vector3(x0, y0), vertexColor,
                    new Vector4(quad.AtlasX, quad.AtlasY, packedOutline.x, packedOutline.y));
                vh.AddVert(new Vector3(x0, y1), vertexColor,
                    new Vector4(quad.AtlasX, quad.AtlasY + quad.Height, packedOutline.x, packedOutline.y));
                vh.AddVert(new Vector3(x1, y1), vertexColor,
                    new Vector4(quad.AtlasX + quad.Width, quad.AtlasY + quad.Height, packedOutline.x,
                        packedOutline.y));
                vh.AddVert(new Vector3(x1, y0), vertexColor,
                    new Vector4(quad.AtlasX + quad.Width, quad.AtlasY, packedOutline.x, packedOutline.y));
                vh.AddTriangle(vertexIndex, vertexIndex + 1, vertexIndex + 2);
                vh.AddTriangle(vertexIndex + 2, vertexIndex + 3, vertexIndex);
            }

            UploadFallbackQuads(origin, packedOutline);
            UploadSpriteQuads(origin);
        }

        private void UploadFallbackQuads(Vector2 origin, Vector2 packedOutline)
        {
            var componentColor = color;
            for (var fi = 0; fi < _fallbackSubs.Count; fi++)
            {
                var sub = _fallbackSubs[fi];
                var fontIndex = fi + 1;

                SSpriteVerts.Clear();
                SSpriteColors.Clear();
                SSpriteUvs.Clear();
                SSpriteIndices.Clear();

                foreach (var quad in SQuads)
                {
                    if (quad.FontIndex != fontIndex)
                        continue;

                    var x0 = origin.x + quad.X * pixelScale;
                    var y0 = origin.y + quad.Y * pixelScale;
                    var x1 = origin.x + (quad.X + quad.Width) * pixelScale;
                    var y1 = origin.y + (quad.Y + quad.Height) * pixelScale;
                    Color32 vertexColor = componentColor * quad.Color;

                    var vertexIndex = SSpriteVerts.Count;
                    SSpriteVerts.Add(new Vector3(x0, y0));
                    SSpriteVerts.Add(new Vector3(x0, y1));
                    SSpriteVerts.Add(new Vector3(x1, y1));
                    SSpriteVerts.Add(new Vector3(x1, y0));
                    SSpriteColors.Add(vertexColor);
                    SSpriteColors.Add(vertexColor);
                    SSpriteColors.Add(vertexColor);
                    SSpriteColors.Add(vertexColor);
                    // The Texpix shader expects atlas font-pixel coordinates in uv0.xy
                    // and the packed outline in uv0.zw.
                    SSpriteUvs.Add(new Vector4(quad.AtlasX, quad.AtlasY, packedOutline.x, packedOutline.y));
                    SSpriteUvs.Add(new Vector4(quad.AtlasX, quad.AtlasY + quad.Height, packedOutline.x,
                        packedOutline.y));
                    SSpriteUvs.Add(new Vector4(quad.AtlasX + quad.Width, quad.AtlasY + quad.Height, packedOutline.x,
                        packedOutline.y));
                    SSpriteUvs.Add(new Vector4(quad.AtlasX + quad.Width, quad.AtlasY, packedOutline.x,
                        packedOutline.y));
                    SSpriteIndices.Add(vertexIndex);
                    SSpriteIndices.Add(vertexIndex + 1);
                    SSpriteIndices.Add(vertexIndex + 2);
                    SSpriteIndices.Add(vertexIndex + 2);
                    SSpriteIndices.Add(vertexIndex + 3);
                    SSpriteIndices.Add(vertexIndex);
                }

                if (SSpriteVerts.Count == 0)
                    sub.ClearMesh();
                else
                    sub.UploadMesh(SSpriteVerts, SSpriteColors, SSpriteUvs, SSpriteIndices);
            }
        }

        private void UploadSpriteQuads(Vector2 origin)
        {
            if (_spriteSubGraphic == null)
                return;
            if (spriteAsset == null || spriteAsset.Texture == null || SSpriteQuads.Count == 0)
            {
                _spriteSubGraphic.ClearMesh();
                return;
            }

            SSpriteVerts.Clear();
            SSpriteColors.Clear();
            SSpriteUvs.Clear();
            SSpriteIndices.Clear();

            var invW = 1f / spriteAsset.Texture.width;
            var invH = 1f / spriteAsset.Texture.height;
            var componentAlpha = color.a;
            foreach (var quad in SSpriteQuads)
            {
                Color tinted = quad.Color;
                tinted.a *= componentAlpha;
                Color32 spriteColor = tinted;
                var x0 = origin.x + quad.X * pixelScale;
                var y0 = origin.y + quad.Y * pixelScale;
                var x1 = origin.x + (quad.X + quad.Width) * pixelScale;
                var y1 = origin.y + (quad.Y + quad.Height) * pixelScale;
                var u0 = quad.AtlasX * invW;
                var v0 = quad.AtlasY * invH;
                var u1 = (quad.AtlasX + quad.Width) * invW;
                var v1 = (quad.AtlasY + quad.Height) * invH;

                var vertexIndex = SSpriteVerts.Count;
                SSpriteVerts.Add(new Vector3(x0, y0));
                SSpriteVerts.Add(new Vector3(x0, y1));
                SSpriteVerts.Add(new Vector3(x1, y1));
                SSpriteVerts.Add(new Vector3(x1, y0));
                SSpriteColors.Add(spriteColor);
                SSpriteColors.Add(spriteColor);
                SSpriteColors.Add(spriteColor);
                SSpriteColors.Add(spriteColor);
                // Sprites render with the standard UI shader: normalized UVs, no outline.
                SSpriteUvs.Add(new Vector4(u0, v0));
                SSpriteUvs.Add(new Vector4(u0, v1));
                SSpriteUvs.Add(new Vector4(u1, v1));
                SSpriteUvs.Add(new Vector4(u1, v0));
                SSpriteIndices.Add(vertexIndex);
                SSpriteIndices.Add(vertexIndex + 1);
                SSpriteIndices.Add(vertexIndex + 2);
                SSpriteIndices.Add(vertexIndex + 2);
                SSpriteIndices.Add(vertexIndex + 3);
                SSpriteIndices.Add(vertexIndex);
            }

            _spriteSubGraphic.UploadMesh(SSpriteVerts, SSpriteColors, SSpriteUvs, SSpriteIndices);
        }
    }
}