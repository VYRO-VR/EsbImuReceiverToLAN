using Android.Content;
using Android.Runtime;
using Android.Util;

namespace EsbReceiverToLanAndroid.Platforms.Android;

/// <summary>
/// Lightweight crash/diagnostic logger. Captures otherwise-fatal unhandled
/// exceptions so a hard crash on a headset (where logcat is awkward to reach)
/// can still be diagnosed after the fact.
///
/// Entries are written to the app's external files directory
/// (/sdcard/Android/data/&lt;package&gt;/files/crash.log) which can be pulled
/// over USB/MTP or `adb pull` without root, and mirrored to logcat under the
/// "EsbCrash" tag.
/// </summary>
public static class CrashLog
{
    private const string Tag = "EsbCrash";
    private const string FileName = "crash.log";
    private static Context? _context;
    private static bool _installed;
    private static readonly object _gate = new();

    public static void Install(Context context)
    {
        if (_installed) return;
        _installed = true;
        _context = context.ApplicationContext ?? context;

        // Managed exceptions surfaced by the Android runtime (the usual source
        // of "app closed instantly" with no visible error).
        AndroidEnvironment.UnhandledExceptionRaiser += (sender, e) =>
            Write("AndroidEnvironment", e.Exception);

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            Write("AppDomain", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            Write("UnobservedTask", e.Exception);
            e.SetObserved();
        };
    }

    public static void Write(string source, Exception? ex)
    {
        var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({source})\n{ex}\n\n";
        try
        {
            Log.Error(Tag, message);
        }
        catch { /* logging must never throw */ }

        // Surface errors in the in-app Activity log too, so they're visible on a
        // headset without pulling the crash file.
        try { DiagnosticsLog.Write($"Error ({source}): {ex?.Message}"); }
        catch { /* logging must never throw */ }

        try
        {
            lock (_gate)
            {
                var path = LogPath;
                if (path != null)
                    File.AppendAllText(path, message);
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>Returns the most recent crash log contents, or null if none.</summary>
    public static string? ReadLast()
    {
        try
        {
            var path = LogPath;
            if (path != null && File.Exists(path))
                return File.ReadAllText(path);
        }
        catch { /* ignore */ }
        return null;
    }

    public static void Clear()
    {
        try
        {
            var path = LogPath;
            if (path != null && File.Exists(path))
                File.Delete(path);
        }
        catch { /* ignore */ }
    }

    private static string? LogPath
    {
        get
        {
            var dir = _context?.GetExternalFilesDir(null)?.AbsolutePath;
            return dir == null ? null : Path.Combine(dir, FileName);
        }
    }
}
