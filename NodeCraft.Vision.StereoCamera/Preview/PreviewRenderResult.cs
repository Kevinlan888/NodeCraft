using System;
using System.Windows.Media.Imaging;

namespace NodeCraft.Vision.StereoCamera.Preview
{
    internal sealed class PreviewRenderResult
    {
        internal PreviewRenderResult(BitmapSource bitmap, string statusText)
        {
            Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
            StatusText = statusText ?? string.Empty;
        }

        internal BitmapSource Bitmap { get; }

        internal string StatusText { get; }
    }
}
