# Theme Persistence and Dark-Mode Grid Contrast Design

## Problem

The flow canvas resolves `colorNeutralStroke1` once in `FlowCanvas.OnApplyTemplate` and stores the resulting brush in a normal CLR property. In dark mode that token is too bright for a background grid, and the stored brush is not a dynamic resource, so a later theme change cannot reliably update the grid rendering.

The main window's dark-theme menu only changes the in-memory `CommonControlTheme`. It does not restore the choice at startup or save it for the next launch.

## Goals

- Make the canvas grid use a lower-contrast theme token in both light and dark themes.
- Make a user's light/dark selection persist across application restarts.
- Default to light mode when no valid preference exists.
- Keep theme switching responsive and safe when the preference file is missing, malformed, or not writable.
- Keep tests deterministic without reading or writing the real user's settings directory.

## Non-goals

- Do not add system-theme detection or automatic OS theme synchronization.
- Do not persist graph files, window layout, zoom, or other editor preferences.
- Do not change the grid spacing, major-line cadence, thickness, or canvas interaction behavior.

## Design

### Dynamic grid brush

`FlowCanvas.GridBrush` becomes a WPF dependency property registered with `AffectsRender`. During template application, the control calls `SetResourceReference` for `colorNeutralStrokeSubtle`. The render path continues to draw minor and major lines using the same brush and thickness rules.

Using a dependency property gives the brush a real dynamic-resource binding. When `CommonControlTheme.Theme` changes, WPF replaces the resource value and invalidates the canvas render through the dependency property's metadata. The lower-contrast token is used for both modes: the current CommonControls resources resolve it to `#E0E0E0` in light mode and `#0A0A0A` in dark mode.

### User theme preference

Add a small `ThemePreferenceStore` in the host project. Its default path is:

`%LocalAppData%\\NodeCraft\\settings.json`

The JSON shape is intentionally small and versionless:

```json
{
  "theme": "Dark"
}
```

The store exposes a load operation that returns `Light` for a missing file, malformed JSON, an unknown value, or an unusable path. Its save operation creates the parent directory and writes the selected enum name. File-system errors are contained so a preference failure cannot prevent the requested in-memory theme change. The store accepts an explicit path in its constructor so tests can use a temporary file.

### Main-window lifecycle

`MainWindow` receives an optional `ThemePreferenceStore` dependency; production construction uses the default store, while tests inject a temporary store. After XAML initialization, the window:

1. Loads the stored theme, defaulting to light.
2. Applies that theme to the first `CommonControlTheme` in application resources.
3. Synchronizes the checkable menu item's `IsChecked` state without treating startup synchronization as a new user action.

The checked and unchecked handlers apply the requested theme and save it through the store. A guard prevents the initial menu synchronization from causing a redundant save. Existing callers that construct `MainWindow` with only `FlowPage` remain source-compatible through the optional dependency.

## Data flow

```text
settings.json -> ThemePreferenceStore.Load()
                         |
                         v
                 MainWindow startup
                    /             \\
     CommonControlTheme.Theme   menu IsChecked
                |
                v
 FlowCanvas dynamic GridBrush resource

menu toggle -> ChangeTheme -> CommonControlTheme.Theme
                         \\
                          -> ThemePreferenceStore.Save()
```

## Error handling

- Missing or invalid preference content resolves to light mode.
- Directory creation and file writes are best-effort; an `IOException`, `UnauthorizedAccessException`, or JSON serialization failure is swallowed by the store and does not cancel the theme toggle.
- If application resources do not contain a `CommonControlTheme`, theme application returns without throwing, matching the current behavior.
- If the grid resource is unavailable, the dependency property's neutral fallback brush remains usable and rendering continues.

## Testing

Add regression coverage to the existing `NodeCraft.Tests` console test runner:

- `ThemePreferenceStore` returns light for a missing/invalid file and round-trips dark through an injected temporary path.
- `MainWindow` restores a dark preference at construction, checks the menu item, and applies the dark `CommonControlTheme`.
- Toggling the menu updates the theme and writes the selected value; startup synchronization does not require a user action.
- The `FlowCanvas` grid brush is a dynamic-resource-backed dependency property using `colorNeutralStrokeSubtle`, and the themed canvas resolves the expected light and dark brush values after theme changes.

The implementation must first run each new test in a failing state, then make the smallest production change necessary to pass it, followed by the complete existing test runner and solution build where dependency restore is available.

## Acceptance criteria

- Selecting “深色主题”, closing the application, and reopening it starts in dark mode with the menu checked.
- Selecting the light theme persists the light choice for the next launch.
- A first launch without a settings file starts in light mode.
- The dark-mode grid is visibly lower contrast than the current `colorNeutralStroke1` grid and changes when the theme is toggled.
- Existing flow editing, rendering, and test behavior remains unchanged.
