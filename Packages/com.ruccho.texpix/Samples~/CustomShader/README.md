# Texpix Custom Shader Sample

`TexpixRainbow.shader` shows how to write a custom shader for Texpix text using the
public include:

```hlsl
#include "Packages/com.ruccho.texpix/Runtime/Shaders/Texpix.hlsl"
```

The include provides:

- `TexpixAtlasUV(fontPx, atlasTexelSize)` / `TexpixSubPixel(fontPx)` /
  `TexpixExtractLevel(atlasR, subPixel)` — decode the 2bpp atlas
  (levels: 3 = fill, 2 = 4-neighbor outline, 1 = diagonal-only outline, 0 = outside)
- `TexpixSampleLevel_Tex2D(tex, texelSize, fontPx)` — one-call decode for
  built-in-pipeline `sampler2D`
- `TexpixUnpackOutline(packed, outlineColor, outlineMode)` — decode the outline
  color/mode carried in `uv0.zw`
- `TexpixShade(level, fillColor, outlineColor, outlineMode)` — standard
  fill/outline resolve

Vertex stream:

- `uv0.xy` — **atlas font-pixel coordinates** (not normalized UVs)
- `uv0.zw` — outline color and mode, packed as integers (`TexpixVertexFormat`
  on the C# side). Unpack in the vertex shader and interpolate the results.
- vertex color — the text color (component color × rich-text color)

Nothing is per-component in material properties, so one material instance serves
every `TexpixText` and uGUI can batch them; a custom shader should keep that
property by reading the outline from the vertex stream rather than from uniforms.

Usage: create a material from `Texpix/Samples/Rainbow` and assign it to
`TexpixText.material`. Keep the UI stencil/clip properties and pragmas if you copy
this as a starting point — they are what make Mask / RectMask2D work.
