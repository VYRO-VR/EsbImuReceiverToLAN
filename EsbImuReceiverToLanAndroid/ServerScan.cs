using System.Net;
using System.Net.Sockets;
using System.Text;
using SlimeImuProtocol.SlimeVR;
using static SlimeImuProtocol.SlimeVR.FirmwareConstants;

namespace EsbReceiverToLanAndroid {
    /// <summary>
    /// Finds a SlimeVR server by scanning the local /24 subnet (and localhost):
    /// sends a SlimeVR handshake to every host on port 6969 and waits for the
    /// "Hey OVR =D 5" reply. Works without UDP broadcast and uses its own socket,
    /// so it never disturbs an active streaming session.
    /// </summary>
    internal static class ServerScan {
        private const int SlimePort = 6969;
        private const string HandshakeReply = "Hey OVR =D 5";

        /// <summary>Returns the IP of the first SlimeVR server that answers, or null.</summary>
        public static async Task<string?> FindAsync(TimeSpan timeout, CancellationToken ct = default) {
            string? local = GetLocalIPv4();

            using var udp = new UdpClient();
            try { udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0)); } catch { /* already bound */ }

            var pb = new PacketBuilder("EsbToLanScan");
            byte[] handshake = pb.BuildHandshakePacket(
                BoardType.SLIMEVR, ImuType.UNKNOWN, McuType.UNKNOWN,
                MagnetometerStatus.NOT_SUPPORTED, new byte[] { 0, 0, 0, 0, 0, 1 });

            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _ = Task.Run(async () => {
                try {
                    while (!linked.Token.IsCancellationRequested) {
                        var res = await udp.ReceiveAsync(linked.Token);
                        var text = Encoding.UTF8.GetString(res.Buffer);
                        if (text.Contains(HandshakeReply)) {
                            tcs.TrySetResult(res.RemoteEndPoint.Address.ToString());
                            return;
                        }
                    }
                } catch { /* socket closed / cancelled */ }
            }, linked.Token);

            try {
                await SafeSend(udp, handshake, IPAddress.Loopback, linked.Token);

                if (local != null) {
                    var parts = local.Split('.');
                    if (parts.Length == 4) {
                        string baseAddr = $"{parts[0]}.{parts[1]}.{parts[2]}.";
                        for (int i = 1; i <= 254 && !linked.Token.IsCancellationRequested && !tcs.Task.IsCompleted; i++) {
                            if (IPAddress.TryParse(baseAddr + i, out var addr))
                                await SafeSend(udp, handshake, addr, linked.Token);
                            if ((i & 15) == 0) await Task.Delay(5, linked.Token); // gentle pacing
                        }
                    }
                }

                var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout, linked.Token));
                return done == tcs.Task ? tcs.Task.Result : null;
            } catch {
                return tcs.Task.IsCompletedSuccessfully ? tcs.Task.Result : null;
            } finally {
                linked.Cancel();
            }
        }

        private static async Task SafeSend(UdpClient udp, byte[] data, IPAddress addr, CancellationToken ct) {
            try { await udp.SendAsync(data, new IPEndPoint(addr, SlimePort), ct); } catch { /* unreachable host */ }
        }

        private static string? GetLocalIPv4() {
            try {
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Connect("8.8.8.8", 65530); // picks the outbound interface; sends nothing
                return (s.LocalEndPoint as IPEndPoint)?.Address.ToString();
            } catch {
                return null;
            }
        }
    }
}
