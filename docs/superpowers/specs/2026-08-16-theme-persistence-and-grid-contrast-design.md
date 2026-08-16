# Theme Persistence and Dark-Mode Grid Contrast Design

## Problem

The flow canvas resolves `colorNeutralStroke1` once in `FlowCanvas.OnApplyTemplate` and stores the resulting brush in a normal CLR property. In dark mode that token is too bright for a background grid, and the stored brush is not a dynamic resource, so a later theme change cannot reliably update the grid rendering.

The main window's dark-theme menu only changes the in-memory `CommonControlTheme`. It does not restore the choice at startup or save it for the next launch.

## Goals

- Make the canvas grid use a lower-contrast theme token in both light and dark themes.
- Make a user's light/dark selection persist across application restarts.
- Default to light mode when no valid preference exists.
- Restore the persisted theme before any application UI is constructed.
- Keep theme switching responsive and safe when the preference file is missing, malformed, or not writable.
- Keep tests deterministic without reading or writing the real user's settings directory.

## Non-goals

- Do not add system-theme detection or automatic OS theme synchronization.
- Do not persist graph files, window layout, zoom, or other editor preferences.
- Do not change the grid spacing, major-line cadence, thickness, or canvas interaction behavior.

## Design

### Dynamic grid brush

`FlowCanvas.GridBrush` becomes a WPF dependency property registered with `AffectsRender`. The `FlowCanvas` style in `NodeCraft.Flow/Themes/Flow.xaml` supplies its default value through a `{DynamicResource colorNeutralStrokeSubtle}` setter. `FlowCanvas.OnApplyTemplate` no longer resolves or assigns a grid brush. The render path continues to draw minor and major lines using the same brush and thickness rules.

Using a style setter preserves normal WPF property precedence: callers can still override `GridBrush` locally, while controls that use the default receive a real dynamic-resource binding. When `CommonControlTheme.Theme` changes, WPF replaces the resource value and invalidates the canvas render through the dependency property's metadata. The lower-contrast token is used for both modes. For rationale only, the current CommonControls package resolves it to `#E0E0E0` in light mode and `#0A0A0A` in dark mode; implementation tests bind to the resource key rather than these package-specific values.

### User theme preference

Add a small `ThemePreferenceStore` in the host project. Its default path is:

`%LocalAppData%\NodeCraft\settings.json`

The JSON shape is intentionally small and versionless:

```json
{
  "theme": "Dark"
}
```

The store is registered as a singleton and receives `ILogger<ThemePreferenceStore>`. It exposes a load operation that returns `Light` for a missing file, malformed JSON, an unknown value, or an unusable path. Missing files are an expected first-run case; malformed content and read failures produce a warning log before falling back to light.

The save operation creates the parent directory, serializes the selected enum name to a uniquely named temporary file in that directory, closes the file, and replaces the destination with `File.Move(tempPath, settingsPath, overwrite: true)`. A `finally` block removes a leftover temporary file after a failed write or replace. Keeping both files in the same directory avoids a cross-volume move and makes the final replacement atomic on the supported local Windows file systems.

Load and save catch only expected persistence failures such as `IOException`, `UnauthorizedAccessException`, and `JsonException`. A failed save returns `false` and logs a warning, but it does not roll back the already requested in-memory theme change. The store accepts an explicit path in its constructor so tests can use a temporary file; production construction uses the default local-application-data path.

### Application startup and main-window lifecycle

Theme resource lookup and assignment move into an internal singleton `ApplicationThemeManager` used by both `App` and `MainWindow`. It receives `ILogger<ApplicationThemeManager>`, exposes the currently applied theme, and applies a requested theme to the first `CommonControlTheme` in application resources. During `App.OnStartup`, after the service provider and logging are available but before resolving `FlowPage` or `MainWindow`, the application:

1. Resolves the singleton `ThemePreferenceStore`.
2. Loads the stored theme, defaulting to light.
3. Resolves `ApplicationThemeManager` and applies the loaded theme.
4. Continues plugin loading and resolves the UI services only after the application theme is current.

`MainWindow` receives both singletons as required constructor dependencies. After XAML initialization it reads `ApplicationThemeManager.CurrentTheme` and synchronizes the checkable menu item's `IsChecked` state under an initialization guard, without treating startup synchronization as a user action. Direct test callers pass a temporary-path store and a manager explicitly.

The checked and unchecked handlers use `ApplicationThemeManager` to apply the requested theme and then save it through the store. The UI theme remains changed if persistence fails; the warning log retains the failure details for diagnosis.

## Data flow

```text
settings.json -> ThemePreferenceStore.Load()
                         |
                         v
                    App.OnStartup
                         |
                         v
              CommonControlTheme.Theme
                    /             \
                   v               v
    MainWindow menu IsChecked   FlowCanvas style
                                      |
                                      v
                         dynamic GridBrush resource

menu toggle -> ApplicationThemeManager -> CommonControlTheme.Theme
                         \
                          -> ThemePreferenceStore.Save()
```

## Error handling

- A missing preference file resolves to light mode without a warning because it is the expected first-run state.
- Invalid JSON, unknown theme values, and expected read failures log a warning and resolve to light mode.
- Directory creation, temporary-file writes, and replacement failures log a warning and return `false`; they do not cancel the in-memory theme toggle.
- Temporary files are cleaned in a `finally` block after either success or failure.
- If application resources do not contain a `CommonControlTheme`, theme application returns without throwing and logs a warning, preserving the current non-crashing behavior while making the configuration failure observable.
- If the grid resource is unavailable, the dependency property's neutral fallback brush remains usable and rendering continues.

## Testing

Add regression coverage to the existing `NodeCraft.Tests` console test runner:

- `ThemePreferenceStore` returns light for a missing or invalid file, logs invalid input, and round-trips dark through an injected temporary path.
- Saving dark and then light through the same path causes a new store instance to load light, covering both persisted choices.
- A successful save leaves only `settings.json` and no temporary sibling file.
- `ApplicationThemeManager` reports and applies the current theme, and the `App.OnStartup` integration restores dark before `FlowPage` or `MainWindow` is constructed.
- `MainWindow` reflects the already applied dark theme in its checked menu item without rewriting the preference during startup synchronization.
- Toggling the menu updates the application theme and writes dark, then light, through the injected store.
- `GridBrushProperty` is registered with `AffectsRender`, and the `FlowCanvas` style supplies `{DynamicResource colorNeutralStrokeSubtle}`.
- In a themed window, `FlowCanvas.GridBrush` matches the current `colorNeutralStrokeSubtle` resource in both light and dark modes, changes after a theme switch, and does not remain equal to the dark `colorNeutralStroke1` resource. Tests do not pin the package's hexadecimal color values.

The implementation must first run each new test in a failing state, then make the smallest production change necessary to pass it, followed by the complete existing test runner and solution build where dependency restore is available.

## Acceptance criteria

- Selecting “深色主题”, closing the application, and reopening it starts in dark mode with the menu checked.
- Selecting the light theme persists the light choice for the next launch.
- A first launch without a settings file starts in light mode.
- Theme restoration occurs before any `FlowPage` or `MainWindow` instance is created.
- In each theme, the default canvas grid brush resolves to `colorNeutralStrokeSubtle`; after switching to dark it differs from `colorNeutralStroke1` and redraws without recreating the canvas.
- Expected preference failures do not crash or undo a theme switch and produce a warning log.
- Existing flow editing, rendering, and test behavior remains unchanged.
