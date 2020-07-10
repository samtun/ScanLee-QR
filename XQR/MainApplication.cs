using System;
using Android.App;
using Android.Runtime;
using AndroidX.Camera.Camera2;
using AndroidX.Camera.Core;

namespace CameraXTestApp
{
    [Application]
    public class MainApplication : Application, CameraXConfig.IProvider
    {
        public CameraXConfig CameraXConfig => Camera2Config.DefaultConfig();

        public MainApplication(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }
    }
}