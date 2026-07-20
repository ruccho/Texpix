using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Texpix
{
    /// <summary>
    ///     Child renderer used by <see cref="TexpixText" /> for content that needs a
    ///     different texture/material than the font atlas (inline sprites). Geometry is
    ///     owned by the parent and pushed directly via CanvasRenderer.SetMesh; the
    ///     graphic's own rebuild never generates vertices (SetVerticesDirty is a no-op)
    ///     so the pushed mesh is not clobbered. Masking/stencil behaves like any
    ///     MaskableGraphic.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TexpixSubGraphic : MaskableGraphic
    {
        private Mesh _mesh;
        private Texture _texture;

        public override Texture mainTexture => _texture != null ? _texture : s_WhiteTexture;

        protected override void OnDestroy()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying)
                    Destroy(_mesh);
                else
                    DestroyImmediate(_mesh);
                _mesh = null;
            }

            base.OnDestroy();
        }

        public void SetTexture(Texture value)
        {
            if (_texture == value)
                return;
            _texture = value;
            SetMaterialDirty();
        }

        /// <summary>Geometry is pushed by the parent; the normal rebuild path must not clear it.</summary>
        public override void SetVerticesDirty()
        {
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }

        public void UploadMesh(List<Vector3> vertices, List<Color32> colors, List<Vector2> uvs, List<int> indices)
        {
            if (_mesh == null)
                _mesh = new Mesh { name = "Texpix Sprites", hideFlags = HideFlags.HideAndDontSave };
            _mesh.Clear();
            _mesh.SetVertices(vertices);
            _mesh.SetColors(colors);
            _mesh.SetUVs(0, uvs);
            _mesh.SetTriangles(indices, 0, false);
            canvasRenderer.SetMesh(_mesh);
        }

        public void ClearMesh()
        {
            if (_mesh == null)
                return;
            _mesh.Clear();
            canvasRenderer.SetMesh(_mesh);
        }
    }
}