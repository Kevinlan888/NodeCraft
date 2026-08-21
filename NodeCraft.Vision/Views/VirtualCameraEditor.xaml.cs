using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.Flow;
using NodeCraft.Vision.Nodes;

namespace NodeCraft.Vision.Views
{
    internal sealed partial class VirtualCameraEditor : UserControl
    {
        private readonly FlowCanvas _canvas;
        private readonly VirtualCameraNodeModel _node;
        private readonly TextBox _sourcePathEditor;
        private readonly ComboBox _loadModeEditor;
        private readonly TextBox _frameRateEditor;
        private readonly TextBox _maxPreloadedImagesEditor;
        private readonly TextBox _maxPreloadedBytesEditor;
        private readonly CheckBox _skipErrorImagesEditor;
        private bool _initializing = true;

        private VirtualCameraEditor(FlowCanvas canvas, VirtualCameraNodeModel node)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _node = node ?? throw new ArgumentNullException(nameof(node));

            InitializeComponent();
            _sourcePathEditor = SourcePathEditor;
            _loadModeEditor = LoadModeEditor;
            _frameRateEditor = FrameRateEditor;
            _maxPreloadedImagesEditor = MaxPreloadedImagesEditor;
            _maxPreloadedBytesEditor = MaxPreloadedBytesEditor;
            _skipErrorImagesEditor = SkipErrorImagesEditor;

            _loadModeEditor.ItemsSource = Enum.GetValues(typeof(VirtualCameraLoadMode));
            _sourcePathEditor.TextChanged += SourcePathEditor_TextChanged;
            _loadModeEditor.SelectionChanged += LoadModeEditor_SelectionChanged;
            _frameRateEditor.TextChanged += FrameRateEditor_TextChanged;
            _maxPreloadedImagesEditor.TextChanged += MaxPreloadedImagesEditor_TextChanged;
            _maxPreloadedBytesEditor.TextChanged += MaxPreloadedBytesEditor_TextChanged;
            _skipErrorImagesEditor.Checked += SkipErrorImagesEditor_Changed;
            _skipErrorImagesEditor.Unchecked += SkipErrorImagesEditor_Changed;

            _sourcePathEditor.Text = _node.SourcePath ?? string.Empty;
            _loadModeEditor.SelectedItem = _node.LoadMode;
            _frameRateEditor.Text = _node.FrameRate.ToString(
                "G17",
                CultureInfo.InvariantCulture);
            _maxPreloadedImagesEditor.Text = _node.MaxPreloadedImages.ToString(
                CultureInfo.InvariantCulture);
            _maxPreloadedBytesEditor.Text = (_node.MaxPreloadedBytes
                / VirtualCameraNodeModel.BytesPerMegabyte)
                .ToString(CultureInfo.InvariantCulture);
            _skipErrorImagesEditor.IsChecked = _node.SkipErrorImages;
            _initializing = false;
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (!(node is VirtualCameraNodeModel virtualCameraNode))
            {
                throw new InvalidOperationException(
                    "VirtualCameraEditor requires a VirtualCameraNodeModel.");
            }

            return new VirtualCameraEditor(canvas, virtualCameraNode);
        }

        private void SourcePathEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_initializing)
            {
                return;
            }

            _node.SourcePath = _sourcePathEditor.Text ?? string.Empty;
            NotifyChanged();
        }

        private void LoadModeEditor_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_initializing
                || !(_loadModeEditor.SelectedItem is VirtualCameraLoadMode mode))
            {
                return;
            }

            _node.LoadMode = mode;
            NotifyChanged();
        }

        private void FrameRateEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_initializing
                || !double.TryParse(
                    _frameRateEditor.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                || !VirtualCameraNodeModel.IsValidFrameRate(value))
            {
                return;
            }

            _node.FrameRate = value;
            NotifyChanged();
        }

        private void MaxPreloadedImagesEditor_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (_initializing
                || !int.TryParse(
                    _maxPreloadedImagesEditor.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                return;
            }

            _node.MaxPreloadedImages = value;
            NotifyChanged();
        }

        private void MaxPreloadedBytesEditor_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (_initializing
                || !long.TryParse(
                    _maxPreloadedBytesEditor.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var megabytes)
                || megabytes <= 0)
            {
                return;
            }

            long bytes;
            try
            {
                bytes = checked(megabytes * VirtualCameraNodeModel.BytesPerMegabyte);
            }
            catch (OverflowException)
            {
                return;
            }

            _node.MaxPreloadedBytes = bytes;
            NotifyChanged();
        }

        private void SkipErrorImagesEditor_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
            {
                return;
            }

            _node.SkipErrorImages = _skipErrorImagesEditor.IsChecked == true;
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            _canvas.NotifyGraphChanged(refreshNodeContents: false);
        }
    }
}
