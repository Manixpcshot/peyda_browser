# Peyda Browser themes

A theme is a single JSON file. Import one via
**Settings → Appearance → Import theme…**, export the active one with
**Export theme…**. Themes are validated field-by-field: invalid values fall
back to safe defaults and a malformed file can never crash the browser -
at worst you get the default "Peyda Dark" look plus a status message.

## Format

```json
{
  "peydaThemeVersion": 1,
  "name": "Peyda Midnight",
  "mode": "dark",
  "accent": "#7C3AED",
  "background": "#0F1117",
  "surface": "#171A23",
  "toolbar": "#151821",
  "textPrimary": "#F8FAFC",
  "textSecondary": "#94A3B8",
  "border": "#262B3A",
  "tabActive": "#1D2130",
  "tabInactive": "#11141D",
  "tabStyle": "rounded",
  "tabPosition": "top",
  "density": "normal",
  "sidebarEnabled": true,
  "transparency": 0.95,
  "cornerRadius": 8,
  "toolbarHeight": 48,
  "uiFontFamily": "Segoe UI Variable Text"
}
```

| Field | Type | Rules |
| --- | --- | --- |
| `peydaThemeVersion` | int | currently `1`; newer files load with known fields only |
| `name` | string | 1-60 chars, no control characters |
| `mode` | `"light"` \| `"dark"` | picking a theme aligns the app mode to it |
| colors (`accent`, `background`, `surface`, `toolbar`, `textPrimary`, `textSecondary`, `border`, `tabActive`, `tabInactive`) | `#RRGGBB` or `#AARRGGBB` | normalized to upper-case; anything else → default for the mode |
| `tabStyle` | `"rounded"` \| `"square"` | stored; visual application comes with the tab-strip restyle |
| `tabPosition` | `"top"` \| `"left"` | `"left"` (vertical tabs) is reserved - not rendered yet |
| `density` | `"compact"` \| `"normal"` \| `"comfortable"` | the value in Settings takes precedence at runtime |
| `sidebarEnabled` | bool | default for the sidebar buttons |
| `transparency` | number `0.5`–`1.0` | applied to window/toolbar surfaces (values outside are clamped) |
| `cornerRadius` | number `0`–`24` | maps to WinUI `ControlCornerRadius` |
| `toolbarHeight` | number `36`–`72` | toolbar height in DIPs (density may clamp further) |
| `uiFontFamily` | string | letters/digits/spaces/commas, max 100 chars |

## Built-in themes

- **Peyda Light** (`peyda-light`)
- **Peyda Dark** (`peyda-dark`, default)
- **Peyda Midnight** (`peyda-midnight`)
- **Peyda Sunset** (`peyda-sunset`)

Built-ins cannot be deleted; export one, tweak the JSON, and import it back
as your own.

## Tips for theme authors

- Keep text contrast in mind: `textPrimary` on `surface` should be at least
  WCAG AA (4.5:1). Peyda does not (yet) auto-correct low contrast - it only
  guarantees the file can't break the app.
- `accent` drives the Windows accent brushes too (buttons, focus rings,
  selection). Peyda derives the darker/lighter accent shades automatically.
- Because pages also react to the effective light/dark mode
  (`prefers-color-scheme`), choose `mode` to match your palette.
