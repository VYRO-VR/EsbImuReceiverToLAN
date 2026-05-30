using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Usb;
using Android.OS;
using EsbReceiverToLanAndroid.Platforms.Android.Services;
using Microsoft.Maui.Storage;
using SlimeImuProtocol.SlimeVR;

namespace EsbReceiverToLanAndroid
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    [IntentFilter(new[] { UsbManager.ActionUsbDeviceAttached })]
    [MetaData(UsbManager.ActionUsbDeviceAttached, Resource = "@xml/device_filter")]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            HandleUsbDeviceIntent(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            if (intent != null)
                HandleUsbDeviceIntent(intent);
        }

        private void HandleUsbDeviceIntent(Intent? intent)
        {
            if (intent?.Action != UsbManager.ActionUsbDeviceAttached)
                return;

            var device = intent.GetParcelableExtra(UsbManager.ExtraDevice) as UsbDevice;
            if (device != null)
            {
                LoadConfigAndStartService(device);
            }
        }

        private void LoadConfigAndStartService(UsbDevice? device = null)
        {
            var savedEndpoint = string.Empty;
            var configPath = Path.Combine(FileSystem.AppDataDirectory, "config.txt");
            if (File.Exists(configPath))
            {
                savedEndpoint = File.ReadAllText(configPath).Trim();
                if (!string.IsNullOrEmpty(savedEndpoint) && System.Net.IPAddress.TryParse(savedEndpoint, out _))
                    UDPHandler.Endpoint = savedEndpoint;
            }

            // Plug-and-play: with no saved server IP yet, fall back to broadcast
            // discovery so the receiver "just works" when plugged in without the
            // user having opened the app and typed an address first.
            if (string.IsNullOrEmpty(UDPHandler.Endpoint) || !System.Net.IPAddress.TryParse(UDPHandler.Endpoint, out _))
                UDPHandler.Endpoint = "255.255.255.255";

            TrackerUsbReceiver.OnDeviceConnected?.Invoke(this, EventArgs.Empty);
            var serviceIntent = new Intent(this, typeof(TrackerListenerService));
            serviceIntent.SetPackage(PackageName);
            serviceIntent.SetAction("com.vyrovr.connect.ACTION_USB_DEVICE_ATTACHED");
            if (device != null)
                serviceIntent.PutExtra(UsbManager.ExtraDevice, device);
            StartTrackerService(serviceIntent);
        }

        private void StartTrackerService(Intent serviceIntent)
        {
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                    StartForegroundService(serviceIntent);
                else
                    StartService(serviceIntent);
            }
            catch (Exception ex)
            {
                // Starting a foreground service can be disallowed depending on
                // app state on newer Android. Log and degrade gracefully rather
                // than crashing the launch triggered by plugging in the dongle.
                Platforms.Android.CrashLog.Write("StartTrackerService", ex);
                try { StartService(serviceIntent); } catch { /* give up quietly */ }
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            StopService(new Intent(this, typeof(TrackerListenerService)));
        }
    }
}
