using System;
using System.Collections.Generic;
using UnityEngine;

namespace Texpix
{
    /// <summary>
    ///     Inline sprite sheet for Texpix text. Entries reference pixel rects in an RGBA
    ///     texture (point-filtered recommended); metrics follow the glyph convention:
    ///     the quad is placed at pen + bearingX, baseline + bearingY - height.
    /// </summary>
    [CreateAssetMenu(fileName = "TexpixSpriteAsset", menuName = "Texpix/Sprite Asset")]
    public sealed class TexpixSpriteAsset : ScriptableObject
    {
        [SerializeField] private Texture2D texture;
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        [NonSerialized] private Dictionary<string, int> _lookup;

        public Texture2D Texture => texture;
        public int EntryCount => entries.Length;

        private void OnValidate()
        {
            _lookup = null;
        }

        public static TexpixSpriteAsset Create(Texture2D texture, Entry[] entries)
        {
            var asset = CreateInstance<TexpixSpriteAsset>();
            asset.texture = texture;
            asset.entries = entries ?? Array.Empty<Entry>();
            return asset;
        }

        public bool TryGetEntry(string name, out Entry entry)
        {
            if (_lookup == null || _lookup.Count != entries.Length)
            {
                _lookup = new Dictionary<string, int>(entries.Length);
                for (var i = 0; i < entries.Length; i++)
                    _lookup[entries[i].name] = i;
            }

            if (_lookup.TryGetValue(name, out var index))
            {
                entry = entries[index];
                return true;
            }

            entry = default;
            return false;
        }

        /// <summary>
        ///     Allocation-free name lookup for the rich-text parser. Linear scan:
        ///     a Dictionary cannot be queried by span on this runtime, and entry
        ///     counts are small.
        /// </summary>
        public bool TryGetEntry(ReadOnlySpan<char> name, out Entry entry)
        {
            for (var i = 0; i < entries.Length; i++)
                if (name.SequenceEqual(entries[i].name))
                {
                    entry = entries[i];
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
            _lookup = null;
        }
#endif
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
    }
}