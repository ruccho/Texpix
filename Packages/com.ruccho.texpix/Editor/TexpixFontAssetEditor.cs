using UnityEditor;
using UnityEngine;

namespace Texpix.Editor
{
    [CustomEditor(typeof(TexpixFontAsset))]
    public class TexpixFontAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var asset = (TexpixFontAsset)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Static Baking", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(asset.SourceFont == null))
            {
                if (GUILayout.Button("Bake Static Atlas"))
                {
                    try
                    {
                        asset.Bake();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Texpix bake failed: {e.Message}", asset);
                    }
                }
            }

            Texture2D baked = asset.BakedAtlasTextureForInspector;
            if (baked != null)
            {
                EditorGUILayout.LabelField($"Baked: {asset.BakedGlyphCount} glyphs, {asset.BakedKerningCount} kerning pairs, atlas {baked.width * 4}x{baked.height}px (R8 {baked.width}x{baked.height})");
                if (GUILayout.Button("Clear Baked Data"))
                    asset.ClearBaked();

                Rect rect = GUILayoutUtility.GetRect(128, 128, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(rect, baked, null, ScaleMode.ScaleToFit);
            }
            else if (asset.AtlasMode == TexpixAtlasMode.Static)
            {
                EditorGUILayout.HelpBox("Atlas mode is Static but no baked data exists. Bake or switch to Dynamic.", MessageType.Warning);
            }
        }
    }
}
