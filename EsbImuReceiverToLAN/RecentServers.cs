using System.Net;

namespace EspImuReceiverToLAN {
    /// <summary>Persists the most-recently used SlimeVR server IPs (most recent first).</summary>
    internal static class RecentServers {
        private const int MaxEntries = 5;
        private static string FilePath => Path.Combine(AppContext.BaseDirectory, "recent_servers.txt");

        public static List<string> Load() {
            try {
                if (!File.Exists(FilePath)) return new List<string>();
                return File.ReadAllLines(FilePath)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && IPAddress.TryParse(l, out _))
                    .Distinct()
                    .Take(MaxEntries)
                    .ToList();
            } catch {
                return new List<string>();
            }
        }

        /// <summary>Move <paramref name="ip"/> to the top of the recent list and persist.</summary>
        public static void Add(string? ip) {
            if (string.IsNullOrWhiteSpace(ip) || ip == "255.255.255.255" || !IPAddress.TryParse(ip, out _))
                return;
            try {
                var list = Load();
                list.RemoveAll(x => string.Equals(x, ip, StringComparison.OrdinalIgnoreCase));
                list.Insert(0, ip);
                File.WriteAllLines(FilePath, list.Take(MaxEntries));
            } catch {
                /* best effort */
            }
        }
    }
}
