using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace NodeCraft.Localization
{
    internal class LocalizationProvider : INotifyPropertyChanged, IDisposable
    {
        private static readonly object ProvidersLock = new object();
        private static readonly List<WeakReference<LocalizationProvider>> Providers = new List<WeakReference<LocalizationProvider>>();
        private readonly string _key;
        private bool _disposed;

        static LocalizationProvider()
        {
            LanguageManager.LanguageChanged += OnLanguageChanged;
        }

        public string Value => LanguageManager.GetString(_key);

        public LocalizationProvider(string key)
        {
            _key = key;
            lock (ProvidersLock)
            {
                Providers.Add(new WeakReference<LocalizationProvider>(this));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private static void OnLanguageChanged(object sender, LanguageChangedEventArgs e)
        {
            var liveProviders = new List<LocalizationProvider>();

            lock (ProvidersLock)
            {
                for (var index = Providers.Count - 1; index >= 0; index--)
                {
                    if (Providers[index].TryGetTarget(out var provider))
                    {
                        liveProviders.Add(provider);
                    }
                    else
                    {
                        Providers.RemoveAt(index);
                    }
                }
            }

            foreach (var provider in liveProviders)
            {
                provider.NotifyLanguageChanged();
            }
        }

        private void NotifyLanguageChanged()
        {
            if (!_disposed)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (ProvidersLock)
            {
                for (var index = Providers.Count - 1; index >= 0; index--)
                {
                    if (!Providers[index].TryGetTarget(out var provider)
                        || ReferenceEquals(provider, this))
                    {
                        Providers.RemoveAt(index);
                    }
                }
            }
        }
    }
}
