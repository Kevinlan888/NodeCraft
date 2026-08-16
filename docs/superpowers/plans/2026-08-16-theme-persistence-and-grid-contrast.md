# Theme Persistence and Dark-Mode Grid Contrast Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist the current user's light/dark selection across NodeCraft restarts and render the flow-canvas grid with a dynamically updating, lower-contrast theme brush.

**Architecture:** Keep file persistence in a focused `ThemePreferenceStore`, and keep application-resource mutation in an `ApplicationThemeManager` singleton. Restore the saved theme in `App.OnStartup` before resolving UI services; let `MainWindow` only synchronize the menu and handle user toggles. Convert `FlowCanvas.GridBrush` to an `AffectsRender` dependency property whose default comes from the `Flow.xaml` dynamic style.

**Tech Stack:** C# 9, .NET 8 WPF (`net8.0-windows`), CommonControls.WPF 1.0.0, Microsoft.Extensions.DependencyInjection/Logging 8, System.Text.Json, the existing self-running `NodeCraft.Tests` console test harness.

## Global Constraints

- A missing, malformed, unreadable, or unknown preference resolves to `Light`; a missing file is the expected first-run case and does not log a warning.
- Restore the saved theme before constructing `FlowPage` or `MainWindow`.
- Theme persistence uses `%LocalAppData%\NodeCraft\settings.json` in production and an injected temporary path in tests.
- Preference writes use a uniquely named sibling temporary file and same-directory replacement; expected persistence failures log a warning, return `false`, and never undo the in-memory theme change.
- The default grid brush is `{DynamicResource colorNeutralStrokeSubtle}`; tests compare resource values and do not pin CommonControls hexadecimal colors.
- Do not change grid spacing, major-line cadence, thickness, zoom, panning, graph files, window layout, or any preference other than theme.
- Do not add OS theme detection or automatic system-theme synchronization.
- Keep themed colors in XAML dynamic resources; do not add hard-coded production colors.
- Host and flow projects remain C# 9 with nullable disabled; `NodeCraft.Tests` remains nullable-enabled.
- Run implementation tests with `--no-restore` first because this workspace already has assets and restricted network access may block NuGet.

---

## File Map

- Create `NodeCraft/Theming/ThemePreferenceStore.cs` — parse, validate, log, and atomically persist the user's theme.
- Create `NodeCraft/Theming/ApplicationThemeManager.cs` — read and update the application `CommonControlTheme` and expose `CurrentTheme`.
- Create `NodeCraft.Tests/ThemeTests.cs` — focused store, manager, startup-order, menu, and dynamic-grid regressions plus a recording logger.
- Modify `NodeCraft/App.xaml.cs` — register both singleton services and restore the saved theme before plugin/UI resolution.
- Modify `NodeCraft/MainWindow.xaml.cs` — inject the services, initialize the menu from `CurrentTheme`, and persist explicit toggles.
- Modify `NodeCraft.Flow/Flow/FlowCanvas.cs` — register `GridBrushProperty` with `AffectsRender` and remove one-time resource lookup.
- Modify `NodeCraft.Flow/Themes/Flow.xaml` — supply the dynamic subtle-stroke brush through the `FlowCanvas` style.
- Modify `NodeCraft.Tests/Program.cs` — invoke the focused theme tests and adapt the existing main-window integration test.

---

### Task 1: Add resilient per-user theme storage

**Files:**

- Create: `NodeCraft/Theming/ThemePreferenceStore.cs`
- Create: `NodeCraft.Tests/ThemeTests.cs`
- Modify: `NodeCraft.Tests/Program.cs:53-72`

**Interfaces:**

- Consumes: `CommonControlTheme.BaseTheme`, `ILogger<ThemePreferenceStore>`, `System.Text.Json`, and a settings-file path.
- Produces:
  - `public ThemePreferenceStore(ILogger<ThemePreferenceStore> logger)`
  - `internal ThemePreferenceStore(string settingsPath, ILogger<ThemePreferenceStore> logger)`
  - `public CommonControlTheme.BaseTheme Load()`
  - `public bool Save(CommonControlTheme.BaseTheme theme)`
  - `internal static string GetDefaultSettingsPath()`

- [ ] **Step 1: Register a focused theme-test entry point and write the missing-file test.**

Add `RunThemeTests();` immediately after `RunExecutionErrorFormatterTests();` in `Program.Main`:

```csharp
RunExecutionErrorFormatterTests();
RunThemeTests();
```

