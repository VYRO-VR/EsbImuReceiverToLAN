using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SlimeImuProtocol.SlimeVR;
using static SlimeImuProtocol.SlimeVR.FirmwareConstants;

namespace EspImuReceiverToLAN {
    /// <summary>A SlimeVR server found on the network, with a friendly name when resolvable.</summary>
    internal sealed record DiscoveredServer(string Ip, string Name) {
        public string Display => Name == Ip ? Ip : $"{Name} ({Ip})";
    }

    /// <summary>
    /// Finds SlimeVR server(s) by scanning the local /24 subnet (and localhost):
    /// it sends a SlimeVR handshake to every host on port 6969 and collects the
    /// ones that reply "Hey OVR =D 5". Works without UDP broadcast and uses its
    /// own socket, so it never disturbs an active streaming session.
    /// </summary>
    internal static class ServerScan {
        private const int SlimePort = 6969;
        private const string HandshakeReply = "Hey OVR =D 5";

        /// <summary>Returns the IP of the first SlimeVR server that answers, or null.</summary>
        public static async Task<string?> FindAsync(TimeSpan timeout, CancellationToken ct = default) {
            var all = await FindAllAsync(timeout, stopAtFirst: true, ct);
            return all.Count > 0 ? all[0].Ip : null;
        }

        /// <summary>
        /// Returns every SlimeVR server that answers within <paramref name="timeout"/>,
        /// each with a reverse-DNS hostname when available (so users can tell PCs apart).
        /// </summary>
        public static async Task<List<DiscoveredServer>> FindAllAsync(
            TimeSpan timeout, bool stopAtFirst = false, CancellationToken ct = default) {

            string? local = GetLocalIPv4();
            var found = new ConcurrentDictionary<string, byte>();

            using var udp = new UdpClient();
            try { udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0)); } catch { /* already bound */ }

            var pb = new PacketBuilder("EsbToLanScan");
            byte[] handshake = pb.BuildHandshakePacket(
                BoardType.SLIMEVR, ImuType.UNKNOWN, McuType.UNKNOWN,
                MagnetometerStatus.NOT_SUPPORTED, new byte[] { 0, 0, 0, 0, 0, 1 });

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var receiver = Task.Run(async () => {
                try {
                    while (!linked.Token.IsCancellationRequested) {
                        var res = await udp.ReceiveAsync(linked.Token);
                        var text = Encoding.UTF8.GetString(res.Buffer);
                        if (text.Contains(HandshakeReply)) {
                            found.TryAdd(res.RemoteEndPoint.Address.ToString(), 0);
                            if (stopAtFirst) { linked.Cancel(); return; }
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
                        for (int i = 1; i <= 254 && !linked.Token.IsCancellationRequested; i++) {
                            if (IPAddress.TryParse(baseAddr + i, out var addr))
                                await SafeSend(udp, handshake, addr, linked.Token);
                            if ((i & 15) == 0) await Task.Delay(5, linked.Token); // gentle pacing
                        }
                    }
                }

                await Task.WhenAny(receiver, Task.Delay(timeout, linked.Token));
            } catch {
                /* fall through to whatever we collected */
            } finally {
                linked.Cancel();
            }

            // Resolve friendly names so users can pick the right PC.
            var list = new List<DiscoveredServer>();
            foreach (var ip in found.Keys)
                list.Add(new DiscoveredServer(ip, await ResolveName(ip)));
            list.Sort((a, b) => string.CompareOrdinal(a.Ip, b.Ip));
            return list;
        }

        private static async Task<string> ResolveName(string ip) {
            try {
                var t = Dns.GetHostEntryAsync(ip);
                var done = await Task.WhenAny(t, Task.Delay(800));
                if (done == t && t.IsCompletedSuccessfully) {
                    var name = t.Result.HostName;
                    if (!string.IsNullOrWhiteSpace(name) && name != ip)
                        return name.Split('.')[0]; // short host name
                }
            } catch { /* no reverse DNS */ }
            return ip;
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
