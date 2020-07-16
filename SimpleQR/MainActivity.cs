using System;
using System.Text.RegularExpressions;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Vision;
using Android.Gms.Vision.Barcodes;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.ConstraintLayout.Widget;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Java.Lang;
using Java.Util.Concurrent;
using Plugin.Clipboard;
using SimpleQR.Analyzers;
using SimpleQR.Enums;
using SimpleQR.Models;
using Uri = Android.Net.Uri;

namespace SimpleQR
{
    [Activity(Label = "@string/app_name", Theme = "@style/Theme.AppCompat.Light.NoActionBar",
        MainLauncher = true, Icon = "@mipmap/ic_launcher",
        ConfigurationChanges = ConfigChanges.Locale | ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
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

        private string _wifiPatternStart = @"WIFI:.*";
        private string _wifiPatternEnd = @"((;(S|H|T|P):)|;;$|;$)";
        private Regex WifiSsidPattern => new Regex(@"" + _wifiPatternStart + @"S:(([A-z0-9]|^\S)+)" + _wifiPatternEnd);
        private Regex WifiTypePattern => new Regex(@"" + _wifiPatternStart + @"T:(WPA|WEP)" + _wifiPatternEnd);
        private Regex WifiPasswordPattern => new Regex(@"" + _wifiPatternStart + @"P:(.+?)" + _wifiPatternEnd);
        private Regex WifiHiddenPattern => new Regex(@"" + _wifiPatternStart + @"H:(true|false)" + _wifiPatternEnd);

        private WifiAccessPoint _wifiAccessPoint;

        private string _result;

        private ScanResultType _resultType = ScanResultType.URI;

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
                _result = scanResult.RawValue;
                var resultButtonText = "No Data Found";
                _wifiAccessPoint = ResultToWifiInformation();
                if (_wifiAccessPoint != null)
                {
                    // WIFI Information found
                    _resultType = ScanResultType.WIFI;
                    resultButtonText = $"Copy password for '{_wifiAccessPoint.Ssid}'";
                }
                else if (URLUtil.IsValidUrl(_result))
                {
                    // URL Found
                    _resultType = ScanResultType.URI;
                    var openActionText = Resources.GetText(Resource.String.action_open);
                    resultButtonText = $"{openActionText}: {_result}";
                }
                else if (!string.IsNullOrEmpty(_result))
                {
                    // Plain text found
                    _resultType = ScanResultType.PLAIN_TEXT;
                    var copyActionText = Resources.GetText(Resource.String.action_open);
                    resultButtonText = $"{copyActionText}: {_result}";
                }
                else
                {
                    // No data found
                    _resultType = ScanResultType.UNKNOWN;
                }

                RunOnUiThread(() =>
                {
                    _resultButton.Text = resultButtonText;
                    _resultButtonWrapper.Visibility = ViewStates.Visible;
                });
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
            _torchButton.SetBackgroundResource(_torchIsActive
                ? Resource.Drawable.torch_on
                : Resource.Drawable.torch_off);
            _camera.CameraControl.EnableTorch(_torchIsActive);
        }

        private void HandleResult(object sender, EventArgs eventArgs)
        {
            switch (_resultType)
            {
                case ScanResultType.WIFI:
                    // TODO Offer to connect directly here?
                    CrossClipboard.Current.SetText(_wifiAccessPoint.Password);
                    var wifiToast = Toast.MakeText(this, "Password copied", ToastLength.Short);
                    wifiToast.Show();
                    break;
                case ScanResultType.URI:
                    // Open the result in a browser
                    var uri = Uri.Parse(_result);
                    var intent = new Intent(Intent.ActionView, uri);
                    StartActivity(intent);
                    break;
                case ScanResultType.PLAIN_TEXT:
                    CrossClipboard.Current.SetText(_result);
                    var textToast = Toast.MakeText(this, "Result copied", ToastLength.Short);
                    textToast.Show();
                    break;
                default:
                    // nop
                    break;
            }
        }

        private WifiAccessPoint ResultToWifiInformation()
        {
            var ssidMatch = WifiSsidPattern.Match(_result);
            var typeMatch = WifiTypePattern.Match(_result);
            var passwordMatch = WifiPasswordPattern.Match(_result);
            
            if (ssidMatch.Success
                && typeMatch.Success
                && passwordMatch.Success)
            {
                var hiddenMatch = WifiHiddenPattern.Match(_result);
                var wifiIsHidden = hiddenMatch.Groups[1].ToString().Equals("true");
                return new WifiAccessPoint(
                    ssidMatch.Groups[1].ToString(),
                    passwordMatch.Groups[1].ToString(),
                    typeMatch.Groups[1].ToString(),
                    wifiIsHidden);
            }

            return null;
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