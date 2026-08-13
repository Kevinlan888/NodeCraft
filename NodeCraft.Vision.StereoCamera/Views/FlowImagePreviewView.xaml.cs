using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.Flow;
using NodeCraft.Vision.StereoCamera.Nodes;
using NodeCraft.Vision.StereoCamera.Preview;

namespace NodeCraft.Vision.StereoCamera.Views
{
    public sealed class FlowImagePreviewView : UserControl
    {
        private readonly FlowImagePreviewNodeModel _node;
        private readonly LatestPreviewRenderQueue _renderQueue;
        private readonly Image _previewImage;
        private readonly TextBlock _frameText;
        private readonly TextBlock _statusText;
        private bool _unloaded;

        private FlowImagePreviewView(FlowImagePreviewNodeModel node)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            var root = LoadViewRoot();
            Content = root.Content;
            _previewImage = root.FindName("PreviewImage") as Image
                ?? throw new InvalidOperationException("FlowImagePreviewView is missing PreviewImage.");
            _frameText = root.FindName("FrameText") as TextBlock
                ?? throw new InvalidOperationException("FlowImagePreviewView is missing FrameText.");
            _statusText = root.FindName("StatusText") as TextBlock
                ?? throw new InvalidOperationException("FlowImagePreviewView is missing StatusText.");
            _renderQueue = new LatestPreviewRenderQueue(
                FlowImageBitmapConverter.Convert,
                ApplyRenderResultAsync);
            _node.PropertyChanged += Node_PropertyChanged;
            Unloaded += FlowImagePreviewView_Unloaded;
            DataContext = _node;
            UpdateFrameText();
            if (_node.CurrentImage != null)
            {
                _renderQueue.Submit(_node.CurrentImage);
            }
        }

        private static UserControl LoadViewRoot()
        {
            var assembly = typeof(FlowImagePreviewView).Assembly;
            using var stream = assembly.GetManifestResourceStream(
                "NodeCraft.Vision.StereoCamera.Views.FlowImagePreviewView.xaml");
            if (stream == null)
            {
                throw new InvalidOperationException("FlowImagePreviewView.xaml was not embedded into the plugin assembly.");
            }

            using var reader = new StreamReader(stream);
            return System.Windows.Markup.XamlReader.Parse(reader.ReadToEnd()) as UserControl
                ?? throw new InvalidOperationException("FlowImagePreviewView.xaml did not produce a UserControl root.");
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (!(node is FlowImagePreviewNodeModel previewNode))
            {
                throw new InvalidOperationException("FlowImagePreviewView requires a FlowImagePreviewNodeModel.");
            }

            return new FlowImagePreviewView(previewNode);
        }

        private void Node_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FlowImagePreviewNodeModel.CurrentImage))
            {
                UpdateFrameText();
                if (!_unloaded && _node.CurrentImage != null)
                {
                    _renderQueue.Submit(_node.CurrentImage);
                }
            }
            else if (e.PropertyName == nameof(FlowImagePreviewNodeModel.StatusText))
            {
                _statusText.Text = _node.StatusText;
            }
        }

        private async Task ApplyRenderResultAsync(long version, PreviewRenderResult result)
        {
            if (_unloaded)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (_unloaded)
                {
                    return;
                }

                _node.SetBitmapSource(result.Bitmap);
                _node.SetStatusText(result.StatusText);
                _previewImage.Source = result.Bitmap;
                _statusText.Text = result.StatusText;
                UpdateFrameText();
            }).Task.ConfigureAwait(false);
        }

        private void UpdateFrameText()
        {
            var image = _node.CurrentImage;
            _frameText.Text = image == null
                ? "No image"
                : $"Frame {image.FrameId} · {image.Width}x{image.Height} · {image.PixelFormat}";
            _statusText.Text = _node.StatusText;
        }

        private void FlowImagePreviewView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_unloaded)
            {
                return;
            }

            _unloaded = true;
            _node.PropertyChanged -= Node_PropertyChanged;
            _renderQueue.Dispose();
        }
    }
}
