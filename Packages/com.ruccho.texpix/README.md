# Texpix

A pixel-font-specialized text system for Unity (uGUI). No SDF — always
pixel-perfect, nearest-neighbor rendering with a compact 2bpp atlas that encodes
4-/8-neighbor outlines for free at runtime.

## Features

- **2bpp atlas**: R8 texture packs 4 font pixels per texel; each pixel stores
  fill / 4-neighbor outline / diagonal outline / outside. Outline modes are a
  shader switch — no extra texture space or passes.
- **Dynamic & static atlases**: on-demand glyph rasterization via FontEngine
  (grid-based O(1) packing, growth, exhaustion auto-reset), or editor-baked
  static data (glyph table + kerning + atlas sub-asset, no FontEngine at runtime).
- **Layout**: word wrap + CJK character wrap with basic kinsoku, 9-way alignment,
  overflow (clip/ellipsis), kerning, pixel snapping, surrogate pairs.
- **Rich text (minimal)**: `<color>`, `<sprite>` (with `tint=1`), `<noparse>`, `<br>`.
- **Inline sprites**: `TexpixSpriteAsset` + automatic sub-renderer.
- **Font fallback chain** with per-font sub-renderers.
- **Custom shaders**: public `Texpix.hlsl` include; see the "Custom Shader" sample.

## Quick start

1. Import a pixel font TTF, create `Texpix > Font Asset`, assign the font and its
   native pixel size (e.g. 8 for Press Start 2P, 10 for PixelMplus10).
2. Add `UI > Texpix Text` to a Canvas, assign the font asset, set Pixel Scale to
   your canvas-units-per-font-pixel factor (integer recommended).
3. Optional: bake a static atlas from the font asset inspector; add fallback fonts;
   create a `Texpix > Sprite Asset` for inline sprites.

Requires Unity 6000.3+. See `docs/` in the repository for design notes.
