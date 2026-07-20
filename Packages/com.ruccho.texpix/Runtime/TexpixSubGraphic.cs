using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Texpix
{
    /// <summary>
    /// Child renderer used by <see cref="TexpixText"/> for content that needs a
    /// different texture/material than the font atlas (inline sprites). Geometry is
    /// owned by the parent and pushed directly via CanvasRenderer.SetMesh; the
    /// graphic's own rebuild never generates vertices (SetVerticesDirty is a no-op)
    /// so the pushed mesh is not clobbered. Masking/stencil behaves like any
    /// MaskableGraphic.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TexpixSubGraphic : MaskableGraphic
    {
        Texture texture;
        Mesh mesh;

        public override Texture mainTexture => texture != null ? texture : s_WhiteTexture;

        public void SetTexture(Texture value)
        {
            if (texture == value)
                return;
            texture = value;
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
            if (mesh == null)
                mesh = new Mesh { name = "Texpix Sprites", hideFlags = HideFlags.HideAndDontSave };
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0, false);
            canvasRenderer.SetMesh(mesh);
        }

        public void ClearMesh()
        {
            if (mesh == null)
                return;
            mesh.Clear();
            canvasRenderer.SetMesh(mesh);
        }

        protected override void OnDestroy()
        {
            if (mesh != null)
            {
                if (Application.isPlaying)
                    Destroy(mesh);
                else
                    DestroyImmediate(mesh);
                mesh = null;
            }
            base.OnDestroy();
        }
    }
}
