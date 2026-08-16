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
