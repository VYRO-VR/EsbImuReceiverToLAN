using System.Text;

namespace EsbReceiverToLanAndroid;

/// <summary>
/// Small in-memory, thread-safe ring buffer of human-readable status lines shown
/// in the app's on-screen "Activity log". It lets the UI surface what the
/// receiver service, USB layer and server discovery are doing without needing
/// <c>adb</c>/logcat — which is awkward to reach on a headset. Unlike
/// <see cref="Platforms.Android.CrashLog"/> (which persists fatal crashes to a
/// file), this is a lightweight live feed for normal runtime activity.
/// </summary>
public static class DiagnosticsLog
{
    private const int MaxLines = 200;
    private static readonly object _gate = new();
    private static readonly LinkedList<string> _lines = new();

    /// <summary>Raised whenever a line is added or the log is cleared, so the UI can refresh.</summary>
    public static event Action? Changed;

    /// <summary>Append a timestamped line to the log.</summary>
    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        lock (_gate)
        {
            _lines.AddLast(line);
            while (_lines.Count > MaxLines)
                _lines.RemoveFirst();
        }
        try { Changed?.Invoke(); } catch { /* no UI attached yet */ }
    }

    /// <summary>Oldest-first text snapshot of the whole log.</summary>
    public static string Snapshot()
    {
        lock (_gate)
        {
            if (_lines.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var l in _lines) sb.AppendLine(l);
            return sb.ToString();
        }
    }

    public static void Clear()
    {
        lock (_gate) _lines.Clear();
        try { Changed?.Invoke(); } catch { /* no UI attached yet */ }
    }
}
