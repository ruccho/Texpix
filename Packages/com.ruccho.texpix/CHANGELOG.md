# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-07-20

Initial development version.

### Added
- 2bpp (4-level) R8 glyph atlas with 4-/8-neighbor outline encoding, grid-based
  dynamic packing, growth, and exhaustion auto-reset.
- Internal FontEngine access via reflection-resolved managed function pointers.
- Dynamic and editor-baked static font assets (`TexpixFontAsset`), font fallback
  chain, kerning.
- Layout engine: word/CJK wrapping with kinsoku, 9-way alignment, overflow modes
  (Overflow/Truncate/Ellipsis), pixel snapping, surrogate pairs.
- Minimal rich text: `<color>`, `<sprite>` (`tint=1`), `<noparse>`, `<br>`.
- Inline sprites (`TexpixSpriteAsset`) rendered through `TexpixSubGraphic`.
- `Texpix/UI Default` shader, public `Texpix.hlsl` include, and a "Custom Shader"
  sample (`Texpix/Samples/Rainbow`).
- uGUI component `TexpixText` (Mask / RectMask2D compatible).
