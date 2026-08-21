using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace NodeCraft.BuiltIn.Views
{
    internal static class BuiltInXamlViewLoader
    {
        internal static UserControl LoadAndAttach(UserControl view, string viewName)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            var xamlName = viewName + ".xaml";
            var resourceName = "NodeCraft.BuiltIn.Views." + xamlName;
            using var stream = typeof(BuiltInXamlViewLoader).Assembly
                .GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException(
                    xamlName + " was not embedded into the plugin assembly.");
            }

            using var reader = new StreamReader(stream);
            var root = XamlReader.Parse(reader.ReadToEnd()) as UserControl;
            if (root == null)
            {
                throw new InvalidOperationException(
                    xamlName + " did not produce the expected UserControl root.");
            }

            var parsedContent = root.Content;
            root.Content = null;
            view.Content = parsedContent;
            return root;
        }

        internal static T RequireElement<T>(
            UserControl root,
            string viewName,
            string elementName)
            where T : FrameworkElement
        {
            return root.FindName(elementName) as T
                ?? throw new InvalidOperationException(
                    viewName + " is missing " + elementName + ".");
        }
    }
}
