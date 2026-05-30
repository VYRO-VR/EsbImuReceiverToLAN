using System.Net;
using Microsoft.Maui.Storage;

namespace EsbReceiverToLanAndroid {
    /// <summary>Persists the most-recently used SlimeVR server IPs (most recent first).</summary>
    internal static class RecentServers {
        private const int MaxEntries = 5;
        private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "recent_servers.txt");

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
