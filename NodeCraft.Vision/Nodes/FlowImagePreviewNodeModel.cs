using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using NodeCraft.Flow;

namespace NodeCraft.Vision.Nodes
{
    public sealed class FlowImagePreviewNodeModel : NodeModel, INotifyPropertyChanged
    {
        public const string FlowNodeTypeKey = "nodecraft.vision.image-preview";

        private FlowImage _currentImage;
        private string _statusText = string.Empty;
        private BitmapSource _bitmapSource;

        public FlowImagePreviewNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Image Preview (FlowImage)";
            InputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = "image",
                    Parameter = new Parameter { ParameterType = FlowDataType.Image.Key },
                    PortDirection = EPortDirection.None,
                },
            };
            OutputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = "image",
                    Parameter = new Parameter { ParameterType = FlowDataType.Image.Key },
                    PortDirection = EPortDirection.None,
                },
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FlowImage CurrentImage => _currentImage;

        public string StatusText => _statusText;

        public BitmapSource BitmapSource => _bitmapSource;

        internal void SetCurrentImage(FlowImage image)
        {
            if (!ReferenceEquals(_currentImage, image))
            {
                _currentImage = image;
                OnPropertyChanged(nameof(CurrentImage));
            }
        }

        internal void SetStatusText(string statusText)
        {
            statusText ??= string.Empty;
            if (string.Equals(_statusText, statusText, StringComparison.Ordinal))
            {
                return;
            }

            _statusText = statusText;
            OnPropertyChanged(nameof(StatusText));
        }

        internal void SetBitmapSource(BitmapSource bitmapSource)
        {
            if (ReferenceEquals(_bitmapSource, bitmapSource))
            {
                return;
            }

            _bitmapSource = bitmapSource;
            OnPropertyChanged(nameof(BitmapSource));
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
