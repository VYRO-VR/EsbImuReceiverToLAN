using System.Text;
using SlimeImuProtocol.SlimeVR;
using static SlimeImuProtocol.SlimeVR.FirmwareConstants;

namespace EspImuReceiverToLAN {
    /// <summary>
    /// Drives SlimeVR server discovery without needing a tracker connected.
    ///
    /// The relay normally only handshakes once a dongle is plugged in (each
    /// tracker owns a <see cref="UDPHandler"/>). This helper spins up a single
    /// discovery-only <see cref="UDPHandler"/> so the app can find — or test a
    /// connection to — a SlimeVR server before any hardware is attached.
    ///
    /// On a successful handshake <see cref="UDPHandler.OnServerDiscovered"/>
    /// fires (handled elsewhere) and the discovery-only handler disposes itself.
    /// </summary>
    internal sealed class ServerProbe : IDisposable {
        private UDPHandler? _handler;

        /// <summary>
        /// Begin probing <paramref name="target"/> (a specific IP, or
        /// "255.255.255.255" to broadcast across the LAN). Safe to call repeatedly.
        /// </summary>
        public void Start(string target) {
            Stop();
            try {
                // The handler reads the static endpoint, so point it first.
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

        /// <summary>
        /// Probe <paramref name="target"/> and return true if a SlimeVR server
        /// responds before <paramref name="timeout"/> elapses.
        /// </summary>
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
