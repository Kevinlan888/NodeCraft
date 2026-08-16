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
