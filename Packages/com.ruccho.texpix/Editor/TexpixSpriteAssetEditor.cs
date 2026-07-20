using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Texpix.Editor
{
    [CustomEditor(typeof(TexpixSpriteAsset))]
    public class TexpixSpriteAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var asset = (TexpixSpriteAsset)target;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(asset.Texture == null))
            {
                // Convenience: fill entries from the Sprite sub-assets of the texture
                // (Sprite Mode: Multiple). Default metrics: sits on the baseline,
                // advance = width; adjust per entry afterwards.
                if (GUILayout.Button("Import Entries From Texture Sprites"))
                {
                    string path = AssetDatabase.GetAssetPath(asset.Texture);
                    List<Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(path)
                        .OfType<Sprite>()
                        .OrderBy(s => s.name)
                        .ToList();
                    if (sprites.Count == 0)
                    {
                        Debug.LogWarning($"Texpix: no Sprite sub-assets found in '{path}'. Set the texture's Sprite Mode to Multiple and slice it first.", asset);
                    }
                    else
                    {
                        var entries = sprites.Select(s => new TexpixSpriteAsset.Entry
                        {
                            name = s.name,
                            x = Mathf.RoundToInt(s.rect.x),
                            y = Mathf.RoundToInt(s.rect.y),
                            width = Mathf.RoundToInt(s.rect.width),
                            height = Mathf.RoundToInt(s.rect.height),
                            bearingX = 0,
                            bearingY = Mathf.RoundToInt(s.rect.height),
                            advance = Mathf.RoundToInt(s.rect.width) + 1,
                        }).ToArray();
                        Undo.RecordObject(asset, "Import Texpix Sprite Entries");
                        asset.SetEntries(entries);
                        EditorUtility.SetDirty(asset);
                        Debug.Log($"Texpix: imported {entries.Length} sprite entries.", asset);
                    }
                }
            }
        }
    }
}
