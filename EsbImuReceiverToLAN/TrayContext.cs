using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using EsbImuReceiverToLan.Tracking.Trackers.HID;
using SlimeImuProtocol.SlimeVR;

namespace EspImuReceiverToLAN {
    /// <summary>
    /// System-tray host for the ESB → SlimeVR relay. Auto-discovers the SlimeVR
    /// server (no IP typing required in the common case), with manual entry,
    /// recent-server pick, and a connection test as conveniences.
    /// </summary>
    internal sealed class TrayContext : ApplicationContext {
        private const string BroadcastAddress = "255.255.255.255";

        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _recentMenu;
        private readonly ToolStripMenuItem _testItem;
        private readonly string _configPath;
        private readonly string _localIp;
        private readonly System.Windows.Forms.Timer _uiTimer;
        private readonly ServerProbe _discoveryProbe = new();

        private TrackersHID? _trackers;
        private bool _serverConfirmed;
        private volatile bool _trackersDetected;
        private volatile string? _discoveredIpPending;
        private DateTime _discoveryStartedUtc = DateTime.MinValue;
        private bool _noServerTipShown;
        private CancellationTokenSource? _scanCts;
        private volatile List<DiscoveredServer>? _pendingServerChoices;
        private bool _choosingServer;

        public TrayContext() {
            _configPath = Path.Combine(AppContext.BaseDirectory, "config.txt");
            _localIp = GetLocalIPv4();

            _statusItem = new ToolStripMenuItem("Starting…") { Enabled = false };
            _recentMenu = new ToolStripMenuItem("Recent servers");
            _testItem = new ToolStripMenuItem("Test connection", null, async (_, _) => await TestConnection());

            var menu = new ContextMenuStrip();
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_recentMenu);
            menu.Items.Add("Set SlimeVR IP…", null, (_, _) => PromptForIp());
            menu.Items.Add(_testItem);
            menu.Items.Add("Re-discover server", null, (_, _) => StartDiscovery(alsoExisting: true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open data folder", null, (_, _) => OpenDataFolder());
            menu.Items.Add("Exit", null, (_, _) => ExitThread());
            menu.Opening += (_, _) => RebuildRecentMenu();

            _notifyIcon = new NotifyIcon {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "ESB IMU Receiver",
                Visible = true,
                ContextMenuStrip = menu
            };
            _notifyIcon.DoubleClick += (_, _) => ShowStatusBalloon();

            UDPHandler.OnServerDiscovered += OnServerDiscovered;

            LoadEndpointOrDiscover();
            StartTrackers();

            _uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _uiTimer.Tick += (_, _) => OnTick();
            _uiTimer.Start();
        }

        private void LoadEndpointOrDiscover() {
            string? saved = File.Exists(_configPath) ? File.ReadAllText(_configPath).Trim() : null;
            if (!string.IsNullOrEmpty(saved) && saved != BroadcastAddress && IPAddress.TryParse(saved, out _)) {
                UDPHandler.Endpoint = saved;
                RecentServers.Add(saved);
                // Confirm the saved server is actually reachable in the background.
                _discoveryProbe.Start(saved);
                _discoveryStartedUtc = DateTime.UtcNow;
            } else {
                StartDiscovery(alsoExisting: false);
            }
        }

        private void StartDiscovery(bool alsoExisting) {
            _serverConfirmed = false;
            _noServerTipShown = false;
            _discoveryStartedUtc = DateTime.UtcNow;

            // Broadcast isn't reliable, so scan the local subnet for the server.
            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;
            _ = Task.Run(async () => {
                var servers = await ServerScan.FindAllAsync(TimeSpan.FromSeconds(8), stopAtFirst: false, token);
                if (token.IsCancellationRequested || servers.Count == 0)
                    return;
                if (servers.Count == 1) {
                    ApplyDiscoveredServer(servers[0].Ip);
                } else {
                    // Multiple PCs running SlimeVR (e.g. two players) — let the user pick.
                    _pendingServerChoices = servers;
                }
            });

            if (alsoExisting) {
                try { UDPHandler.ForceUDPClientsToDoHandshake(); } catch { /* best effort */ }
            }
        }

        // Fired by the per-tracker handlers once a server answers their handshake.
        private void OnServerDiscovered(object? sender, string ip) => ApplyDiscoveredServer(ip);

        // Single place that records a found/confirmed server. Safe to call from a
        // background thread; the UI balloon is surfaced by the UI timer.
        private void ApplyDiscoveredServer(string ip) {
            if (string.IsNullOrEmpty(ip) || !IPAddress.TryParse(ip, out _))
                return;

            UDPHandler.Endpoint = ip;
            try { File.WriteAllText(_configPath, ip); } catch { /* best effort */ }
            RecentServers.Add(ip);
            _serverConfirmed = true;
            _scanCts?.Cancel();
            _discoveryProbe.Stop();
            _discoveredIpPending = ip;
        }

        private void StartTrackers() {
            if (_trackers != null) return;
            _trackers = new TrackersHID();
            _trackers.trackersConsumer += (_, _) => _trackersDetected = true;
        }

        private void SelectServer(string ip) {
            UDPHandler.Endpoint = ip;
            try { File.WriteAllText(_configPath, ip); } catch { /* best effort */ }
            RecentServers.Add(ip);
            _serverConfirmed = false;
            try { UDPHandler.ForceUDPClientsToDoHandshake(); } catch { /* best effort */ }
            // Confirm reachability (and re-point any active trackers).
            _discoveryProbe.Start(ip);
            _discoveryStartedUtc = DateTime.UtcNow;
            _noServerTipShown = false;
            RefreshStatus();
        }

        private void PromptForIp() {
            var current = UDPHandler.Endpoint == BroadcastAddress ? "" : (UDPHandler.Endpoint ?? "");
            var input = InputBox.Show(
                "Enter the IP of the PC running SlimeVR Server (e.g. 192.168.1.42).\n" +
                "It's shown in SlimeVR, or run 'ipconfig'. Leave blank to auto-discover.\n" +
                $"This device's IP: {_localIp}",
                "Set SlimeVR IP", current);
            if (input == null) return; // cancelled

            input = input.Trim();
            if (string.IsNullOrEmpty(input)) {
                StartDiscovery(alsoExisting: true);
                return;
            }

            if (!IPAddress.TryParse(input, out _)) {
                MessageBox.Show("That is not a valid IP address.", "ESB IMU Receiver",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SelectServer(input);
        }

        private async Task TestConnection() {
            var endpoint = UDPHandler.Endpoint;
            bool specific = !string.IsNullOrEmpty(endpoint) && endpoint != BroadcastAddress && IPAddress.TryParse(endpoint, out _);

            _testItem.Enabled = false;
            _testItem.Text = "Testing…";
            try {
                if (specific) {
                    bool ok = await ServerProbe.TestAsync(endpoint, TimeSpan.FromSeconds(6));
                    if (ok) {
                        _serverConfirmed = true;
                        _notifyIcon.ShowBalloonTip(4000, "ESB IMU Receiver",
                            $"SlimeVR server responded at {endpoint}. You're good to go.", ToolTipIcon.Info);
                    } else {
                        _notifyIcon.ShowBalloonTip(6000, "ESB IMU Receiver",
                            $"No response from SlimeVR at {endpoint}.\n" +
                            $"Check SlimeVR is running and this PC ({_localIp}) is on the same network.",
                            ToolTipIcon.Warning);
                    }
                } else {
                    // No specific IP: scan the subnet to find any server.
                    var found = await ServerScan.FindAsync(TimeSpan.FromSeconds(8));
                    if (found != null) {
                        ApplyDiscoveredServer(found);
                        _notifyIcon.ShowBalloonTip(4000, "ESB IMU Receiver",
                            $"Found SlimeVR server at {found}.", ToolTipIcon.Info);
                    } else {
                        _notifyIcon.ShowBalloonTip(6000, "ESB IMU Receiver",
                            $"No SlimeVR server found on this network.\n" +
                            $"Check SlimeVR is running and this PC ({_localIp}) is on the same network.",
                            ToolTipIcon.Warning);
                    }
                }
            } catch {
                /* best effort */
            } finally {
                _testItem.Text = "Test connection";
                _testItem.Enabled = true;
            }
        }

        private void RebuildRecentMenu() {
            _recentMenu.DropDownItems.Clear();
            var recents = RecentServers.Load();
            if (recents.Count == 0) {
                _recentMenu.DropDownItems.Add(new ToolStripMenuItem("(none yet)") { Enabled = false });
                return;
            }
            foreach (var ip in recents) {
                var item = new ToolStripMenuItem(ip);
                var captured = ip;
                item.Click += (_, _) => SelectServer(captured);
                if (string.Equals(ip, UDPHandler.Endpoint, StringComparison.OrdinalIgnoreCase))
                    item.Checked = true;
                _recentMenu.DropDownItems.Add(item);
            }
        }

        private void PromptServerChoice(List<DiscoveredServer> servers) {
            _choosingServer = true;
            try {
                var labels = servers.Select(s => s.Display).ToArray();
                var pick = ChoiceBox.Show(
                    "Multiple SlimeVR servers were found on this network.\nChoose the PC to send tracking data to:",
                    "Select your PC", labels);
                if (pick >= 0 && pick < servers.Count)
                    SelectServer(servers[pick].Ip);
            } finally {
                _choosingServer = false;
            }
        }

        private void OpenDataFolder() {
            try {
                Process.Start(new ProcessStartInfo { FileName = AppContext.BaseDirectory, UseShellExecute = true });
            } catch { /* best effort */ }
        }

        private void ShowStatusBalloon() {
            _notifyIcon.ShowBalloonTip(3000, "ESB IMU Receiver", BuildStatusText(longForm: true), ToolTipIcon.Info);
        }

        private void OnTick() {
            // Surface a discovery that happened on a background thread.
            var discovered = _discoveredIpPending;
            if (discovered != null) {
                _discoveredIpPending = null;
                _notifyIcon.ShowBalloonTip(3000, "ESB IMU Receiver",
                    $"Connected to SlimeVR at {discovered}", ToolTipIcon.Info);
            }

            // Multiple SlimeVR servers found — prompt (on the UI thread) for the right PC.
            var choices = _pendingServerChoices;
            if (choices != null && !_choosingServer) {
                _pendingServerChoices = null;
                PromptServerChoice(choices);
            }

            // After ~10s with no server found, nudge the user about the usual cause.
            if (!_serverConfirmed && !_noServerTipShown && _discoveryStartedUtc != DateTime.MinValue
                && (DateTime.UtcNow - _discoveryStartedUtc) > TimeSpan.FromSeconds(10)) {
                _noServerTipShown = true;
                _notifyIcon.ShowBalloonTip(7000, "Can't find SlimeVR yet",
                    "Make sure SlimeVR Server is running and this PC is on the same network.\n" +
                    $"This PC's IP: {_localIp}", ToolTipIcon.Warning);
            }

            RefreshStatus();
        }

        private void RefreshStatus() {
            var full = BuildStatusText(longForm: true);
            _statusItem.Text = full;
            var tip = BuildStatusText(longForm: false);
            _notifyIcon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        private string BuildStatusText(bool longForm) {
            var endpoint = UDPHandler.Endpoint;
            bool broadcasting = string.IsNullOrEmpty(endpoint) || endpoint == BroadcastAddress;

            string server = _serverConfirmed && !broadcasting ? $"Connected to {endpoint}"
                          : broadcasting ? "Searching for SlimeVR server…"
                          : $"Server: {endpoint} (not confirmed)";

            if (!longForm) return server;

            string trackers = _trackersDetected ? "trackers detected" : "no trackers yet";
            return $"{server}\nThis PC: {_localIp} • {trackers}";
        }

        private static string GetLocalIPv4() {
            try {
                // Picks the outbound interface without sending any traffic.
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Connect("8.8.8.8", 65530);
                return (s.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
            } catch {
                return "unknown";
            }
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                _uiTimer?.Stop();
                _uiTimer?.Dispose();
                _scanCts?.Cancel();
                _discoveryProbe?.Dispose();
                if (_notifyIcon != null) {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
