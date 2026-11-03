using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace Texpix
{
    /// <summary>
    ///     Access to internal <see cref="FontEngine" /> members. Two internal API surfaces are supported:
    ///     <list type="bullet">
    ///         <item>
    ///             Current-face overloads (verified on Unity 6000.3 to 6000.5, see docs/notes/fontengine-access.md).
    ///             Reflection resolves each MethodInfo once; calls go through managed function pointers so there
    ///             is no per-call boxing or argument array allocation.
    ///         </item>
    ///         <item>
    ///             FontFaceHandle-first overloads (verified on Unity 6000.6.7f1, where the current-face overloads
    ///             were removed). FontFaceHandle is an internal type, so the handle is held as a boxed
    ///             <see cref="object" /> and the members are called with <see cref="MethodBase.Invoke(object, object[])" />.
    ///             These calls run only when a glyph is rasterized or the kerning table is loaded.
    ///         </item>
    ///     </list>
    ///     The surface is selected at runtime by the presence of the FontFaceHandle type.
    ///     In the current-face mode the face handle arguments are ignored and may be null.
    /// </summary>
    internal static unsafe class FontEngineBridge
    {
        private const string FaceHandleTypeName = "UnityEngine.TextCore.LowLevel.FontFaceHandle";

        private static bool _sBound;
        private static bool _sUsesFaceHandle;

        // Current-face mode.
        private static delegate*<uint, int, GlyphPackingMode, List<GlyphRect>, List<GlyphRect>, GlyphRenderMode,
            Texture2D, out Glyph, bool> _sTryAddGlyphToTexture;

        private static delegate*<GlyphPairAdjustmentRecord[]> _sGetAllPairAdjustmentRecords;

        // FontFaceHandle mode.
        private static MethodInfo _sLoadFontFaceFromFontWithHandle;
        private static MethodInfo _sLoadFontFaceFromFamilyNameWithHandle;
        private static MethodInfo _sTryAddGlyphToTextureWithHandle;
        private static MethodInfo _sGetAllPairAdjustmentRecordsWithHandle;

        /// <summary>
        ///     True when the running Unity version requires a face handle for the internal members.
        ///     Face handles must then be obtained with the LoadFontFace overloads of this class.
        /// </summary>
        public static bool UsesFaceHandle
        {
            get
            {
                EnsureBound();
                return _sUsesFaceHandle;
            }
        }

        public static void EnsureBound()
        {
            if (_sBound)
                return;

            var faceHandleType = typeof(FontEngine).Assembly.GetType(FaceHandleTypeName);
            if (faceHandleType != null)
                BindFaceHandleSurface(faceHandleType);
            else
                BindCurrentFaceSurface();

            _sUsesFaceHandle = faceHandleType != null;
            _sBound = true;
        }

        private static void BindCurrentFaceSurface()
        {
            _sTryAddGlyphToTexture =
                (delegate*<uint, int, GlyphPackingMode, List<GlyphRect>, List<GlyphRect>, GlyphRenderMode, Texture2D,
                    out Glyph, bool>)
                GetFunctionPointer(Resolve("TryAddGlyphToTexture",
                    typeof(uint), typeof(int), typeof(GlyphPackingMode), typeof(List<GlyphRect>),
                    typeof(List<GlyphRect>),
                    typeof(GlyphRenderMode), typeof(Texture2D), typeof(Glyph).MakeByRefType()));

            _sGetAllPairAdjustmentRecords = (delegate*<GlyphPairAdjustmentRecord[]>)
                GetFunctionPointer(Resolve("GetAllPairAdjustmentRecords"));
        }

        private static void BindFaceHandleSurface(Type faceHandleType)
        {
            _sLoadFontFaceFromFontWithHandle = Resolve("LoadFontFace",
                typeof(Font), typeof(float), typeof(int), faceHandleType.MakeByRefType());

            _sLoadFontFaceFromFamilyNameWithHandle = Resolve("LoadFontFace",
                typeof(string), typeof(string), typeof(float), faceHandleType.MakeByRefType());

            _sTryAddGlyphToTextureWithHandle = Resolve("TryAddGlyphToTexture",
                faceHandleType, typeof(uint), typeof(int), typeof(GlyphPackingMode), typeof(List<GlyphRect>),
                typeof(List<GlyphRect>), typeof(GlyphRenderMode), typeof(Texture2D), typeof(Glyph).MakeByRefType());

            _sGetAllPairAdjustmentRecordsWithHandle = Resolve("GetAllPairAdjustmentRecords", faceHandleType);
        }

        /// <summary>
        ///     Loads a face and returns its handle. In the current-face mode this is equivalent to
        ///     <see cref="FontEngine.LoadFontFace(Font, float, int)" /> and the returned handle is null.
        /// </summary>
        public static FontEngineError LoadFontFace(Font font, float pointSize, int faceIndex, out object faceHandle)
        {
            EnsureBound();
            if (!_sUsesFaceHandle)
            {
                faceHandle = null;
                return FontEngine.LoadFontFace(font, pointSize, faceIndex);
            }

            var args = new object[] { font, pointSize, faceIndex, null };
            var error = (FontEngineError)_sLoadFontFaceFromFontWithHandle.Invoke(null, args);
            faceHandle = args[3];
            return error;
        }

        /// <summary>
        ///     Loads a system font face and returns its handle. In the current-face mode this is equivalent to
        ///     <see cref="FontEngine.LoadFontFace(string, string, float)" /> and the returned handle is null.
        /// </summary>
        public static FontEngineError LoadFontFace(string familyName, string styleName, float pointSize,
            out object faceHandle)
        {
            EnsureBound();
            if (!_sUsesFaceHandle)
            {
                faceHandle = null;
                return FontEngine.LoadFontFace(familyName, styleName, pointSize);
            }

            var args = new object[] { familyName, styleName, pointSize, null };
            var error = (FontEngineError)_sLoadFontFaceFromFamilyNameWithHandle.Invoke(null, args);
            faceHandle = args[3];
            return error;
        }

        public static bool TryAddGlyphToTexture(object faceHandle, uint glyphIndex, int padding,
            GlyphPackingMode packingMode, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects,
            GlyphRenderMode renderMode, Texture2D texture, out Glyph glyph)
        {
            EnsureBound();
            if (!_sUsesFaceHandle)
                return _sTryAddGlyphToTexture(glyphIndex, padding, packingMode, freeGlyphRects, usedGlyphRects,
                    renderMode, texture, out glyph);

            var args = new object[]
            {
                faceHandle, glyphIndex, padding, packingMode, freeGlyphRects, usedGlyphRects, renderMode, texture, null
            };
            var added = (bool)_sTryAddGlyphToTextureWithHandle.Invoke(null, args);
            glyph = (Glyph)args[8];
            return added;
        }

        public static GlyphPairAdjustmentRecord[] GetAllPairAdjustmentRecords(object faceHandle)
        {
            EnsureBound();
            if (!_sUsesFaceHandle)
                return _sGetAllPairAdjustmentRecords();

            return (GlyphPairAdjustmentRecord[])_sGetAllPairAdjustmentRecordsWithHandle.Invoke(null,
                new[] { faceHandle });
        }

        private static MethodInfo Resolve(string name, params Type[] signature)
        {
            var method = typeof(FontEngine).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic, null,
                signature, null);
            if (method == null)
                throw new MissingMethodException(
                    $"Texpix: internal FontEngine.{name}({string.Join(", ", Array.ConvertAll(signature, t => t.Name))}) " +
                    "was not found. The internal FontEngine API surface likely changed in this Unity version. " +
                    "Update FontEngineBridge bindings.");
            return method;
        }

        private static void* GetFunctionPointer(MethodInfo method)
        {
            return (void*)method.MethodHandle.GetFunctionPointer();
        }
    }
}
