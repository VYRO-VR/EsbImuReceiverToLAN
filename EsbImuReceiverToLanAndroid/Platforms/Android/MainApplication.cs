using Android.App;
using Android.Runtime;
using EsbReceiverToLanAndroid;
using EsbReceiverToLanAndroid.Platforms.Android;

namespace EsbReceiverToLanAndroid
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        public override void OnCreate()
        {
            // Install the crash logger before anything else so a startup crash
            // is captured rather than silently closing the app.
            CrashLog.Install(this);
            base.OnCreate();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