Create `NodeCraft.Tests/ThemeTests.cs` with the first test and reusable helpers:

```csharp
using CommonControls.WPF;
using Microsoft.Extensions.Logging;
using NodeCraft.Theming;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static partial class Program
{
    private static void RunThemeTests()
    {
        Run("theme preferences default to light when settings are missing", () =>
        {
            var directory = CreateThemeTestDirectory();
            try
            {
                var logger = new RecordingLogger<ThemePreferenceStore>();
                var store = new ThemePreferenceStore(
                    Path.Combine(directory, "settings.json"),
                    logger);

                return store.Load() == CommonControlTheme.BaseTheme.Light
                    && logger.Entries.Count == 0;
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    private static string CreateThemeTestDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "nodecraft-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; }
            = new List<(LogLevel Level, string Message, Exception? Exception)>();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new NullScope();

            public void Dispose()
            {
            }
        }
    }
}
```

- [ ] **Step 2: Run the test harness and verify the RED state.**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
```

Expected: build fails with `CS0234` or `CS0246` because `NodeCraft.Theming.ThemePreferenceStore` does not exist.

- [ ] **Step 3: Add the smallest store that satisfies the missing-file behavior.**

Create `NodeCraft/Theming/ThemePreferenceStore.cs`:

```csharp
using CommonControls.WPF;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace NodeCraft.Theming
{
    public sealed class ThemePreferenceStore
    {
        private readonly string _settingsPath;
        private readonly ILogger<ThemePreferenceStore> _logger;

        public ThemePreferenceStore(ILogger<ThemePreferenceStore> logger)
            : this(GetDefaultSettingsPath(), logger)
        {
        }

        internal ThemePreferenceStore(
            string settingsPath,
            ILogger<ThemePreferenceStore> logger)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
                throw new ArgumentException("A settings path is required.", nameof(settingsPath));

            _settingsPath = settingsPath;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public CommonControlTheme.BaseTheme Load()
        {
            return CommonControlTheme.BaseTheme.Light;
        }

        internal static string GetDefaultSettingsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NodeCraft",
                "settings.json");
        }
    }
}
```

- [ ] **Step 4: Re-run the test harness and verify the first GREEN state.**

Run the same `dotnet run` command.

Expected: `PASS theme preferences default to light when settings are missing` and the existing runner still ends with `ALL PASS`.

- [ ] **Step 5: Add failing malformed, unknown, and unusable-path preference tests.**

Append these tests inside `RunThemeTests`:

```csharp
Run("theme preferences log and fall back for invalid content", () =>
{
    var directory = CreateThemeTestDirectory();
    var settingsPath = Path.Combine(directory, "settings.json");
    try
    {
        var logger = new RecordingLogger<ThemePreferenceStore>();
        var store = new ThemePreferenceStore(settingsPath, logger);

        File.WriteAllText(settingsPath, "{");
        var malformed = store.Load();

        File.WriteAllText(settingsPath, "{\"theme\":\"Solarized\"}");
        var unknown = store.Load();

        File.Delete(settingsPath);
        Directory.CreateDirectory(settingsPath);
        var unusable = store.Load();

        return malformed == CommonControlTheme.BaseTheme.Light
            && unknown == CommonControlTheme.BaseTheme.Light
            && unusable == CommonControlTheme.BaseTheme.Light
            && logger.Entries.Count(entry => entry.Level == LogLevel.Warning) == 3;
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
});
```

- [ ] **Step 6: Run the harness and verify the invalid-content test fails for the expected reason.**

Expected: `FAIL theme preferences log and fall back for invalid content` because `Load` does not read or log the file.

- [ ] **Step 7: Implement JSON parsing, enum validation, and warning logs.**

Add `using System.Text.Json;`. Add these members to `ThemePreferenceStore` and replace `Load`:

```csharp
private static readonly JsonSerializerOptions SerializerOptions
    = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

public CommonControlTheme.BaseTheme Load()
{
    try
    {
        if (!File.Exists(_settingsPath))
        {
            if (Directory.Exists(_settingsPath))
            {
                _logger.LogWarning(
                    "Theme settings path '{SettingsPath}' is not a file; using Light.",
                    _settingsPath);
            }

            return CommonControlTheme.BaseTheme.Light;
        }

        var document = JsonSerializer.Deserialize<ThemePreferenceDocument>(
            File.ReadAllText(_settingsPath),
            SerializerOptions);
        if (document != null
            && Enum.TryParse(
                document.Theme,
                ignoreCase: true,
                out CommonControlTheme.BaseTheme theme)
            && Enum.IsDefined(typeof(CommonControlTheme.BaseTheme), theme))
        {
            return theme;
        }

        _logger.LogWarning(
            "Theme settings at '{SettingsPath}' contain an unknown theme; using Light.",
            _settingsPath);
    }
    catch (Exception exception) when (IsPersistenceException(exception))
    {
        _logger.LogWarning(
            exception,
            "Failed to read theme settings from '{SettingsPath}'; using Light.",
            _settingsPath);
    }

    return CommonControlTheme.BaseTheme.Light;
}

private static bool IsPersistenceException(Exception exception)
{
    return exception is IOException
        || exception is UnauthorizedAccessException
        || exception is JsonException
        || exception is NotSupportedException
        || exception is ArgumentException
        || exception is System.Security.SecurityException;
}

private sealed class ThemePreferenceDocument
{
    public string Theme { get; set; }
}
```

- [ ] **Step 8: Re-run the harness and verify invalid content now passes.**

Expected: both theme-preference tests pass and the runner ends with `ALL PASS`.

- [ ] **Step 9: Add failing round-trip, atomic-cleanup, and write-failure tests.**

Append:

```csharp
Run("theme preferences round-trip dark then light without temporary files", () =>
{
    var directory = CreateThemeTestDirectory();
    var settingsPath = Path.Combine(directory, "settings.json");
    try
    {
        var logger = new RecordingLogger<ThemePreferenceStore>();
        var store = new ThemePreferenceStore(settingsPath, logger);

        var darkSaved = store.Save(CommonControlTheme.BaseTheme.Dark);
        var darkLoaded = new ThemePreferenceStore(settingsPath, logger).Load();
        var lightSaved = store.Save(CommonControlTheme.BaseTheme.Light);
        var lightLoaded = new ThemePreferenceStore(settingsPath, logger).Load();
        var files = Directory.GetFiles(directory)
            .Select(Path.GetFileName)
            .ToArray();

        return darkSaved
            && darkLoaded == CommonControlTheme.BaseTheme.Dark
            && lightSaved
            && lightLoaded == CommonControlTheme.BaseTheme.Light
            && files.SequenceEqual(new[] { "settings.json" });
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
});

Run("theme preference write failures are logged and cleaned", () =>
{
    var directory = CreateThemeTestDirectory();
    var settingsPath = Path.Combine(directory, "settings.json");
    Directory.CreateDirectory(settingsPath);
    try
    {
        var logger = new RecordingLogger<ThemePreferenceStore>();
        var store = new ThemePreferenceStore(settingsPath, logger);

        var saved = store.Save(CommonControlTheme.BaseTheme.Dark);

        return !saved
            && logger.Entries.Any(entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains(
                    "Failed to save theme settings",
                    StringComparison.Ordinal))
            && !Directory.GetFiles(directory)
                .Any(path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
});
```

- [ ] **Step 10: Run the harness and verify the save tests are RED.**

Expected: build fails with `CS1061` because `ThemePreferenceStore.Save` does not exist.

- [ ] **Step 11: Implement atomic save, expected-failure logging, and temporary-file cleanup.**

Add this method to `ThemePreferenceStore`:

```csharp
public bool Save(CommonControlTheme.BaseTheme theme)
{
    string tempPath = null;
    try
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrEmpty(directory))
            directory = Directory.GetCurrentDirectory();

        Directory.CreateDirectory(directory);
        tempPath = Path.Combine(
            directory,
            "." + Path.GetFileName(_settingsPath)
                + "." + Guid.NewGuid().ToString("N") + ".tmp");

        var document = new ThemePreferenceDocument
        {
            Theme = theme.ToString(),
        };
        File.WriteAllText(
            tempPath,
            JsonSerializer.Serialize(document, SerializerOptions));
        File.Move(tempPath, _settingsPath, overwrite: true);
        tempPath = null;
        return true;
    }
    catch (Exception exception) when (IsPersistenceException(exception))
    {
        _logger.LogWarning(
            exception,
            "Failed to save theme settings to '{SettingsPath}'.",
            _settingsPath);
        return false;
    }
    finally
    {
        if (!string.IsNullOrEmpty(tempPath))
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception exception) when (IsPersistenceException(exception))
            {
                _logger.LogWarning(
                    exception,
                    "Failed to clean temporary theme settings file '{TempPath}'.",
                    tempPath);
            }
        }
    }
}
```

- [ ] **Step 12: Run the harness and verify all store tests are GREEN.**

Expected: all four theme-preference tests pass, no temporary file remains, and the runner ends with `ALL PASS`.

- [ ] **Step 13: Commit the focused storage change.**

```powershell
git add -- NodeCraft/Theming/ThemePreferenceStore.cs NodeCraft.Tests/ThemeTests.cs NodeCraft.Tests/Program.cs
git commit -m "feat: persist the user theme preference"
```

---

### Task 2: Restore the application theme before UI construction

**Files:**

- Create: `NodeCraft/Theming/ApplicationThemeManager.cs`
- Modify: `NodeCraft/App.xaml.cs:1-79`
- Modify: `NodeCraft.Tests/ThemeTests.cs`

**Interfaces:**

- Consumes: `ResourceDictionary.MergedDictionaries`, `CommonControlTheme`, `ILogger<ApplicationThemeManager>`, and `ThemePreferenceStore.Load()`.
- Produces:
  - `public ApplicationThemeManager(ILogger<ApplicationThemeManager> logger)`
  - `internal ApplicationThemeManager(ResourceDictionary resources, ILogger<ApplicationThemeManager> logger)`
  - `public CommonControlTheme.BaseTheme CurrentTheme { get; }`
  - `public bool Apply(CommonControlTheme.BaseTheme theme)`
- App DI registrations:
  - `services.AddSingleton<ThemePreferenceStore>();`
  - `services.AddSingleton<ApplicationThemeManager>();`

- [ ] **Step 1: Add failing manager behavior tests.**

Add `using System.Windows;` to `ThemeTests.cs` and append:

```csharp
Run("application theme manager applies and reports the current theme", () =>
    RunOnSta(() =>
    {
        var resources = new ResourceDictionary();
        var controlTheme = new CommonControlTheme
        {
            Theme = CommonControlTheme.BaseTheme.Light,
        };
        resources.MergedDictionaries.Add(controlTheme);
        var logger = new RecordingLogger<ApplicationThemeManager>();
        var manager = new ApplicationThemeManager(resources, logger);

        return manager.Apply(CommonControlTheme.BaseTheme.Dark)
            && manager.CurrentTheme == CommonControlTheme.BaseTheme.Dark
            && controlTheme.Theme == CommonControlTheme.BaseTheme.Dark
            && logger.Entries.Count == 0;
    }));

