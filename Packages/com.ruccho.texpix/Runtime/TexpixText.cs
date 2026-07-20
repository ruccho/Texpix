using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Texpix
{
    public enum TexpixOutlineMode
    {
        None = 0,
        FourNeighbor = 1,
        EightNeighbor = 2,
    }

    /// <summary>
    /// uGUI text component rendering a Texpix pixel-font atlas. Vertices carry atlas
    /// font-pixel coordinates in uv0; the shader decodes the 2bpp atlas per pixel.
    /// Text is laid out on the baseline starting at the rect pivot (M2 scope).
    /// </summary>
    // Note: since uGUI 2.0 (Unity 6), Graphic no longer requires CanvasRenderer
    // itself, so the component must declare it explicitly.
    [AddComponentMenu("UI/Texpix Text")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TexpixText : MaskableGraphic
    {
        [SerializeField] TexpixFontAsset font;
        [SerializeField, TextArea(1, 10)] string text = "";
        [SerializeField, Min(0.01f)] float pixelScale = 1f;
        [SerializeField] TexpixHorizontalAlignment horizontalAlignment = TexpixHorizontalAlignment.Left;
        [SerializeField] TexpixVerticalAlignment verticalAlignment = TexpixVerticalAlignment.Top;
        [SerializeField] TexpixWrapMode wrapMode = TexpixWrapMode.Wrap;
        [SerializeField] TexpixOverflowMode overflow = TexpixOverflowMode.Overflow;
        [SerializeField] bool snapToPixelGrid = true;
        [SerializeField] bool richText = true;
        [SerializeField] TexpixSpriteAsset spriteAsset;
        [SerializeField] TexpixOutlineMode outlineMode = TexpixOutlineMode.None;
        [SerializeField] Color outlineColor = Color.black;

        static readonly List<TexpixQuad> s_Quads = new();
        static readonly List<TexpixQuad> s_SpriteQuads = new();
        static readonly List<Vector3> s_SpriteVerts = new();
        static readonly List<Color32> s_SpriteColors = new();
        static readonly List<Vector2> s_SpriteUvs = new();
        static readonly List<int> s_SpriteIndices = new();
        static readonly int s_OutlineColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int s_OutlineModeId = Shader.PropertyToID("_OutlineMode");
        const string SpriteChildName = "Texpix Sprites";
        const string FallbackChildPrefix = "Texpix Fallback ";

        Material runtimeMaterial;
        readonly List<TexpixFontAsset> subscribedFonts = new();
        TexpixSubGraphic spriteSubGraphic;
        readonly List<TexpixSubGraphic> fallbackSubs = new();
        bool populating;
        bool pendingAtlasDirty;

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

        public override Texture mainTexture => font != null && font.IsReady ? font.AtlasTexture : Texture2D.whiteTexture;

        public override Material defaultMaterial
        {
            get
            {
                if (runtimeMaterial == null)
                {
                    var shader = Shader.Find("Texpix/UI Default");
                    if (shader == null)
                    {
                        Debug.LogError("Texpix: shader 'Texpix/UI Default' not found.");
                        return base.defaultMaterial;
                    }
                    runtimeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    // No SetMaterialDirty here: this getter is typically first hit
                    // inside a canvas rebuild, where dirtying is not allowed.
                    WriteMaterialProperties();
                }
                return runtimeMaterial;
            }
        }

        void WriteMaterialProperties()
        {
            runtimeMaterial.SetColor(s_OutlineColorId, outlineColor);
            runtimeMaterial.SetFloat(s_OutlineModeId, (float)outlineMode);
        }

        void ApplyMaterialProperties()
        {
            if (runtimeMaterial == null)
                return;
            WriteMaterialProperties();
            SetMaterialDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SubscribeFont();
            EnsureSpriteSubGraphic();
            EnsureFallbackSubGraphics();
        }

        /// <summary>
        /// One sub-graphic per fallback font, sharing this component's Texpix material
        /// but bound to the fallback font's atlas. Must run outside canvas rebuilds.
        /// </summary>
        void EnsureFallbackSubGraphics()
        {
            int needed = font != null ? font.ResolvedChain.Count - 1 : 0;
            for (int i = fallbackSubs.Count; i < needed; i++)
                fallbackSubs.Add(FindOrCreateSubGraphic(FallbackChildPrefix + (i + 1)));
            for (int i = 0; i < fallbackSubs.Count; i++)
            {
                TexpixSubGraphic sub = fallbackSubs[i];
                if (i < needed)
                {
                    TexpixFontAsset chainFont = font.ResolvedChain[i + 1];
                    sub.material = material;
                    sub.SetTexture(chainFont.IsReady ? chainFont.AtlasTexture : null);
                }
                else
                {
                    sub.ClearMesh();
                }
            }
        }

        TexpixSubGraphic FindOrCreateSubGraphic(string childName)
        {
            foreach (Transform child in transform)
            {
                if (child.name == childName && child.TryGetComponent(out TexpixSubGraphic existing))
                    return SetupSubGraphic(existing);
            }
            var go = new GameObject(childName, typeof(RectTransform))
            {
                hideFlags = HideFlags.DontSave,
                layer = gameObject.layer,
            };
            go.transform.SetParent(transform, false);
            var sub = go.AddComponent<TexpixSubGraphic>();
            sub.raycastTarget = false;
            return SetupSubGraphic(sub);
        }

        TexpixSubGraphic SetupSubGraphic(TexpixSubGraphic sub)
        {
            // Anchor to the parent's pivot so the sub's local space equals the parent's.
            RectTransform subRect = sub.rectTransform;
            subRect.anchorMin = subRect.anchorMax = rectTransform.pivot;
            subRect.anchoredPosition = Vector2.zero;
            subRect.sizeDelta = Vector2.zero;
            return sub;
        }

        void EnsureSpriteSubGraphic()
        {
            if (spriteAsset == null)
            {
                spriteSubGraphic?.ClearMesh();
                return;
            }
            spriteSubGraphic ??= FindOrCreateSubGraphic(SpriteChildName);
            SetupSubGraphic(spriteSubGraphic);
            spriteSubGraphic.SetTexture(spriteAsset.Texture);
        }

        protected override void OnDisable()
        {
            if (pendingAtlasDirty)
            {
                Canvas.willRenderCanvases -= DeferredAtlasDirty;
                pendingAtlasDirty = false;
            }
            UnsubscribeFont();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            DestroySubGraphic(ref spriteSubGraphic);
            for (int i = 0; i < fallbackSubs.Count; i++)
            {
                TexpixSubGraphic sub = fallbackSubs[i];
                DestroySubGraphic(ref sub);
            }
            fallbackSubs.Clear();
            if (runtimeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(runtimeMaterial);
                else
                    DestroyImmediate(runtimeMaterial);
                runtimeMaterial = null;
            }
            base.OnDestroy();
        }

        void SubscribeFont()
        {
            UnsubscribeFont();
            if (font == null)
                return;
            foreach (TexpixFontAsset chainFont in font.ResolvedChain)
            {
                chainFont.AtlasChanged += OnAtlasChanged;
                subscribedFonts.Add(chainFont);
            }
        }

        void UnsubscribeFont()
        {
            foreach (TexpixFontAsset subscribed in subscribedFonts)
            {
                if (subscribed != null)
                    subscribed.AtlasChanged -= OnAtlasChanged;
            }
            subscribedFonts.Clear();
        }

        static void DestroySubGraphic(ref TexpixSubGraphic sub)
        {
            if (sub == null)
                return;
            if (Application.isPlaying)
                Destroy(sub.gameObject);
            else
                DestroyImmediate(sub.gameObject);
            sub = null;
        }

        void OnAtlasChanged()
        {
            // Fired on atlas texture recreation (growth) and atlas resets — both
            // require a repaint, including our own (a reset invalidates quads already
            // emitted during the current populate). Dirtying inside the canvas
            // rebuild loop is unsupported, so defer to the next canvas update.
            if (populating || CanvasUpdateRegistry.IsRebuildingGraphics() || CanvasUpdateRegistry.IsRebuildingLayout())
            {
                if (!pendingAtlasDirty)
                {
                    pendingAtlasDirty = true;
                    Canvas.willRenderCanvases += DeferredAtlasDirty;
                }
            }
            else
            {
                SetAllDirty();
            }
        }

        void DeferredAtlasDirty()
        {
            Canvas.willRenderCanvases -= DeferredAtlasDirty;
            pendingAtlasDirty = false;
            if (this != null && isActiveAndEnabled)
                SetAllDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (font == null || !font.IsReady || string.IsNullOrEmpty(text))
            {
                spriteSubGraphic?.ClearMesh();
                return;
            }

            Rect rect = rectTransform.rect;
            var settings = new TexpixLayoutSettings
            {
                maxWidthPx = Mathf.FloorToInt(rect.width / pixelScale),
                maxHeightPx = Mathf.FloorToInt(rect.height / pixelScale),
                horizontalAlignment = horizontalAlignment,
                verticalAlignment = verticalAlignment,
                wrapMode = wrapMode,
                overflow = overflow,
                richText = richText,
                spriteAsset = spriteAsset,
            };

            populating = true;
            try
            {
                TexpixTextGenerator.Generate(font, text, in settings, s_Quads,
                    spriteAsset != null ? s_SpriteQuads : null);
            }
            finally
            {
                populating = false;
            }

            // Layout origin is the rect's top-left corner; content extends toward -y.
            // Snapping keeps glyph corners on multiples of pixelScale in local space so
            // a pixel-perfect canvas samples texels 1:1.
            Vector2 origin = new(rect.xMin, rect.yMax);
            if (snapToPixelGrid)
                origin = new Vector2(
                    Mathf.Round(origin.x / pixelScale) * pixelScale,
                    Mathf.Round(origin.y / pixelScale) * pixelScale);

            Color componentColor = color;
            foreach (TexpixQuad quad in s_Quads)
            {
                if (quad.fontIndex != 0)
                    continue;

                float x0 = origin.x + quad.x * pixelScale;
                float y0 = origin.y + quad.y * pixelScale;
                float x1 = origin.x + (quad.x + quad.width) * pixelScale;
                float y1 = origin.y + (quad.y + quad.height) * pixelScale;
                Color32 vertexColor = componentColor * (Color)quad.color;

                int vertexIndex = vh.currentVertCount;
                vh.AddVert(new Vector3(x0, y0), vertexColor, new Vector4(quad.atlasX, quad.atlasY));
                vh.AddVert(new Vector3(x0, y1), vertexColor, new Vector4(quad.atlasX, quad.atlasY + quad.height));
                vh.AddVert(new Vector3(x1, y1), vertexColor, new Vector4(quad.atlasX + quad.width, quad.atlasY + quad.height));
                vh.AddVert(new Vector3(x1, y0), vertexColor, new Vector4(quad.atlasX + quad.width, quad.atlasY));
                vh.AddTriangle(vertexIndex, vertexIndex + 1, vertexIndex + 2);
                vh.AddTriangle(vertexIndex + 2, vertexIndex + 3, vertexIndex);
            }

            UploadFallbackQuads(origin);
            UploadSpriteQuads(origin);
        }

        void UploadFallbackQuads(Vector2 origin)
        {
            Color componentColor = color;
            for (int fi = 0; fi < fallbackSubs.Count; fi++)
            {
                TexpixSubGraphic sub = fallbackSubs[fi];
                int fontIndex = fi + 1;

                s_SpriteVerts.Clear();
                s_SpriteColors.Clear();
                s_SpriteUvs.Clear();
                s_SpriteIndices.Clear();

                foreach (TexpixQuad quad in s_Quads)
                {
                    if (quad.fontIndex != fontIndex)
                        continue;

                    float x0 = origin.x + quad.x * pixelScale;
                    float y0 = origin.y + quad.y * pixelScale;
                    float x1 = origin.x + (quad.x + quad.width) * pixelScale;
                    float y1 = origin.y + (quad.y + quad.height) * pixelScale;
                    Color32 vertexColor = componentColor * (Color)quad.color;

                    int vertexIndex = s_SpriteVerts.Count;
                    s_SpriteVerts.Add(new Vector3(x0, y0));
                    s_SpriteVerts.Add(new Vector3(x0, y1));
                    s_SpriteVerts.Add(new Vector3(x1, y1));
                    s_SpriteVerts.Add(new Vector3(x1, y0));
                    s_SpriteColors.Add(vertexColor);
                    s_SpriteColors.Add(vertexColor);
                    s_SpriteColors.Add(vertexColor);
                    s_SpriteColors.Add(vertexColor);
                    // The Texpix shader expects atlas font-pixel coordinates in uv0.
                    s_SpriteUvs.Add(new Vector2(quad.atlasX, quad.atlasY));
                    s_SpriteUvs.Add(new Vector2(quad.atlasX, quad.atlasY + quad.height));
                    s_SpriteUvs.Add(new Vector2(quad.atlasX + quad.width, quad.atlasY + quad.height));
                    s_SpriteUvs.Add(new Vector2(quad.atlasX + quad.width, quad.atlasY));
                    s_SpriteIndices.Add(vertexIndex);
                    s_SpriteIndices.Add(vertexIndex + 1);
                    s_SpriteIndices.Add(vertexIndex + 2);
                    s_SpriteIndices.Add(vertexIndex + 2);
                    s_SpriteIndices.Add(vertexIndex + 3);
                    s_SpriteIndices.Add(vertexIndex);
                }

                if (s_SpriteVerts.Count == 0)
                    sub.ClearMesh();
                else
                    sub.UploadMesh(s_SpriteVerts, s_SpriteColors, s_SpriteUvs, s_SpriteIndices);
            }
        }

        void UploadSpriteQuads(Vector2 origin)
        {
            if (spriteSubGraphic == null)
                return;
            if (spriteAsset == null || spriteAsset.Texture == null || s_SpriteQuads.Count == 0)
            {
                spriteSubGraphic.ClearMesh();
                return;
            }

            s_SpriteVerts.Clear();
            s_SpriteColors.Clear();
            s_SpriteUvs.Clear();
            s_SpriteIndices.Clear();

            float invW = 1f / spriteAsset.Texture.width;
            float invH = 1f / spriteAsset.Texture.height;
            float componentAlpha = color.a;
            foreach (TexpixQuad quad in s_SpriteQuads)
            {
                Color tinted = quad.color;
                tinted.a *= componentAlpha;
                Color32 spriteColor = tinted;
                float x0 = origin.x + quad.x * pixelScale;
                float y0 = origin.y + quad.y * pixelScale;
                float x1 = origin.x + (quad.x + quad.width) * pixelScale;
                float y1 = origin.y + (quad.y + quad.height) * pixelScale;
                float u0 = quad.atlasX * invW;
                float v0 = quad.atlasY * invH;
                float u1 = (quad.atlasX + quad.width) * invW;
                float v1 = (quad.atlasY + quad.height) * invH;

                int vertexIndex = s_SpriteVerts.Count;
                s_SpriteVerts.Add(new Vector3(x0, y0));
                s_SpriteVerts.Add(new Vector3(x0, y1));
                s_SpriteVerts.Add(new Vector3(x1, y1));
                s_SpriteVerts.Add(new Vector3(x1, y0));
                s_SpriteColors.Add(spriteColor);
                s_SpriteColors.Add(spriteColor);
                s_SpriteColors.Add(spriteColor);
                s_SpriteColors.Add(spriteColor);
                s_SpriteUvs.Add(new Vector2(u0, v0));
                s_SpriteUvs.Add(new Vector2(u0, v1));
                s_SpriteUvs.Add(new Vector2(u1, v1));
                s_SpriteUvs.Add(new Vector2(u1, v0));
                s_SpriteIndices.Add(vertexIndex);
                s_SpriteIndices.Add(vertexIndex + 1);
                s_SpriteIndices.Add(vertexIndex + 2);
                s_SpriteIndices.Add(vertexIndex + 2);
                s_SpriteIndices.Add(vertexIndex + 3);
                s_SpriteIndices.Add(vertexIndex);
            }

            spriteSubGraphic.UploadMesh(s_SpriteVerts, s_SpriteColors, s_SpriteUvs, s_SpriteIndices);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            pixelScale = Mathf.Max(0.01f, pixelScale);
            SubscribeFont(); // re-resolves the fallback chain
            ApplyMaterialProperties();
            // Object creation is not allowed inside OnValidate; defer sub-graphic setup.
            UnityEditor.EditorApplication.delayCall += () =>
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
    }
}
