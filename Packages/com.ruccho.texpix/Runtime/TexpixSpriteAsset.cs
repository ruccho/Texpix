using System;
using System.Collections.Generic;
using UnityEngine;

namespace Texpix
{
    /// <summary>
    /// Inline sprite sheet for Texpix text. Entries reference pixel rects in an RGBA
    /// texture (point-filtered recommended); metrics follow the glyph convention:
    /// the quad is placed at pen + bearingX, baseline + bearingY - height.
    /// </summary>
    [CreateAssetMenu(fileName = "TexpixSpriteAsset", menuName = "Texpix/Sprite Asset")]
    public sealed class TexpixSpriteAsset : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string name;
            /// <summary>Bottom-left of the sprite in the texture, in pixels.</summary>
            public int x;
            public int y;
            public int width;
            public int height;
            public int bearingX;
            /// <summary>Baseline-relative offset to the sprite's top edge (height = sits on the baseline).</summary>
            public int bearingY;
            public int advance;
        }

        [SerializeField] Texture2D texture;
        [SerializeField] Entry[] entries = Array.Empty<Entry>();

        [NonSerialized] Dictionary<string, int> lookup;

        public Texture2D Texture => texture;
        public int EntryCount => entries.Length;

        public static TexpixSpriteAsset Create(Texture2D texture, Entry[] entries)
        {
            var asset = CreateInstance<TexpixSpriteAsset>();
            asset.texture = texture;
            asset.entries = entries ?? Array.Empty<Entry>();
            return asset;
        }

        public bool TryGetEntry(string name, out Entry entry)
        {
            if (lookup == null || lookup.Count != entries.Length)
            {
                lookup = new Dictionary<string, int>(entries.Length);
                for (int i = 0; i < entries.Length; i++)
                    lookup[entries[i].name] = i;
            }
            if (lookup.TryGetValue(name, out int index))
            {
                entry = entries[index];
                return true;
            }
            entry = default;
            return false;
        }

        public bool TryGetEntry(int index, out Entry entry)
        {
            if (index >= 0 && index < entries.Length)
            {
                entry = entries[index];
                return true;
            }
            entry = default;
            return false;
        }

#if UNITY_EDITOR
        internal void SetEntries(Entry[] newEntries)
        {
            entries = newEntries ?? Array.Empty<Entry>();
            lookup = null;
        }
#endif

        void OnValidate()
        {
            lookup = null;
        }
    }
}
