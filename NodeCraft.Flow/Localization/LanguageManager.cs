using System;
using System.Globalization;

namespace NodeCraft.Localization
{
    public static class LanguageManager
    {
        private static SupportedLanguage _language;

        public static SupportedLanguage Language
        {
            get => _language;
            set
            {
                if (_language == value) return;

                var old = _language;
                _language = value;

                // Sync CultureInfo so .resx ResourceManager also picks up the change
                var culture = ToCultureInfo(value);
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;

                LanguageChanged?.Invoke(null, new LanguageChangedEventArgs(old, value));
            }
        }

        public static event EventHandler<LanguageChangedEventArgs> LanguageChanged;

        static LanguageManager()
        {
            _language = FromCultureInfo(CultureInfo.CurrentUICulture);
        }

        public static string GetString(string key)
        {
            var rm = Properties.Strings.ResourceManager;
            return rm.GetString(key, ToCultureInfo(_language)) ?? key;
        }

        public static CultureInfo ToCultureInfo(SupportedLanguage lang)
        {
            return lang switch
            {
                SupportedLanguage.zh_CN => new CultureInfo("zh-CN"),
                SupportedLanguage.en_US => new CultureInfo("en-US"),
                SupportedLanguage.ko_KR => new CultureInfo("ko-KR"),
                _ => new CultureInfo("zh-CN")
            };
        }

        public static SupportedLanguage FromCultureInfo(CultureInfo culture)
        {
            if (culture == null) return SupportedLanguage.zh_CN;

            var name = culture.Name;
            if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return SupportedLanguage.zh_CN;
            if (name.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return SupportedLanguage.en_US;
            if (name.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return SupportedLanguage.ko_KR;

            return SupportedLanguage.zh_CN;
        }
    }
}
