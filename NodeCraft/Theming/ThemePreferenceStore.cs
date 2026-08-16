using CommonControls.WPF;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;

namespace NodeCraft.Theming
{
    public sealed class ThemePreferenceStore
    {
        private static readonly JsonSerializerOptions SerializerOptions
            = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            };

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
            try
            {
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
            catch (FileNotFoundException)
            {
                return CommonControlTheme.BaseTheme.Light;
            }
            catch (DirectoryNotFoundException)
            {
                return CommonControlTheme.BaseTheme.Light;
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

        internal static string GetDefaultSettingsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NodeCraft",
                "settings.json");
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
    }
}
