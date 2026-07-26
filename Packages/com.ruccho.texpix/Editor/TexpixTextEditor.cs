using System.Collections.Generic;
using UnityEditor;

namespace Texpix.Editor
{
    /// <summary>
    ///     Inspector for <see cref="TexpixText" />. uGUI's <c>GraphicEditor</c> is registered
    ///     for <c>MaskableGraphic</c> only, not its subclasses, so the component would
    ///     otherwise get Unity's default inspector — which
    ///     <see cref="UnityEditor.Editor.DrawDefaultInspector" /> reproduces exactly. The only
    ///     addition is a warning for outline settings that cannot take effect because a font
    ///     in the chain stores no outline pixels (see <see cref="TexpixOutlineFormatWarning" />).
    /// </summary>
    [CustomEditor(typeof(TexpixText))]
    [CanEditMultipleObjects]
    public class TexpixTextEditor : UnityEditor.Editor
    {
        private readonly List<string> _warnings = new();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            _warnings.Clear();
            foreach (var target in targets)
            {
                if (target is not TexpixText text)
                    continue;
                var warning = TexpixOutlineFormatWarning.For(text.OutlineMode, text.Font);
                // A multi-selection usually shares one font, so report each distinct
                // problem once instead of repeating it per component.
                if (warning != null && !_warnings.Contains(warning))
                    _warnings.Add(warning);
            }

            foreach (var warning in _warnings)
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }
    }
}
