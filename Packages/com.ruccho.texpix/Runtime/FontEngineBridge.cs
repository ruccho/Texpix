using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace Texpix
{
    /// <summary>
    ///     Access to internal <see cref="FontEngine" /> members. Reflection is used only to
    ///     resolve each MethodInfo once; calls go through managed function pointers so there
    ///     is no per-call boxing or argument array allocation.
    ///     Signatures verified on Unity 6000.3 (see docs/notes/fontengine-access.md).
    ///     Newer Unity versions (6000.7+) move these to FontFaceHandle-first overloads;
    ///     binding failures report the expected signature to make that visible.
    /// </summary>
    internal static unsafe class FontEngineBridge
    {
        private static bool _sBound;

        private static delegate*<uint, int, GlyphPackingMode, List<GlyphRect>, List<GlyphRect>, GlyphRenderMode,
            Texture2D, out Glyph, bool> _sTryAddGlyphToTexture;

        private static delegate*<GlyphPairAdjustmentRecord[]> _sGetAllPairAdjustmentRecords;

        public static void EnsureBound()
        {
            if (_sBound)
                return;

            _sTryAddGlyphToTexture =
                (delegate*<uint, int, GlyphPackingMode, List<GlyphRect>, List<GlyphRect>, GlyphRenderMode, Texture2D,
                    out Glyph, bool>)
                GetFunctionPointer(Resolve("TryAddGlyphToTexture",
                    typeof(uint), typeof(int), typeof(GlyphPackingMode), typeof(List<GlyphRect>),
                    typeof(List<GlyphRect>),
                    typeof(GlyphRenderMode), typeof(Texture2D), typeof(Glyph).MakeByRefType()));

            _sGetAllPairAdjustmentRecords = (delegate*<GlyphPairAdjustmentRecord[]>)
                GetFunctionPointer(Resolve("GetAllPairAdjustmentRecords"));

            _sBound = true;
        }

        public static bool TryAddGlyphToTexture(uint glyphIndex, int padding, GlyphPackingMode packingMode,
            List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects, GlyphRenderMode renderMode,
            Texture2D texture, out Glyph glyph)
        {
            EnsureBound();
            return _sTryAddGlyphToTexture(glyphIndex, padding, packingMode, freeGlyphRects, usedGlyphRects, renderMode,
                texture, out glyph);
        }

        public static GlyphPairAdjustmentRecord[] GetAllPairAdjustmentRecords()
        {
            EnsureBound();
            return _sGetAllPairAdjustmentRecords();
        }

        private static MethodInfo Resolve(string name, params Type[] signature)
        {
            var method = typeof(FontEngine).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic, null,
                signature, null);
            if (method == null)
                throw new MissingMethodException(
                    $"Texpix: internal FontEngine.{name}({string.Join(", ", Array.ConvertAll(signature, t => t.Name))}) " +
                    "was not found. The internal FontEngine API surface likely changed in this Unity version " +
                    "(6000.7+ uses FontFaceHandle-first overloads). Update FontEngineBridge bindings.");
            return method;
        }

        private static void* GetFunctionPointer(MethodInfo method)
        {
            return (void*)method.MethodHandle.GetFunctionPointer();
        }
    }
}