using System;

namespace NodeCraft.Localization
{
    public class LanguageChangedEventArgs : EventArgs
    {
        public SupportedLanguage OldLanguage { get; }
        public SupportedLanguage NewLanguage { get; }

        public LanguageChangedEventArgs(SupportedLanguage oldLanguage, SupportedLanguage newLanguage)
        {
            OldLanguage = oldLanguage;
            NewLanguage = newLanguage;
        }
    }
}
