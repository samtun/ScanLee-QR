using System;
using Android.Gms.Vision;
using Android.Graphics;
using AndroidX.Camera.Core;
using Java.Nio;

namespace CameraXTestApp.Analyzers
{
    /// <summary>
    /// Provides an implementation of ImageAnalysis.IAnalyzer and calls the given Action with a Frame from
    /// the Google.Vision Package. The Action is then expected to analyze the Frame with a BarcodeDetector.
    /// </summary>
    public class ImageAnalyzer : Java.Lang.Object, ImageAnalysis.IAnalyzer
    {
        private readonly Action<Frame> _analyze;

        public ImageAnalyzer(Action<Frame> analyze)
        {
            _analyze = analyze;
        }

        public void Analyze(IImageProxy imageProxy)
        {
            var buffer = imageProxy.Image.GetPlanes()[0].Buffer;
            buffer.Rewind();
            var imageWidth = imageProxy.Image.Width;
            var imageHeight = imageProxy.Image.Height;
            var frame = new Frame.Builder().SetImageData(buffer, imageWidth, imageHeight, (int)ImageFormatType.Yv12).Build();
            _analyze(frame);
            imageProxy.Close();
        }
    }
}