Run("application theme manager logs a missing theme dictionary", () =>
    RunOnSta(() =>
    {
        var logger = new RecordingLogger<ApplicationThemeManager>();
        var manager = new ApplicationThemeManager(
            new ResourceDictionary(),
            logger);

        return !manager.Apply(CommonControlTheme.BaseTheme.Dark)
            && manager.CurrentTheme == CommonControlTheme.BaseTheme.Light
            && logger.Entries.Any(entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains(
                    "CommonControlTheme",
                    StringComparison.Ordinal));
    }));
```

- [ ] **Step 2: Run the harness and verify RED.**

Expected: build fails with `CS0246` because `ApplicationThemeManager` does not exist.

- [ ] **Step 3: Implement the application theme manager.**

Create `NodeCraft/Theming/ApplicationThemeManager.cs`:

```csharp
using CommonControls.WPF;
using Microsoft.Extensions.Logging;
using System;
using System.Windows;

namespace NodeCraft.Theming
{
    public sealed class ApplicationThemeManager
    {
        private readonly Func<ResourceDictionary> _resources;
        private readonly ILogger<ApplicationThemeManager> _logger;

        public ApplicationThemeManager(
            ILogger<ApplicationThemeManager> logger)
            : this(() => Application.Current?.Resources, logger)
        {
        }

