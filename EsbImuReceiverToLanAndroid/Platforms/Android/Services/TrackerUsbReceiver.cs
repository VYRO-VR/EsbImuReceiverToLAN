using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Android.Runtime;
using EsbReceiverToLanAndroid.Platforms.Android.Services;
using Microsoft.Maui.Storage;
using SlimeImuProtocol.SlimeVR;
using AOS = Android.OS;

namespace EsbReceiverToLanAndroid {
    [BroadcastReceiver(Enabled = true, Exported = true, Name = "com.vyrovr.connect.TrackerUsbReceiver")]
    [IntentFilter(new[] { "android.hardware.usb.action.USB_DEVICE_ATTACHED" })]
    [IntentFilter(new[] { UsbManager.ActionUsbDeviceDetached })]
    [IntentFilter(new[] { ActionLastDeviceDetached })]
    [Preserve(AllMembers = true)]

    public class TrackerUsbReceiver : BroadcastReceiver {
        private const int HID_TRACKER_RECEIVER_VID = 0x1209;
        private const int HID_TRACKER_RECEIVER_PID = 0x7690;
        public const string ActionLastDeviceDetached = "com.vyrovr.connect.ACTION_LAST_DEVICE_DETACHED";

        public static EventHandler OnDeviceConnected;
        public static EventHandler OnDeviceDisconnected;

        public override void OnReceive(Context context, Intent intent) {
            string action = intent.Action;

            if (UsbManager.ActionUsbDeviceAttached.Equals(action)) {
                UsbDevice device = (UsbDevice)intent.GetParcelableExtra(UsbManager.ExtraDevice);
                if (device != null && device.VendorId == HID_TRACKER_RECEIVER_VID && device.ProductId == HID_TRACKER_RECEIVER_PID) {
                    LoadConfigAndStartService(context, device);
                }
            } else if (UsbManager.ActionUsbDeviceDetached.Equals(action)) {
                UsbDevice device = (UsbDevice)intent.GetParcelableExtra(UsbManager.ExtraDevice);
                if (device != null && device.VendorId == HID_TRACKER_RECEIVER_VID && device.ProductId == HID_TRACKER_RECEIVER_PID) {
                    var detachIntent = new Intent(context, typeof(TrackerListenerService));
                    detachIntent.SetPackage(context.PackageName);
                    detachIntent.SetAction("com.vyrovr.connect.ACTION_USB_DEVICE_DETACHED");
                    detachIntent.PutExtra(UsbManager.ExtraDevice, device);
                    context.StartService(detachIntent);
                }
            } else if (ActionLastDeviceDetached.Equals(action)) {
                OnDeviceDisconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        private static void LoadConfigAndStartService(Context context, UsbDevice? device = null)
        {
            var configPath = System.IO.Path.Combine(FileSystem.AppDataDirectory, "config.txt");
            if (System.IO.File.Exists(configPath))
            {
                var savedEndpoint = System.IO.File.ReadAllText(configPath).Trim();
                if (!string.IsNullOrEmpty(savedEndpoint) && System.Net.IPAddress.TryParse(savedEndpoint, out _))
                    UDPHandler.Endpoint = savedEndpoint;
            }

            // Plug-and-play: default to broadcast discovery when no server IP is
            // configured so plugging in the dongle starts streaming immediately.
            if (string.IsNullOrEmpty(UDPHandler.Endpoint) || !System.Net.IPAddress.TryParse(UDPHandler.Endpoint, out _))
                UDPHandler.Endpoint = "255.255.255.255";

            OnDeviceConnected?.Invoke(null, EventArgs.Empty);
            var serviceIntent = new Intent(context, typeof(TrackerListenerService));
            serviceIntent.SetPackage(context.PackageName);
            serviceIntent.SetAction("com.vyrovr.connect.ACTION_USB_DEVICE_ATTACHED");
            if (device != null)
                serviceIntent.PutExtra(UsbManager.ExtraDevice, device);
            try
            {
                if (AOS.Build.VERSION.SdkInt >= AOS.BuildVersionCodes.O)
                    context.StartForegroundService(serviceIntent);
                else
                    context.StartService(serviceIntent);
            }
            catch (Exception ex)
            {
                Platforms.Android.CrashLog.Write("TrackerUsbReceiver.StartService", ex);
                try { context.StartService(serviceIntent); } catch { /* give up quietly */ }
            }
        }
    }
}