# Texpix Custom Shader Sample

`TexpixRainbow.shader` shows how to write a custom shader for Texpix text using the
public include:

```hlsl
#include "Packages/com.ruccho.texpix/Runtime/Shaders/Texpix.hlsl"
```

The include provides:

- `TexpixAtlasUV(fontPx, atlasTexelSize, atlasFormat)` /
  `TexpixSubPixel(fontPx, atlasFormat)` /
  `TexpixExtractLevel(atlasR, subPixel, atlasFormat)` — decode the atlas
  (levels: 3 = fill, 2 = 4-neighbor outline, 1 = diagonal-only outline, 0 = outside)
- `TexpixSampleLevel_Tex2D(tex, texelSize, fontPx, atlasFormat)` — one-call decode for
  built-in-pipeline `sampler2D`
- `TexpixUnpackOutline(packed, outlineColor, outlineMode, atlasFormat)` — decode the
  outline color/mode and the atlas format carried in `uv0.zw`
- `TexpixShade(level, fillColor, outlineColor, outlineMode)` — standard
  fill/outline resolve

Vertex stream:

- `uv0.xy` — **atlas font-pixel coordinates** (not normalized UVs)
- `uv0.zw` — outline color and mode plus the atlas format, packed as integers
  (`TexpixVertexFormat` on the C# side). Unpack in the vertex shader and interpolate
  the results.
- vertex color — the text color (component color × rich-text color)

`atlasFormat` says how many bits the atlas spends on a font pixel — 2 for a font asset
with **Atlas Format = Outline** (4 pixels per texel), 1 for **Fill Only** (8 per texel).
It comes out of `TexpixUnpackOutline` and must be threaded through every decode call;
a 1bpp atlas decoded as 2bpp renders garbage. Fill-only pixels decode straight to the
fill level, so `TexpixShade` and any level comparison stay format-agnostic.

Nothing is per-component in material properties, so one material instance serves
every `TexpixText` and uGUI can batch them; a custom shader should keep that
property by reading the outline from the vertex stream rather than from uniforms.

Usage: create a material from `Texpix/Samples/Rainbow` and assign it to
`TexpixText.material`. Keep the UI stencil/clip properties and pragmas if you copy
this as a starting point — they are what make Mask / RectMask2D work.