        internal ApplicationThemeManager(
            ResourceDictionary resources,
            ILogger<ApplicationThemeManager> logger)
            : this(() => resources, logger)
        {
        }

        private ApplicationThemeManager(
            Func<ResourceDictionary> resources,
            ILogger<ApplicationThemeManager> logger)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public CommonControlTheme.BaseTheme CurrentTheme { get; private set; }
            = CommonControlTheme.BaseTheme.Light;

        public bool Apply(CommonControlTheme.BaseTheme theme)
        {
            var resources = _resources();
            if (resources != null)
            {
                foreach (var dictionary in resources.MergedDictionaries)
                {
                    if (dictionary is CommonControlTheme controlTheme)
                    {
                        controlTheme.Theme = theme;
                        CurrentTheme = theme;
                        return true;
                    }
                }
            }

            _logger.LogWarning(
                "Application resources do not contain a CommonControlTheme; theme '{Theme}' was not applied.",
                theme);
            return false;
        }
    }
}
```

- [ ] **Step 4: Run the harness and verify the manager tests are GREEN.**

Expected: both application-theme-manager tests pass and the runner ends with `ALL PASS`.

- [ ] **Step 5: Add a failing startup-order contract test.**

Append:

```csharp
Run("NodeCraft restores the theme before loading plugins and resolving UI", () =>
{
    var source = File.ReadAllText(
        FindRepositoryFile("NodeCraft", "App.xaml.cs"));
    var startup = ExtractMethodBody(source, "OnStartup");
    var restoreIndex = startup.IndexOf(
        "themeManager.Apply(themePreferenceStore.Load())",
        StringComparison.Ordinal);
    var pluginIndex = startup.IndexOf(
        "GetRequiredService<PluginLoader>()",
        StringComparison.Ordinal);
    var windowIndex = startup.IndexOf(
        "GetRequiredService<MainWindow>()",
        StringComparison.Ordinal);

    return source.Contains(
            "AddSingleton<ThemePreferenceStore>()",
            StringComparison.Ordinal)
        && source.Contains(
            "AddSingleton<ApplicationThemeManager>()",
            StringComparison.Ordinal)
        && restoreIndex >= 0
        && pluginIndex > restoreIndex
        && windowIndex > restoreIndex;
});
```

- [ ] **Step 6: Run the harness and verify the startup-order test is RED.**

Expected: `FAIL NodeCraft restores the theme before loading plugins and resolving UI` because `App.OnStartup` has no theme services or restore call.

- [ ] **Step 7: Register and invoke the theme services before plugin/UI resolution.**

Add `using NodeCraft.Theming;` to `App.xaml.cs`.

Add these registrations after `services.AddSingleton<IConfiguration>(_configuration);`:

```csharp
services.AddSingleton<ThemePreferenceStore>();
services.AddSingleton<ApplicationThemeManager>();
```

After `AttachUnhandledExceptionHandlers();` and before the existing `PluginLoadReport = Services.GetRequiredService<PluginLoader>().LoadAll(` statement, add:

```csharp
var themePreferenceStore = Services.GetRequiredService<ThemePreferenceStore>();
var themeManager = Services.GetRequiredService<ApplicationThemeManager>();
themeManager.Apply(themePreferenceStore.Load());
```

- [ ] **Step 8: Re-run the harness and verify startup ordering is GREEN.**

Expected: manager and startup-order tests pass, and the complete runner ends with `ALL PASS`.

- [ ] **Step 9: Commit the application startup change.**

```powershell
git add -- NodeCraft/Theming/ApplicationThemeManager.cs NodeCraft/App.xaml.cs NodeCraft.Tests/ThemeTests.cs
git commit -m "feat: restore the theme before creating UI"
```

---

### Task 3: Synchronize and persist the main-window theme menu

**Files:**

- Modify: `NodeCraft/MainWindow.xaml.cs:1-152`
- Modify: `NodeCraft.Tests/Program.cs:896-981`

**Interfaces:**

- Consumes:
  - `ApplicationThemeManager.CurrentTheme`
  - `ApplicationThemeManager.Apply(CommonControlTheme.BaseTheme)`
  - `ThemePreferenceStore.Save(CommonControlTheme.BaseTheme)`
- Produces:
  - `public MainWindow(FlowPage flowPage, ApplicationThemeManager themeManager, ThemePreferenceStore themePreferenceStore)`
  - Startup menu synchronization guarded by `_synchronizingTheme`.
  - Explicit checked/unchecked events apply and save the selected theme.

- [ ] **Step 1: Adapt the existing main-window integration test to require restored dark state, no startup rewrite, and both persisted choices.**

Add `using NodeCraft.Theming;` to `Program.cs`.

Inside `NodeCraft main window exposes the formal menu and theme control`, after adding `theme` to application resources, create the store and manager:

```csharp
var themeDirectory = CreateThemeTestDirectory();
var settingsPath = Path.Combine(themeDirectory, "settings.json");
var storeLogger = new RecordingLogger<ThemePreferenceStore>();
var managerLogger = new RecordingLogger<ApplicationThemeManager>();
var themePreferenceStore = new ThemePreferenceStore(settingsPath, storeLogger);
var themeManager = new ApplicationThemeManager(app.Resources, managerLogger);
var preferenceSeeded = themePreferenceStore.Save(
    CommonControls.WPF.CommonControlTheme.BaseTheme.Dark);
var restored = themeManager.Apply(themePreferenceStore.Load());
```

Replace the existing one-argument window construction with this guarded construction:

```csharp
var warningCountBeforeStartup = storeLogger.Entries.Count(entry =>
    entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
MainWindow window;
using (new FileStream(
    settingsPath,
    FileMode.Open,
    FileAccess.Read,
    FileShare.Read))
{
    window = new MainWindow(
        new FlowPage(NullLoggerFactory.Instance),
        themeManager,
        themePreferenceStore);
}
var startupDidNotRewrite = storeLogger.Entries.Count(entry =>
    entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning)
    == warningCountBeforeStartup;
```

Change the initial menu assertion from rejecting a checked item to requiring restored dark state:

```csharp
|| darkThemeMenuItem == null
|| !darkThemeMenuItem.IsCheckable
|| !darkThemeMenuItem.IsChecked
|| !preferenceSeeded
|| !restored
|| !startupDidNotRewrite
|| theme.Theme != CommonControls.WPF.CommonControlTheme.BaseTheme.Dark
```

Replace the final theme-toggle assertions with:

```csharp
darkThemeMenuItem.IsChecked = false;
var lightApplied = theme.Theme
    == CommonControls.WPF.CommonControlTheme.BaseTheme.Light;
var lightPersisted = new ThemePreferenceStore(
    settingsPath,
    Microsoft.Extensions.Logging.Abstractions.NullLogger<ThemePreferenceStore>.Instance)
    .Load() == CommonControls.WPF.CommonControlTheme.BaseTheme.Light;

darkThemeMenuItem.IsChecked = true;
var darkApplied = theme.Theme
    == CommonControls.WPF.CommonControlTheme.BaseTheme.Dark;
var darkPersisted = new ThemePreferenceStore(
    settingsPath,
    Microsoft.Extensions.Logging.Abstractions.NullLogger<ThemePreferenceStore>.Instance)
    .Load() == CommonControls.WPF.CommonControlTheme.BaseTheme.Dark;

window.Close();

var failingSettingsPath = Path.Combine(themeDirectory, "unwritable-settings");
Directory.CreateDirectory(failingSettingsPath);
var failingStoreLogger = new RecordingLogger<ThemePreferenceStore>();
var failingStore = new ThemePreferenceStore(
    failingSettingsPath,
    failingStoreLogger);
var failureWindow = new MainWindow(
    new FlowPage(NullLoggerFactory.Instance),
    themeManager,
    failingStore);
var failureMenuItem = GetFieldValue<System.Windows.Controls.MenuItem>(
    failureWindow,
    "DarkThemeMenuItem");
if (failureMenuItem == null)
{
    failureWindow.Close();
    return false;
}

failureMenuItem.IsChecked = false;
var failedSaveDidNotUndoTheme = theme.Theme
        == CommonControls.WPF.CommonControlTheme.BaseTheme.Light
    && failingStoreLogger.Entries.Any(entry =>
        entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning
        && entry.Message.Contains(
            "Failed to save theme settings",
            StringComparison.Ordinal));
failureWindow.Close();

return lightApplied
    && lightPersisted
    && darkApplied
    && darkPersisted
    && failedSaveDidNotUndoTheme;
```

Extend the existing `finally` block:

```csharp
finally
{
    File.Delete(operationPath);
    if (Directory.Exists(themeDirectory))
        Directory.Delete(themeDirectory, recursive: true);
    app.Shutdown();
}
```

- [ ] **Step 2: Run the harness and verify RED.**

Expected: build fails with `CS1729` because `MainWindow` does not yet accept `ApplicationThemeManager` and `ThemePreferenceStore`.

- [ ] **Step 3: Inject the theme services and synchronize the menu under a guard.**

Add `using NodeCraft.Theming;` to `MainWindow.xaml.cs`.

Add fields:

```csharp
private readonly ApplicationThemeManager _themeManager;
private readonly ThemePreferenceStore _themePreferenceStore;
private bool _synchronizingTheme;
```

Replace the constructor with:

```csharp
public MainWindow(
    FlowPage flowPage,
    ApplicationThemeManager themeManager,
    ThemePreferenceStore themePreferenceStore)
{
    FlowEditor = flowPage ?? throw new ArgumentNullException(nameof(flowPage));
    _themeManager = themeManager ?? throw new ArgumentNullException(nameof(themeManager));
    _themePreferenceStore = themePreferenceStore
        ?? throw new ArgumentNullException(nameof(themePreferenceStore));

    InitializeComponent();
    _synchronizingTheme = true;
    try
    {
        DarkThemeMenuItem.IsChecked = _themeManager.CurrentTheme
            == CommonControlTheme.BaseTheme.Dark;
    }
    finally
    {
        _synchronizingTheme = false;
    }

    FlowEditor.ExecutionStateChanged += FlowEditor_ExecutionStateChanged;
    RootGrid.Children.Add(FlowEditor);
    Grid.SetRow(FlowEditor, 1);
    UpdateExecutionCommandState();
}
```

- [ ] **Step 4: Route explicit menu changes through the manager and store.**

Replace both handlers and `ChangeTheme` with:

```csharp
private void DarkThemeMenuItem_Checked(object sender, RoutedEventArgs e)
{
    if (!_synchronizingTheme)
        ChangeTheme(CommonControlTheme.BaseTheme.Dark);
}

private void DarkThemeMenuItem_Unchecked(object sender, RoutedEventArgs e)
{
    if (!_synchronizingTheme)
        ChangeTheme(CommonControlTheme.BaseTheme.Light);
}

private void ChangeTheme(CommonControlTheme.BaseTheme theme)
{
    _themeManager.Apply(theme);
    _themePreferenceStore.Save(theme);
}
```

- [ ] **Step 5: Run the harness and verify the complete window flow is GREEN.**

Expected: `PASS NodeCraft main window exposes the formal menu and theme control`, including dark startup, guarded initialization, and light/dark persistence; the runner ends with `ALL PASS`.

- [ ] **Step 6: Commit the main-window integration.**

```powershell
git add -- NodeCraft/MainWindow.xaml.cs NodeCraft.Tests/Program.cs
git commit -m "feat: persist theme menu changes"
```

---

### Task 4: Make the flow grid dynamically follow the subtle theme stroke

**Files:**

- Modify: `NodeCraft.Flow/Flow/FlowCanvas.cs:113-117,163-202`
- Modify: `NodeCraft.Flow/Themes/Flow.xaml:13-38`
- Modify: `NodeCraft.Tests/ThemeTests.cs`

**Interfaces:**

- Consumes: WPF dependency-property metadata and the `colorNeutralStrokeSubtle` dynamic resource.
- Produces:
  - `public static readonly DependencyProperty GridBrushProperty`
  - `public Brush GridBrush { get; set; }` backed by that dependency property.
  - A `FlowCanvas` style setter for `GridBrush`.
- Preserves: caller local-value precedence, the gray fallback, grid thickness, major/minor line rules, zoom behavior, and the existing render loop.

- [ ] **Step 1: Add one integrated failing dependency-property/style/runtime test.**

Add these usings to `ThemeTests.cs`:

```csharp
using NodeCraft.Flow;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
```

Append:

```csharp
Run("FlowCanvas grid brush follows the dynamic subtle stroke resource", () =>
    RunOnSta(() =>
    {
        var metadata = FlowCanvas.GridBrushProperty.GetMetadata(typeof(FlowCanvas))
            as FrameworkPropertyMetadata;
        var root = XDocument.Load(
            FindRepositoryFile("NodeCraft.Flow", "Themes", "Flow.xaml"));
        XNamespace presentation
            = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var style = root.Root?
            .Elements(presentation + "Style")
            .Single(element =>
                (string?)element.Attribute("TargetType")
                    == "{x:Type flow:FlowCanvas}");
        var gridBrushSetter = style?
            .Elements(presentation + "Setter")
            .SingleOrDefault(element =>
                (string?)element.Attribute("Property") == "GridBrush");
        if (metadata?.AffectsRender != true
            || (string?)gridBrushSetter?.Attribute("Value")
                != "{DynamicResource colorNeutralStrokeSubtle}")
        {
            return false;
        }

        var unstyledCanvas = new FlowCanvas();
        if (!ReferenceEquals(unstyledCanvas.GridBrush, Brushes.Gray))
            return false;

        var window = new Window
        {
            Width = 640,
            Height = 480,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };
        var theme = new CommonControlTheme
        {
            Theme = CommonControlTheme.BaseTheme.Light,
        };
        window.Resources.MergedDictionaries.Add(theme);
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/CommonControls.WPF;component/Themes/FluentDesign.Defaults.xaml",
                UriKind.Absolute),
        });
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/NodeCraft.Flow;component/Themes/Flow.xaml",
                UriKind.Absolute),
        });
        var canvas = new FlowCanvas
        {
            Width = 400,
            Height = 300,
        };
        window.Content = canvas;

        try
        {
            window.Show();
            canvas.ApplyTemplate();
            window.UpdateLayout();

            var lightColor = ((SolidColorBrush)canvas.GridBrush).Color;
            var expectedLight = ((SolidColorBrush)canvas.FindResource(
                "colorNeutralStrokeSubtle")).Color;

            theme.Theme = CommonControlTheme.BaseTheme.Dark;
            canvas.Dispatcher.Invoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => { }));
            window.UpdateLayout();

            var darkColor = ((SolidColorBrush)canvas.GridBrush).Color;
            var expectedDark = ((SolidColorBrush)canvas.FindResource(
                "colorNeutralStrokeSubtle")).Color;
            var oldDarkStroke = ((SolidColorBrush)canvas.FindResource(
                "colorNeutralStroke1")).Color;

            var customBrush = new SolidColorBrush(Colors.Magenta);
            canvas.GridBrush = customBrush;
            theme.Theme = CommonControlTheme.BaseTheme.Light;
            canvas.Dispatcher.Invoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => { }));

            return lightColor == expectedLight
                && darkColor == expectedDark
                && darkColor != lightColor
                && darkColor != oldDarkStroke
                && ReferenceEquals(canvas.GridBrush, customBrush);
        }
        finally
        {
            window.Close();
        }
    }));
