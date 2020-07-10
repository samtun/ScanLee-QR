using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Tasks;
using Android.Gms.Vision;
using Android.Gms.Vision.Barcodes;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.ConstraintLayout.Widget;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using CameraXTestApp.Analyzers;
using Java.Lang;
using Java.Util.Concurrent;
using Uri = Android.Net.Uri;

namespace XQR
{
    [Activity(Label = "@string/app_name", Theme = "@style/Theme.AppCompat.Light.NoActionBar", MainLauncher = true, Icon = "@mipmap/ic_launcher")]
    public class MainActivity : AppCompatActivity
    {
        // Camera elements
        private Preview _preview;
        private ImageAnalysis _imageAnalyzer;
        private ICamera _camera;
        private IExecutorService _cameraExecutor;
        private BarcodeDetector _barcodeDetector;

        // Camera configuration
        private bool _torchIsActive;
        private bool _cameraHasFlashUnit;

        // View elements
        private PreviewView _previewView;
        private Button _torchButton;
        private Button _resultButton;
        private Button _cancelResultButton;
        private ConstraintLayout _resultButtonWrapper;

        private const int RequestCodePermissions = 10;
        private const string RequiredPermission = Android.Manifest.Permission.Camera;

        private string _result;
        private string Result
        {
            get => _result;
            set
            {
                if (value == null)
                {
                    return;
                }
                _result = value;
                RunOnUiThread(() =>
                {
                    _resultButton.Text = _result;
                    _resultButtonWrapper.Visibility = ViewStates.Visible;
                });
            }
        }

        private CancellationToken _resultCancellationToken = new CancellationTokenSource().Token;
        
        private bool AllPermissionsGranted =>
            ContextCompat.CheckSelfPermission(BaseContext, RequiredPermission) == Permission.Granted;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);

            _previewView = FindViewById<PreviewView>(Resource.Id.preview_view);
            _torchButton = FindViewById<Button>(Resource.Id.torch_toggle_button);
            _resultButtonWrapper = FindViewById<ConstraintLayout>(Resource.Id.result_button_wrapper);
            _resultButton = FindViewById<Button>(Resource.Id.result_button);
            _cancelResultButton = FindViewById<Button>(Resource.Id.cancel_result_button);
            
            _resultButton.Click += HandleResult;
            _cancelResultButton.Click += HideResultWrapper;
            _torchButton.Click += ToggleTorchMode;

            _barcodeDetector = new BarcodeDetector.Builder(this).SetBarcodeFormats(BarcodeFormat.QrCode).Build();

            // Request camera permissions
            if (AllPermissionsGranted)
            {
                StartCamera();
            }
            else
            {
                ActivityCompat.RequestPermissions(
                    this, new string[] {RequiredPermission}, RequestCodePermissions);
            }

            _cameraExecutor = Executors.NewSingleThreadExecutor();
        }

        /// <summary>
        /// Initializes the camera.
        /// </summary>
        /// <exception cref="System.Exception">Exception that may occur during camera initialization</exception>
        private void StartCamera()
        {
            var cameraProviderFuture = ProcessCameraProvider.GetInstance(this);

            cameraProviderFuture.AddListener(new Runnable(() =>
            {
                // Used to bind the lifecycle of cameras to the lifecycle owner
                var cameraProvider = (ProcessCameraProvider) cameraProviderFuture.Get();

                try
                {
                    // Unbind use cases before rebinding
                    cameraProvider.UnbindAll();

                    // Preview
                    _preview = new Preview.Builder().Build();
                    _imageAnalyzer = new ImageAnalysis.Builder().Build();
                    _imageAnalyzer.SetAnalyzer(_cameraExecutor, new ImageAnalyzer(AnalyzeFrame));
                    _preview?.SetSurfaceProvider(_previewView.PreviewSurfaceProvider);

                    // ScannerMode
                    var cameraSelector = new CameraSelector.Builder().RequireLensFacing(CameraSelector.LensFacingBack)
                                                                     .Build();
                    _camera = cameraProvider.BindToLifecycle(this,
                                                             cameraSelector,
                                                             _preview,
                                                             _imageAnalyzer);
                    _cameraHasFlashUnit = _camera.CameraInfo.HasFlashUnit;
                }
                catch (System.Exception e)
                {
                    throw e;
                }
            }), ContextCompat.GetMainExecutor(this));
        }

        /// <summary>
        /// Analyzes the given frame with a BarcodeDetector and navigates forward if data was found.
        /// </summary>
        /// <param name="frame">The Frame representation of the current image</param>
        private void AnalyzeFrame(Frame frame)
        {
            var barcodeResults = _barcodeDetector.Detect(frame);
            if (barcodeResults.Size() > 0)
            {
                var scanResult = (Barcode) barcodeResults.ValueAt(0);
                Result = scanResult.RawValue;
            }

            barcodeResults.Clear();
        }

        /// <summary>
        /// Toggles the torch. This is only available if the scannerMode is active
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void ToggleTorchMode(object sender, EventArgs eventArgs)
        {
            if (!_cameraHasFlashUnit)
            {
                return;
            }

            _torchIsActive = !_torchIsActive;
            _torchButton.Text = _torchIsActive ? "Torch On" : "Torch Off";
            _camera.CameraControl.EnableTorch(_torchIsActive);
        }

        private void HandleResult(object sender, EventArgs eventArgs)
        {
            if (_result == null)
            {
                return;
            }
            
            // Open the result in the browser
            var uri = Uri.Parse(_result);
            var intent = new Intent(Intent.ActionView, uri);
            StartActivity (intent);
        }

        private void HideResultWrapper(object sender, EventArgs eventArgs)
        {
            _resultButtonWrapper.Visibility = ViewStates.Gone;
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
                                                        Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (grantResults.Length <= 0
                || grantResults[0] != (int) Permission.Granted
                || requestCode != RequestCodePermissions)
            {
                return;
            }
            
            if (AllPermissionsGranted)
            {
                StartCamera();
            }
            else
            {
                Finish();
            }
        }
    }
}