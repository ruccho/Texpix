using System;
using UnityEditor;
using UnityEngine;

namespace Texpix.Editor
{
    [CustomEditor(typeof(TexpixFontAsset))]
    public class TexpixFontAssetEditor : UnityEditor.Editor
    {
        private const string PreviewCharactersPrefKey = "Texpix.FontAsset.PreviewCharacters";

        private readonly TexpixAtlasPreviewDrawer _preview = new();
        private string _previewCharacters;

        private void OnDisable()
        {
            _preview.Dispose();
        }

        /// <summary>
        ///     Glyph writes update the dynamic atlas texture in place without raising
        ///     AtlasChanged (existing meshes stay valid), so polling is the only way to keep
        ///     the preview current while text using this font renders.
        /// </summary>
        public override bool RequiresConstantRepaint()
        {
            var asset = (TexpixFontAsset)target;
            return asset != null && asset.AtlasMode == TexpixAtlasMode.Dynamic &&
                   asset.DynamicAtlasForInspector != null;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var asset = (TexpixFontAsset)target;

            if (asset.AtlasMode == TexpixAtlasMode.Dynamic)
            {
                EditorGUILayout.Space();
                DrawDynamicAtlas(asset);
            }

            EditorGUILayout.Space();
            DrawStaticBaking(asset);
        }

        private void DrawDynamicAtlas(TexpixFontAsset asset)
        {
            EditorGUILayout.LabelField("Dynamic Atlas", EditorStyles.boldLabel);

            if (asset.SourceFont == null)
            {
                EditorGUILayout.HelpBox("Assign a source font to use the dynamic atlas.", MessageType.Info);
                return;
            }

            _previewCharacters ??= EditorPrefs.GetString(PreviewCharactersPrefKey, "Texpix 0123");

            EditorGUI.BeginChangeCheck();
            _previewCharacters = EditorGUILayout.TextField("Characters", _previewCharacters);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PreviewCharactersPrefKey, _previewCharacters);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_previewCharacters)))
                {
                    if (GUILayout.Button("Add To Atlas"))
                        AddCharacters(asset, _previewCharacters);
                }

                if (GUILayout.Button("Reset Atlas"))
                    asset.ResetDynamicAtlasForInspector();
            }

            var atlas = asset.DynamicAtlasForInspector;
            if (atlas?.Texture == null)
            {
                EditorGUILayout.HelpBox(
                    "The dynamic atlas has not been built yet. Glyphs are rasterized on demand, so it fills up while text using this font renders — or add characters above.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                $"{asset.DynamicGlyphCountForInspector} glyphs, {atlas.UsedCellCount}/{atlas.Capacity} cells of {atlas.CellWidthPx}x{atlas.CellHeightPx}px, atlas {atlas.WidthPx}x{atlas.HeightPx}px (R8 {atlas.Texture.width}x{atlas.Texture.height})",
                EditorStyles.wordWrappedMiniLabel);
            _preview.Draw(atlas.Texture);
        }

        private void DrawStaticBaking(TexpixFontAsset asset)
        {
            EditorGUILayout.LabelField("Static Baking", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(asset.SourceFont == null))
            {
                if (GUILayout.Button("Bake Static Atlas"))
                    try
                    {
                        asset.Bake();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Texpix bake failed: {e.Message}", asset);
                    }
            }

            var baked = asset.BakedAtlasTextureForInspector;
            if (baked != null)
            {
                EditorGUILayout.LabelField(
                    $"Baked: {asset.BakedGlyphCount} glyphs, {asset.BakedKerningCount} kerning pairs, atlas {baked.width * 4}x{baked.height}px (R8 {baked.width}x{baked.height})",
                    EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Clear Baked Data"))
                    asset.ClearBaked();

                _preview.Draw(baked);
            }
            else if (asset.AtlasMode == TexpixAtlasMode.Static)
            {
                EditorGUILayout.HelpBox("Atlas mode is Static but no baked data exists. Bake or switch to Dynamic.",
                    MessageType.Warning);
            }
        }

        /// <summary>Resolves every codepoint in <paramref name="characters" />, populating the dynamic atlas.</summary>
        private static void AddCharacters(TexpixFontAsset asset, string characters)
        {
            try
            {
                for (var i = 0; i < characters.Length; i++)
                {
                    var c = characters[i];
                    uint codepoint = c;
                    if (char.IsHighSurrogate(c) && i + 1 < characters.Length && char.IsLowSurrogate(characters[i + 1]))
                    {
                        codepoint = (uint)char.ConvertToUtf32(c, characters[i + 1]);
                        i++;
                    }

                    asset.TryGetGlyph(codepoint, out _);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Texpix: failed to populate the dynamic atlas: {e.Message}", asset);
            }
        }
    }
}
