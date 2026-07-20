# Texpix

[日本語](README.ja.md)

<img width="1920" height="1080" alt="Image" src="https://github.com/user-attachments/assets/136cda22-0931-4141-a566-9528d3ca30ca" />

**A pixel-font rendering system for Unity (uGUI).**

Texpix draws pixel fonts the way they were drawn — one font pixel, one screen block, never blurred, never half-covered. It replaces TextMesh Pro for pixel-art UI instead of fighting it into shape.

## Why not just TextMesh Pro?

TextMesh Pro is built around SDF (signed distance field) text, which is designed to stay smooth at any size. Pixel fonts want the exact opposite.

- **Pixel-perfect by default.** No SDF, no anti-aliasing, no sampling settings to tune. Glyphs are rasterized once at the font's native pixel size and drawn with nearest-neighbor filtering, snapped to the pixel grid. Set the font's pixel size and an integer scale, and it just looks right — at any zoom level.
- **Outlines that actually work.** 4-neighbor and 8-neighbor pixel outlines — the classic 1px game-UI look — are a single dropdown. TMP's dilate-based outline is a distance-field effect and cannot produce a clean 1px neighbor outline. In Texpix, the outline costs no extra texture, no extra draw call, and no extra pass: it is already encoded in the atlas.
- **Cheap atlases.** Each glyph pixel needs only 4 states, so 4 font pixels are packed into one byte of an R8 texture. That's roughly 1/8 the memory of an equivalent RGBA atlas — and it holds the outline data too.

Use TMP for body text and free scaling. Use Texpix when the font is the art.

## Requirements

- Unity 6000.3 or newer
- uGUI (Canvas). UI Toolkit is not supported.

## Installation

Package Manager → **Add package from git URL…**:

```
https://github.com/ruccho/Texpix.git?path=Packages/com.ruccho.texpix#release
```

## Quick start

1. Import a pixel-font TTF into your project.
2. **Assets → Create → Texpix → Font Asset**. Assign the TTF to **Source Font**, and set **Pixel Size** to the font's native size — the size at which the font was designed. Getting this number right is the whole trick; a wrong value is what makes pixel fonts look mushy.
3. In a Canvas, **GameObject → UI → Texpix Text**.
4. Assign the font asset, type your text, and set **Pixel Scale** to how many canvas units one font pixel should occupy. Use an integer (1, 2, 3, …).

That's it — the atlas is built on demand, so there is nothing to bake while you are iterating.

## Features and settings

### Texpix Text (component)

