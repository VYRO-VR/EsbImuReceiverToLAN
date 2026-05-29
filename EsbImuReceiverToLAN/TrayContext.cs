using System.Diagnostics;
using System.Net;
using System.Windows.Forms;
using EsbImuReceiverToLan.Tracking.Trackers.HID;
using SlimeImuProtocol.SlimeVR;

namespace EspImuReceiverToLAN {
    /// <summary>
    /// System-tray host for the ESB → SlimeVR relay. Keeps the existing
    /// <see cref="TrackersHID"/> + <see cref="UDPHandler"/> pipeline running in
    /// the background with a minimal tray UI: status, set/discover the SlimeVR
    /// server address, and exit.
    /// </summary>
    internal sealed class TrayContext : ApplicationContext {
        private const string BroadcastAddress = "255.255.255.255";

        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _statusItem;
        private readonly string _configPath;
        private readonly System.Windows.Forms.Timer _uiTimer;

        private TrackersHID? _trackers;
        private bool _discoverySubscribed;
        private volatile bool _trackersDetected;
        private volatile string? _discoveredIpPending;

        public TrayContext() {
            _configPath = Path.Combine(AppContext.BaseDirectory, "config.txt");

            _statusItem = new ToolStripMenuItem("Starting…") { Enabled = false };

            var menu = new ContextMenuStrip();
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Set SlimeVR IP…", null, (_, _) => PromptForIp());
            menu.Items.Add("Re-discover server", null, (_, _) => BeginDiscovery(force: true));
            menu.Items.Add("Open data folder", null, (_, _) => OpenDataFolder());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitThread());

            _notifyIcon = new NotifyIcon {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "ESB IMU Receiver",
                Visible = true,
                ContextMenuStrip = menu
            };
            _notifyIcon.DoubleClick += (_, _) => ShowStatusBalloon();

            LoadEndpointOrDiscover();
            StartTrackers();

            _uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _uiTimer.Tick += (_, _) => RefreshStatus();
            _uiTimer.Start();
        }

        private void LoadEndpointOrDiscover() {
            string? saved = File.Exists(_configPath) ? File.ReadAllText(_configPath).Trim() : null;
            if (!string.IsNullOrEmpty(saved) && saved != BroadcastAddress && IPAddress.TryParse(saved, out _)) {
                UDPHandler.Endpoint = saved;
            } else {
                BeginDiscovery(force: false);
            }
        }

        private void BeginDiscovery(bool force) {
            if (!_discoverySubscribed) {
                _discoverySubscribed = true;
                UDPHandler.OnServerDiscovered += OnServerDiscovered;
            }
            UDPHandler.Endpoint = BroadcastAddress;
            if (force) {
                try { UDPHandler.ForceUDPClientsToDoHandshake(); } catch { /* best effort */ }
            }
        }

        private void OnServerDiscovered(object? sender, string ip) {
            if (string.IsNullOrEmpty(ip) || !IPAddress.TryParse(ip, out _))
                return;

            UDPHandler.Endpoint = ip;
            try { File.WriteAllText(_configPath, ip); } catch { /* best effort */ }

            // This fires on a background UDP thread; hand the notification off to
            // the UI timer (which runs on the UI thread) instead of marshalling.
            _discoveredIpPending = ip;
        }

        private void StartTrackers() {
            if (_trackers != null) return;
            _trackers = new TrackersHID();
            // Fires when a tracker registers with the receiver; use it as a
            // simple "trackers detected" signal for the status text.
            _trackers.trackersConsumer += (_, _) => _trackersDetected = true;
        }

        private void PromptForIp() {
            var current = UDPHandler.Endpoint == BroadcastAddress ? "" : (UDPHandler.Endpoint ?? "");
            var input = InputBox.Show("Enter the SlimeVR server IP address.\nLeave blank to auto-discover.",
                "Set SlimeVR IP", current);
            if (input == null) return; // cancelled

            input = input.Trim();
            if (string.IsNullOrEmpty(input)) {
                BeginDiscovery(force: true);
                return;
            }

            if (!IPAddress.TryParse(input, out _)) {
                MessageBox.Show("That is not a valid IP address.", "ESB IMU Receiver",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            UDPHandler.Endpoint = input;
            try { File.WriteAllText(_configPath, input); } catch { /* best effort */ }
            try { UDPHandler.ForceUDPClientsToDoHandshake(); } catch { /* best effort */ }
            RefreshStatus();
        }

        private void OpenDataFolder() {
            try {
                Process.Start(new ProcessStartInfo {
                    FileName = AppContext.BaseDirectory,
                    UseShellExecute = true
                });
            } catch { /* best effort */ }
        }

        private void ShowStatusBalloon() {
            _notifyIcon.ShowBalloonTip(3000, "ESB IMU Receiver", BuildStatusText(), ToolTipIcon.Info);
        }

        private void RefreshStatus() {
            // Runs on the UI thread via _uiTimer; safe place to surface a
            // discovery that happened on a background thread.
            var discovered = _discoveredIpPending;
            if (discovered != null) {
                _discoveredIpPending = null;
                _notifyIcon.ShowBalloonTip(3000, "ESB IMU Receiver",
                    $"SlimeVR server found at {discovered}", ToolTipIcon.Info);
            }

            var status = BuildStatusText();
            _statusItem.Text = status;
            // NotifyIcon.Text has a 63-character limit.
            _notifyIcon.Text = status.Length > 63 ? status.Substring(0, 63) : status;
        }

        private string BuildStatusText() {
            var endpoint = UDPHandler.Endpoint;
            string target = string.IsNullOrEmpty(endpoint) || endpoint == BroadcastAddress
                ? "Discovering SlimeVR server…"
                : $"Server: {endpoint}";

            string flow = _trackersDetected ? "trackers detected" : "waiting for trackers";
            return $"{target} • {flow}";
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                _uiTimer?.Stop();
                _uiTimer?.Dispose();
                if (_notifyIcon != null) {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
