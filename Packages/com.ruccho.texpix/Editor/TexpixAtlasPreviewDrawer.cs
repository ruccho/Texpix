using System;
using UnityEditor;
using UnityEngine;

namespace Texpix.Editor
{
    internal enum TexpixAtlasPreviewMode
    {
        /// <summary>Unpacks the packed payload and colors each font pixel by its level.</summary>
        Decoded = 0,

        /// <summary>Shows the packed R8 texels as grayscale, one texel per preview pixel.</summary>
        Raw = 1
    }

    /// <summary>
    ///     Inspector preview for textures in the Texpix atlas format, shared by the dynamic
    ///     and the baked (static) atlas. Unpacking happens in a shader (the same helpers the
    ///     runtime shader uses), so the preview stays live while the dynamic atlas fills up
    ///     and costs nothing on the CPU side.
    /// </summary>
    internal sealed class TexpixAtlasPreviewDrawer : IDisposable
    {
        private const string ShaderName = "Hidden/Texpix/Atlas Preview";
        private const string ShaderPath = "Packages/com.ruccho.texpix/Editor/TexpixAtlasPreview.shader";
        private const string ModePrefKey = "Texpix.AtlasPreview.Mode";
        private const string ZoomPrefKey = "Texpix.AtlasPreview.Zoom";
        private const float MaxBoxHeight = 384f;

        private static readonly string[] ZoomLabels = { "Fit", "1x", "2x", "4x", "8x" };

        /// <summary>Zoom factor per label; 0 means "fit the available box".</summary>
        private static readonly float[] ZoomScales = { 0f, 1f, 2f, 4f, 8f };

        private static readonly int RawId = Shader.PropertyToID("_Raw");
        private static readonly int AtlasFormatId = Shader.PropertyToID("_AtlasFormat");
        private static readonly int AtlasSizeId = Shader.PropertyToID("_AtlasSize");

        private static readonly Color BackgroundColor = new(0.14f, 0.14f, 0.14f, 1f);
        private static readonly Color FillColor = Color.white;
        private static readonly Color EdgeOutlineColor = new(1f, 0.40f, 0.25f, 1f);
        private static readonly Color DiagonalOutlineColor = new(0.25f, 0.55f, 1f, 1f);

        private Material _material;
        private TexpixAtlasPreviewMode _mode;
        private bool _prefsLoaded;
        private Vector2 _scroll;
        private bool _shaderMissingLogged;
        private int _zoom;

        public void Dispose()
        {
            if (_material == null)
                return;
            UnityEngine.Object.DestroyImmediate(_material);
            _material = null;
        }

        /// <summary>Draws the mode/zoom toolbar followed by the atlas image.</summary>
        public void Draw(Texture2D atlas, TexpixAtlasFormat format)
        {
            LoadPrefs();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var mode = (TexpixAtlasPreviewMode)EditorGUILayout.EnumPopup("Preview", _mode);
                var zoom = EditorGUILayout.Popup(_zoom, ZoomLabels, GUILayout.Width(50f));
                if (EditorGUI.EndChangeCheck())
                {
                    _mode = mode;
                    _zoom = zoom;
                    EditorPrefs.SetInt(ModePrefKey, (int)_mode);
                    EditorPrefs.SetInt(ZoomPrefKey, _zoom);
                }
            }

            if (atlas == null)
                return;

            var decoded = _mode == TexpixAtlasPreviewMode.Decoded;
            // Decoded mode expands each texel into the font pixels it packs.
            float srcWidth = decoded ? atlas.width * TexpixAtlas.PixelsPerTexelOf(format) : atlas.width;
            float srcHeight = atlas.height;
            if (srcWidth <= 0f || srcHeight <= 0f)
                return;

            var available = Mathf.Max(64f, EditorGUIUtility.currentViewWidth - 40f);
            var scale = ZoomScales[_zoom] > 0f
                ? ZoomScales[_zoom]
                : Mathf.Min(available / srcWidth, MaxBoxHeight / srcHeight);
            var drawWidth = Mathf.Max(1f, srcWidth * scale);
            var drawHeight = Mathf.Max(1f, srcHeight * scale);

            var scrolled = drawWidth > available || drawHeight > MaxBoxHeight;
            if (scrolled)
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(MaxBoxHeight));

            var rect = GUILayoutUtility.GetRect(drawWidth, drawHeight,
                GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            if (Event.current.type == EventType.Repaint)
                DrawAtlas(rect, atlas, decoded, format);

            if (scrolled)
                EditorGUILayout.EndScrollView();

            if (decoded)
                DrawLegend(format);
        }

        private void DrawAtlas(Rect rect, Texture2D atlas, bool decoded, TexpixAtlasFormat format)
        {
            EditorGUI.DrawRect(rect, BackgroundColor);

            var material = GetMaterial();
            if (material == null)
            {
                // Without the shader the packed bytes are all we can show.
                EditorGUI.DrawPreviewTexture(rect, atlas, null, ScaleMode.StretchToFill);
                return;
            }

            material.mainTexture = atlas;
            material.SetFloat(RawId, decoded ? 0f : 1f);
            material.SetFloat(AtlasFormatId, (float)format);
            material.SetVector(AtlasSizeId, new Vector4(atlas.width, atlas.height, 0f, 0f));
            EditorGUI.DrawPreviewTexture(rect, atlas, material, ScaleMode.StretchToFill);
        }

        private static void DrawLegend(TexpixAtlasFormat format)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSwatch(FillColor, "Fill");
                // A fill-only atlas stores no outline levels, so those swatches would be dead legend.
                if (format == TexpixAtlasFormat.Outline)
                {
                    DrawSwatch(EdgeOutlineColor, "Edge outline");
                    DrawSwatch(DiagonalOutlineColor, "Diagonal outline");
                }

                GUILayout.FlexibleSpace();
            }
        }

        private static void DrawSwatch(Color color, string label)
        {
            var swatch = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f), GUILayout.Height(10f));
            swatch.y += 3f;
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(swatch, color);
            GUILayout.Label(label, EditorStyles.miniLabel);
            GUILayout.Space(6f);
        }

        private Material GetMaterial()
        {
            if (_material != null)
                return _material;

            // Shader.Find covers editor-folder shaders in the editor; the explicit path is a
            // fallback for the case where the package has not been indexed under that name.
            var shader = Shader.Find(ShaderName) ?? AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
            {
                if (!_shaderMissingLogged)
                {
                    _shaderMissingLogged = true;
                    Debug.LogWarning(
                        $"Texpix: preview shader '{ShaderName}' not found; falling back to the raw packed texture.");
                }

                return null;
            }

            _material = new Material(shader)
            {
                name = "Texpix Atlas Preview",
                hideFlags = HideFlags.HideAndDontSave
            };
            _material.SetColor("_FillColor", FillColor);
            _material.SetColor("_EdgeOutlineColor", EdgeOutlineColor);
            _material.SetColor("_DiagonalOutlineColor", DiagonalOutlineColor);
            _material.SetColor("_OutsideColor", new Color(0f, 0f, 0f, 0f));
            return _material;
        }

        private void LoadPrefs()
        {
            if (_prefsLoaded)
                return;
            _mode = (TexpixAtlasPreviewMode)EditorPrefs.GetInt(ModePrefKey, (int)TexpixAtlasPreviewMode.Decoded);
            _zoom = Mathf.Clamp(EditorPrefs.GetInt(ZoomPrefKey, 0), 0, ZoomLabels.Length - 1);
            _prefsLoaded = true;
        }
    }
}