```

- [ ] **Step 2: Run the harness and verify RED.**

Expected: build fails because `FlowCanvas.GridBrushProperty` does not exist.

- [ ] **Step 3: Convert `GridBrush` to an `AffectsRender` dependency property.**

Replace the CLR auto-property in `FlowCanvas.cs` with:

```csharp
public static readonly DependencyProperty GridBrushProperty
    = DependencyProperty.Register(
        nameof(GridBrush),
        typeof(Brush),
        typeof(FlowCanvas),
        new FrameworkPropertyMetadata(
            Brushes.Gray,
            FrameworkPropertyMetadataOptions.AffectsRender));

public Brush GridBrush
{
    get => (Brush)GetValue(GridBrushProperty);
    set => SetValue(GridBrushProperty, value);
}
```

Delete this one-time assignment from `OnApplyTemplate`:

```csharp
GridBrush = (Brush)FindResource("colorNeutralStroke1");
```

- [ ] **Step 4: Supply the default dynamic brush from the FlowCanvas style.**

Add this setter immediately after the `Background` setter in `Flow.xaml`:

```xml
<Setter Property="GridBrush"
        Value="{DynamicResource colorNeutralStrokeSubtle}" />
```

- [ ] **Step 5: Run the harness and verify dynamic switching is GREEN.**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
```

