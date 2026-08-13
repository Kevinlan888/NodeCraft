using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using NodeCraft.Flow;
using NodeCraft.PluginSample.Nodes;

namespace NodeCraft.PluginSample.Views
{
    public class SampleValueEditor : UserControl
    {
        private readonly FlowCanvas _canvas;
        private readonly SampleValueNodeModel _node;
        private readonly Border _editorCard;
        private bool _isInitializing = true;
        private readonly CheckBox _accentSwitch;
        private readonly TextBox _valueEditor;

        public SampleValueEditor(FlowCanvas canvas, SampleValueNodeModel node)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _node = node ?? throw new ArgumentNullException(nameof(node));

            var root = LoadEditorRoot();
            Content = root.Content;
            _editorCard = root.FindName("EditorCard") as Border
                ?? throw new InvalidOperationException("SampleValueEditor is missing EditorCard.");
            _accentSwitch = root.FindName("AccentSwitch") as CheckBox
                ?? throw new InvalidOperationException("SampleValueEditor is missing AccentSwitch.");
            _valueEditor = root.FindName("ValueEditor") as TextBox
                ?? throw new InvalidOperationException("SampleValueEditor is missing ValueEditor.");

            NameScope.SetNameScope(this, new NameScope());
            RegisterName("EditorCard", _editorCard);
            RegisterName("AccentSwitch", _accentSwitch);
            RegisterName("ValueEditor", _valueEditor);

            _accentSwitch.Checked += AccentSwitch_OnChanged;
            _accentSwitch.Unchecked += AccentSwitch_OnChanged;
            _valueEditor.TextChanged += ValueEditor_OnTextChanged;

            DataContext = _node;
            _accentSwitch.IsChecked = _node.UseAccentStyle;
            _valueEditor.Text = _node.ValueText ?? string.Empty;
            ApplyStyleSelection();
            _isInitializing = false;
        }

        public static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not SampleValueNodeModel sampleValueNode)
            {
                throw new InvalidOperationException("SampleValueEditor requires a SampleValueNodeModel.");
            }

            return new SampleValueEditor(canvas, sampleValueNode);
        }

        private static UserControl LoadEditorRoot()
        {
            var assembly = typeof(SampleValueEditor).Assembly;
            using var stream = assembly.GetManifestResourceStream("NodeCraft.PluginSample.Views.SampleValueEditor.xaml");
            if (stream == null)
            {
                throw new InvalidOperationException("SampleValueEditor.xaml was not embedded into the plugin assembly.");
            }

            using var reader = new StreamReader(stream);
            return XamlReader.Parse(reader.ReadToEnd()) as UserControl
                ?? throw new InvalidOperationException("SampleValueEditor.xaml did not produce a UserControl root.");
        }

        private void ValueEditor_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            _node.ValueText = _valueEditor.Text ?? string.Empty;
            _canvas.NotifyGraphChanged();
        }

        private void AccentSwitch_OnChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            _node.UseAccentStyle = _accentSwitch.IsChecked == true;
            ApplyStyleSelection();
            _canvas.NotifyGraphChanged();
        }

        private void ApplyStyleSelection()
        {
            if (_accentSwitch.IsChecked == true)
            {
                _editorCard.SetResourceReference(Border.BackgroundProperty, "colorBrandBackground2");
                _editorCard.SetResourceReference(Border.BorderBrushProperty, "colorBrandStroke1");
                return;
            }

            _editorCard.SetResourceReference(Border.BackgroundProperty, "colorSubtleBackground");
            _editorCard.SetResourceReference(Border.BorderBrushProperty, "colorNeutralStroke1");
        }
    }
}