| Setting | What it does |
| --- | --- |
| **Font** | The `TexpixFontAsset` to render with. |
| **Text** | The string to display. Supports `\n`. |
| **Pixel Scale** | Canvas units per font pixel. Integers keep pixels square and sharp. |
| **Horizontal / Vertical Alignment** | Left / Center / Right × Top / Middle / Bottom (9-way). |
| **Wrap Mode** | `NoWrap`, or `Wrap` — word wrap for spaced text, per-character wrap for CJK with basic kinsoku (line-start/line-end prohibited characters). |
| **Overflow** | `Overflow` (draw past the rect), `Truncate` (drop what doesn't fit), `Ellipsis` (trim and append `…`). |
| **Letter Spacing** | Extra spacing between characters/sprites in font pixels; may be negative. Participates in wrapping and alignment like kerning. |
| **Line Spacing** | Added to the font's line height in font pixels; may be negative. |
| **Snap To Pixel Grid** | Rounds the layout origin to a multiple of Pixel Scale so glyph texels stay 1:1. Leave on. |
| **Rich Text** | Enables the tag set below. |
| **Sprite Asset** | `TexpixSpriteAsset` used by `<sprite>` tags. |
| **Outline Mode** | `None`, `FourNeighbor`, `EightNeighbor`. |
| **Outline Color** | Color of the outline pixels. |

The component is a `MaskableGraphic`, so `Mask` and `RectMask2D` work as usual.

### Rich text

A deliberately small tag set:

- `<color=#RRGGBB>` / `<color=red>` … `</color>` (nestable)
- `<sprite=name>` / `<sprite index=0>`, with `tint=1` to tint by the current color
- `<noparse>…</noparse>`
- `<br>`

Unknown tags are rendered literally rather than swallowed.

### Font Asset

| Setting | What it does |
| --- | --- |
| **Source Font** | The TTF/OTF to rasterize. |
| **Pixel Size** | The font's native design size, in pixels. |
| **Atlas Mode** | `Dynamic` (glyphs added on demand) or `Static` (baked in the editor). |
| **Fallback Fonts** | Other font assets searched, in order, for glyphs this font lacks. Chains are flattened depth-first and cycles are ignored. |
| **Atlas Width / Max Height** | Size bounds of the dynamic atlas. It starts small and doubles in height as needed. |
| **Bake Character Set** | Characters written into the static atlas by **Bake**. |

The inspector has **Bake** / **Clear** buttons and an atlas preview. Baking serializes the glyph table, kerning pairs, metrics, and the atlas texture as a sub-asset, and switches the asset to Static.

### Sprite Asset

**Assets → Create → Texpix → Sprite Asset**: an RGBA texture plus named entries (pixel rect, bearing, advance). If the texture is imported with Sprite Mode *Multiple*, **Import Entries From Texture Sprites** fills the table for you. Inline sprites sit on the baseline and participate in layout and wrapping.

### Custom shaders

`Runtime/Shaders/Texpix.hlsl` is a public include exposing the atlas decode API (`TexpixAtlasUV`, `TexpixSubPixel`, `TexpixExtractLevel`, `TexpixShade`). Import the **Custom Shader** sample from Package Manager for a working example (hue-cycling fill with the full UI stencil/clip boilerplate). Assign your material to the component's standard `material` slot; fallback sub-renderers inherit it.

---

# Advanced

## How it works

**The atlas is 2 bits per font pixel.** When a glyph is rasterized, each of its pixels is classified into one of four levels: *fill*, *4-neighbor outline*, *diagonal-only outline*, or *outside* — computed once, at ingest time, from a 1-pixel padding ring around the bitmap. Four such pixels are packed into one byte of an R8 texture.

**The outline is a shader switch.** Because both outline classes are already in the atlas, `Outline Mode` only changes which levels the shader treats as opaque: `None` draws level 3, `FourNeighbor` adds level 2, `EightNeighbor` adds level 1. No second pass, no dilation, no extra texture.

**Vertices carry font-pixel coordinates.** Quad UVs are integer atlas font-pixel positions, not normalized UVs; the shader decodes the sub-pixel within the texel itself. Layout likewise runs entirely in integer font-pixel space and is scaled by Pixel Scale at mesh build, which is why the result is exact rather than approximately aligned.

**Glyphs come from Unity's FontEngine.** Dynamic assets rasterize on demand (RASTER mode, thresholded to binary) and pack into a fixed cell grid with O(1) allocation. Static assets carry a baked glyph table and atlas and never touch FontEngine at runtime.

## Getting good performance

- **Bake a static atlas for shipping.** Static mode removes runtime rasterization, the FontEngine dependency, and any first-frame hitch when new text appears. Dynamic mode is for iteration; static is for release — unless the content is genuinely unbounded (user names, chat, localized text you don't know upfront).
- **Size `Atlas Max Height` for your working set.** When a dynamic atlas fills up it is cleared and rebuilt, not evicted per glyph — cheap and predictable, but it costs a one-frame artifact and logs a warning. Seeing that warning repeatedly means the atlas is too small for the glyphs on screen at once.
- **Keep the fallback chain short.** Each fallback font that actually contributes glyphs to a text adds a child renderer with its own atlas texture — that is an extra draw call per fallback, per text. Put the characters you use most in the primary font. The same applies to inline sprites: they draw from the sprite texture, in one extra renderer.
- **Share a material to batch.** By default each `Texpix Text` instantiates its own material (that's how per-component outline color and mode are stored), so components do not batch with each other. For a screen full of labels that share a font and outline style, create one material from `Texpix/UI Default`, set `_OutlineMode` and `_OutlineColor` on it, and assign it to each component's `material` slot. The per-component Outline Mode / Outline Color fields no longer apply in that case — the material's values win.
- **Keep Pixel Scale an integer and let the canvas be pixel-perfect.** Fractional scale, a non-integer canvas scale factor, or a rotated/scaled parent will reintroduce sampling error that no amount of snapping can fix.
- **Changing `Text` re-runs layout.** The layout pass is integer-only and allocation-light, but it is still O(characters). Avoid rewriting long strings every frame; split volatile parts (counters, timers) into their own small components.
- **Atlas growth is deferred, not free.** When a dynamic atlas doubles, its texture is recreated and every text using it refreshes on the next canvas update (one frame late). This is rare and self-correcting, but it is another reason to bake before shipping.

## Limitations

- uGUI only — no UI Toolkit, no TextMeshPro drop-in replacement.
- No justified alignment or line-height multiplier.
- One sprite asset per text component; sprite quads always draw above glyphs.
- Kerning never applies across a font-fallback boundary.
- Static atlases are not trimmed to content height.

## License

MIT
