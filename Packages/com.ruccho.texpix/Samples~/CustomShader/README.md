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
- `TexpixShade(level, fillColor, outlineColor, outlineMode)` — standard
  fill/outline resolve

Vertices carry **atlas font-pixel coordinates** in `uv0` (not normalized UVs) and
the vertex color is the text color (component color × rich-text color).

Usage: create a material from `Texpix/Samples/Rainbow` and assign it to
`TexpixText.material`. Keep the UI stencil/clip properties and pragmas if you copy
this as a starting point — they are what make Mask / RectMask2D work.
