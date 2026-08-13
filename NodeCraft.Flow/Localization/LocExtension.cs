using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace NodeCraft.Localization
{
    [ContentProperty("Key")]
    public class LocExtension : MarkupExtension
    {
        public string Key { get; set; }

        public LocExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var target = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;

            if (target?.TargetProperty is DependencyProperty
                && target.TargetObject is DependencyObject)
            {
                var provider = new LocalizationProvider(Key);
                var binding = new Binding(nameof(LocalizationProvider.Value))
                {
                    Source = provider,
                    Mode = BindingMode.OneWay
                };
                return binding.ProvideValue(serviceProvider);
            }

            // Design-time or non-DP target: return current value directly
            return LanguageManager.GetString(Key);
        }
    }
}