Expected: `PASS FlowCanvas grid brush follows the dynamic subtle stroke resource` and the runner ends with `ALL PASS`.

- [ ] **Step 6: Run full build and repository checks.**

Run:

```powershell
dotnet build NodeCraft.sln --no-restore
git diff --check
git status --short
```

Expected:

- `dotnet build` exits 0 with no compilation errors.
- `git diff --check` exits 0.
- `git status --short` lists only the Task 4 source/test files before committing.

- [ ] **Step 7: Commit the dynamic-grid change.**

```powershell
git add -- NodeCraft.Flow/Flow/FlowCanvas.cs NodeCraft.Flow/Themes/Flow.xaml NodeCraft.Tests/ThemeTests.cs
git commit -m "fix: lower the dark theme grid contrast"
```

- [ ] **Step 8: Perform post-commit verification.**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
dotnet build NodeCraft.sln --no-restore
git status --short
```

Expected: the test runner ends with `ALL PASS`, the solution build exits 0, and `git status --short` prints no tracked or untracked changes.

---

## Requirement Coverage

- Dark-grid contrast and live theme updates: Task 4.
- Missing/invalid/default-light preference behavior: Task 1.
- Atomic light/dark persistence and failure logging: Task 1.
- Restore before plugin/UI construction: Task 2.
- Menu synchronization without startup writes and explicit toggle persistence: Task 3.
- No hard-coded package colors and local `GridBrush` override preservation: Task 4.
- Complete regression runner and solution build: Task 4, Steps 5-8.
