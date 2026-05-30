using System.Text;
using SlimeImuProtocol.SlimeVR;
using static SlimeImuProtocol.SlimeVR.FirmwareConstants;

namespace EsbReceiverToLanAndroid {
    /// <summary>
    /// Drives SlimeVR server discovery without a tracker connected. The relay
    /// normally only handshakes once a dongle is plugged in (each tracker owns a
    /// <see cref="UDPHandler"/>); this spins up a single discovery-only handler
    /// so the app can find — or test — a server before any hardware is attached.
    /// </summary>
    internal sealed class ServerProbe : IDisposable {
        private UDPHandler? _handler;

        /// <summary>Probe <paramref name="target"/> (a specific IP, or "255.255.255.255" to broadcast).</summary>
        public void Start(string target) {
            Stop();
            try {
                UDPHandler.Endpoint = target;
                _handler = new UDPHandler(
                    "EsbToLanProbe",
                    Encoding.UTF8.GetBytes("EsbToLanProbe"),
                    BoardType.SLIMEVR,
                    ImuType.UNKNOWN,
                    McuType.UNKNOWN,
                    MagnetometerStatus.NOT_SUPPORTED,
                    1) {
                    IsDiscoveryOnly = true
                };
            } catch {
                _handler = null;
            }
        }

        public void Stop() {
            try { _handler?.Dispose(); } catch { /* best effort */ }
            _handler = null;
        }

        /// <summary>Returns true if a SlimeVR server responds to <paramref name="target"/> within <paramref name="timeout"/>.</summary>
        public static async Task<bool> TestAsync(string target, TimeSpan timeout) {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<string> handler = (_, _) => tcs.TrySetResult(true);
            UDPHandler.OnServerDiscovered += handler;
            using var probe = new ServerProbe();
            try {
                probe.Start(target);
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
                return completed == tcs.Task && tcs.Task.Result;
            } finally {
                UDPHandler.OnServerDiscovered -= handler;
            }
        }

        public void Dispose() => Stop();
    }
}
