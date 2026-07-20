using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Texpix
{
    public enum TexpixOutlineMode
    {
        None = 0,
        FourNeighbor = 1,
        EightNeighbor = 2
    }

    /// <summary>
    ///     uGUI text component rendering a Texpix pixel-font atlas. Vertices carry atlas
    ///     font-pixel coordinates in uv0; the shader decodes the 2bpp atlas per pixel.
    ///     Text is laid out on the baseline starting at the rect pivot (M2 scope).
    /// </summary>
    // Note: since uGUI 2.0 (Unity 6), Graphic no longer requires CanvasRenderer
    // itself, so the component must declare it explicitly.
    [AddComponentMenu("UI/Texpix Text")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TexpixText : MaskableGraphic
    {
        private const string SpriteChildName = "Texpix Sprites";
        private const string FallbackChildPrefix = "Texpix Fallback ";

        private static readonly List<TexpixQuad> SQuads = new();
        private static readonly List<TexpixQuad> SSpriteQuads = new();
        private static readonly List<Vector3> SSpriteVerts = new();
        private static readonly List<Color32> SSpriteColors = new();
        private static readonly List<Vector2> SSpriteUvs = new();
        private static readonly List<int> SSpriteIndices = new();
        private static readonly int SOutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int SOutlineModeId = Shader.PropertyToID("_OutlineMode");
        [SerializeField] private TexpixFontAsset font;
        [SerializeField] [TextArea(1, 10)] private string text = "";
        [SerializeField] [Min(0.01f)] private float pixelScale = 1f;
        [SerializeField] private TexpixHorizontalAlignment horizontalAlignment = TexpixHorizontalAlignment.Left;
        [SerializeField] private TexpixVerticalAlignment verticalAlignment = TexpixVerticalAlignment.Top;
        [SerializeField] private TexpixWrapMode wrapMode = TexpixWrapMode.Wrap;
        [SerializeField] private TexpixOverflowMode overflow = TexpixOverflowMode.Overflow;
        [SerializeField] private bool snapToPixelGrid = true;
        [SerializeField] private bool richText = true;
        [SerializeField] private TexpixSpriteAsset spriteAsset;
        [SerializeField] private TexpixOutlineMode outlineMode = TexpixOutlineMode.None;
        [SerializeField] private Color outlineColor = Color.black;
        private readonly List<TexpixSubGraphic> _fallbackSubs = new();
        private readonly List<TexpixFontAsset> _subscribedFonts = new();
        private bool _pendingAtlasDirty;
        private bool _populating;

        private Material _runtimeMaterial;
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
                SetVerticesDirty();
            }
        }

        /// <summary>Canvas units per font pixel.</summary>
        public float PixelScale
        {
            get => pixelScale;
            set
            {
                pixelScale = Mathf.Max(0.01f, value);
                SetVerticesDirty();
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
                SetVerticesDirty();
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

        public bool RichText
        {
            get => richText;
            set
            {
                if (richText == value)
                    return;
                richText = value;
                SetVerticesDirty();
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
                SetVerticesDirty();
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
                ApplyMaterialProperties();
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
                ApplyMaterialProperties();
            }
        }

        public override Texture mainTexture =>
            font != null && font.IsReady ? font.AtlasTexture : Texture2D.whiteTexture;

        public override Material defaultMaterial
        {
            get
            {
                if (_runtimeMaterial == null)
                {
                    var shader = Shader.Find("Texpix/UI Default");
                    if (shader == null)
                    {
                        Debug.LogError("Texpix: shader 'Texpix/UI Default' not found.");
                        return base.defaultMaterial;
                    }

                    _runtimeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    // No SetMaterialDirty here: this getter is typically first hit
                    // inside a canvas rebuild, where dirtying is not allowed.
                    WriteMaterialProperties();
                }

                return _runtimeMaterial;
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
            if (_runtimeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(_runtimeMaterial);
                else
                    DestroyImmediate(_runtimeMaterial);
                _runtimeMaterial = null;
            }

            base.OnDestroy();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            pixelScale = Mathf.Max(0.01f, pixelScale);
            SubscribeFont(); // re-resolves the fallback chain
            ApplyMaterialProperties();
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

        private void WriteMaterialProperties()
        {
            _runtimeMaterial.SetColor(SOutlineColorId, outlineColor);
            _runtimeMaterial.SetFloat(SOutlineModeId, (float)outlineMode);
        }

        private void ApplyMaterialProperties()
        {
            if (_runtimeMaterial == null)
                return;
            WriteMaterialProperties();
            SetMaterialDirty();
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

        private void OnAtlasChanged()
        {
            // Fired on atlas texture recreation (growth) and atlas resets — both
            // require a repaint, including our own (a reset invalidates quads already
            // emitted during the current populate). Dirtying inside the canvas
            // rebuild loop is unsupported, so defer to the next canvas update.
            if (_populating || CanvasUpdateRegistry.IsRebuildingGraphics() || CanvasUpdateRegistry.IsRebuildingLayout())
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
                RichText = richText,
                SpriteAsset = spriteAsset
            };

            _populating = true;
            try
            {
                TexpixTextGenerator.Generate(font, text, in settings, SQuads,
                    spriteAsset != null ? SSpriteQuads : null);
            }
            finally
            {
                _populating = false;
            }

            // Layout origin is the rect's top-left corner; content extends toward -y.
            // Snapping keeps glyph corners on multiples of pixelScale in local space so
            // a pixel-perfect canvas samples texels 1:1.
            Vector2 origin = new(rect.xMin, rect.yMax);
            if (snapToPixelGrid)
                origin = new Vector2(
                    Mathf.Round(origin.x / pixelScale) * pixelScale,
                    Mathf.Round(origin.y / pixelScale) * pixelScale);

            var componentColor = color;
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
                vh.AddVert(new Vector3(x0, y0), vertexColor, new Vector4(quad.AtlasX, quad.AtlasY));
                vh.AddVert(new Vector3(x0, y1), vertexColor, new Vector4(quad.AtlasX, quad.AtlasY + quad.Height));
                vh.AddVert(new Vector3(x1, y1), vertexColor,
                    new Vector4(quad.AtlasX + quad.Width, quad.AtlasY + quad.Height));
                vh.AddVert(new Vector3(x1, y0), vertexColor, new Vector4(quad.AtlasX + quad.Width, quad.AtlasY));
                vh.AddTriangle(vertexIndex, vertexIndex + 1, vertexIndex + 2);
                vh.AddTriangle(vertexIndex + 2, vertexIndex + 3, vertexIndex);
            }

            UploadFallbackQuads(origin);
            UploadSpriteQuads(origin);
        }

        private void UploadFallbackQuads(Vector2 origin)
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
                    // The Texpix shader expects atlas font-pixel coordinates in uv0.
                    SSpriteUvs.Add(new Vector2(quad.AtlasX, quad.AtlasY));
                    SSpriteUvs.Add(new Vector2(quad.AtlasX, quad.AtlasY + quad.Height));
                    SSpriteUvs.Add(new Vector2(quad.AtlasX + quad.Width, quad.AtlasY + quad.Height));
                    SSpriteUvs.Add(new Vector2(quad.AtlasX + quad.Width, quad.AtlasY));
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
                SSpriteUvs.Add(new Vector2(u0, v0));
                SSpriteUvs.Add(new Vector2(u0, v1));
                SSpriteUvs.Add(new Vector2(u1, v1));
                SSpriteUvs.Add(new Vector2(u1, v0));
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