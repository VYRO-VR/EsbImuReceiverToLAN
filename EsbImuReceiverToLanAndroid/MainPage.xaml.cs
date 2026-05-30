using Android.Content;
using EsbReceiverToLanAndroid.Models;
using Microsoft.Maui.ApplicationModel;
using EsbReceiverToLanAndroid.Platforms.Android.Services;
using EsbReceiverToLanAndroid.Views;
using SlimeImuProtocol.SlimeVR;
using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace EsbReceiverToLanAndroid;

public partial class MainPage : ContentPage
{
    private bool _isTrackerServiceStarted;
    private Intent? intent;
    private IDispatcherTimer? _refreshTimer;
    private string _lastTopologySignature = "";
    private readonly List<(TrackerRotationView RotView, Label NameLabel, Label InfoLabel, Grid Row)> _trackerRowCache = new();
    private readonly Dictionary<string, Vector3> _previousAcceleration = new();
    private const float AccelDeltaThreshold = 1f; // Accelerometer change (m/s² scale) to count as moving

    private List<string> _recentList = new();
    private bool _serverConfirmed;
    private bool _noServerTipShown;
    private bool _scanning;
    private bool _autoScanDone;
    private DateTime _discoveryStartedUtc = DateTime.MinValue;

    public MainPage()
    {
        InitializeComponent();
        LoadConfig();
        InitializeConnectionUi();
        _ = typeof(TrackerUsbReceiver);
        TrackerUsbReceiver.OnDeviceConnected += OnDeviceConnected;
        TrackerUsbReceiver.OnDeviceDisconnected += OnDeviceDisconnected;
        SlimeImuProtocol.SlimeVR.UDPHandler.OnServerDiscovered += OnServerDiscovered;
    }

    private void InitializeConnectionUi()
    {
        deviceIpLabel.Text = $"This device's IP: {GetLocalIPv4()}";
        RefreshRecents();
    }

    private void RefreshRecents()
    {
        _recentList = RecentServers.Load();
        recentPicker.ItemsSource = _recentList;
        recentPicker.IsVisible = _recentList.Count > 0;
    }

    private void RecentPicker_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (recentPicker.SelectedIndex < 0 || recentPicker.SelectedIndex >= _recentList.Count)
            return;
        ipEntry.Text = _recentList[recentPicker.SelectedIndex];
    }

    private async void TestButton_Clicked(object? sender, EventArgs e)
    {
        var target = ipEntry.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(target) && IPAddress.TryParse(target, out _))
        {
            // A specific IP is typed: confirm that exact server responds.
            testButton.IsEnabled = false;
            statusLabel.Text = "Testing connection…";
            statusLabel.TextColor = Colors.LightGreen;
            try
            {
                bool ok = await ServerProbe.TestAsync(target, TimeSpan.FromSeconds(6));
                if (ok)
                {
                    _serverConfirmed = true;
                    statusLabel.Text = $"SlimeVR responded at {target} ✓";
                    statusLabel.TextColor = Colors.LightGreen;
                    RecentServers.Add(target);
                    RefreshRecents();
                }
                else
                {
                    statusLabel.Text = "No response. Check SlimeVR is running and on the same Wi-Fi.";
                    statusLabel.TextColor = Colors.Orange;
                }
            }
            catch { /* best effort */ }
            finally { testButton.IsEnabled = true; }
        }
        else
        {
            // No IP typed: scan the network to find the server automatically.
            await ScanForServerAsync();
        }
    }

    private async Task ScanForServerAsync()
    {
        if (_scanning) return;
        _scanning = true;
        testButton.IsEnabled = false;
        statusLabel.Text = "Scanning network for SlimeVR…";
        statusLabel.TextColor = Colors.LightGreen;
        try
        {
            var servers = await ServerScan.FindAllAsync(TimeSpan.FromSeconds(8));
            if (servers.Count == 0)
            {
                statusLabel.Text = "No SlimeVR found. Check it's running and on the same Wi-Fi as this headset.";
                statusLabel.TextColor = Colors.Orange;
                return;
            }

            DiscoveredServer chosen = servers[0];
            if (servers.Count > 1)
            {
                // Multiple PCs running SlimeVR on this network (e.g. two players):
                // make the user pick the right one rather than guessing.
                var labels = servers.Select(s => s.Display).ToArray();
                var pick = await DisplayActionSheet(
                    "Multiple SlimeVR servers found — choose your PC:", "Cancel", null, labels);
                if (string.IsNullOrEmpty(pick) || pick == "Cancel")
                {
                    statusLabel.Text = $"{servers.Count} servers found — pick your PC to connect.";
                    statusLabel.TextColor = Colors.Orange;
                    return;
                }
                chosen = servers.First(s => s.Display == pick);
            }

            ApplyChosenServer(chosen);
        }
        catch { /* best effort */ }
        finally
        {
            _scanning = false;
            testButton.IsEnabled = true;
        }
    }

    private void ApplyChosenServer(DiscoveredServer server)
    {
        _serverConfirmed = true;
        ipEntry.Text = server.Ip;
        UDPHandler.Endpoint = server.Ip;
        try { File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "config.txt"), server.Ip); } catch { /* best effort */ }
        RecentServers.Add(server.Ip);
        RefreshRecents();
        statusLabel.Text = server.Name == server.Ip
            ? $"Connected to SlimeVR at {server.Ip}"
            : $"Connected to {server.Name} ({server.Ip})";
        statusLabel.TextColor = Colors.LightGreen;
    }

    private void CheckDiscoveryTip()
    {
        if (!_serverConfirmed && !_noServerTipShown && _discoveryStartedUtc != DateTime.MinValue
            && (DateTime.UtcNow - _discoveryStartedUtc) > TimeSpan.FromSeconds(10))
        {
            _noServerTipShown = true;
            statusLabel.Text = "Still searching… make sure SlimeVR is running and this headset is on the same Wi-Fi as your PC.";
            statusLabel.TextColor = Colors.Orange;
        }
    }

    private static string GetLocalIPv4()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 65530); // selects the outbound interface; sends nothing
            return (s.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private void OnServerDiscovered(object? sender, string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return;
        _serverConfirmed = true;
        RecentServers.Add(ip);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (string.IsNullOrWhiteSpace(ipEntry.Text) || ipEntry.Text == "255.255.255.255")
            {
                ipEntry.Text = ip;
                File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "config.txt"), ip);
            }
            statusLabel.Text = $"Connected to SlimeVR at {ip}";
            statusLabel.TextColor = Colors.LightGreen;
            RefreshRecents();
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _refreshTimer = Dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(50); // 20 Hz for smoother rotation preview
        _refreshTimer.Tick += (_, _) => RefreshTrackerList();
        _refreshTimer.Start();

        ShowPreviousCrashIfAny();

        // Auto-find the server on first show when nothing is configured yet.
        if (!_autoScanDone && (string.IsNullOrWhiteSpace(ipEntry.Text) || ipEntry.Text == "255.255.255.255"))
        {
            _autoScanDone = true;
            _ = ScanForServerAsync();
        }
    }

    private void ShowPreviousCrashIfAny()
    {
        var crash = Platforms.Android.CrashLog.ReadLast();
        if (string.IsNullOrEmpty(crash))
            return;

        // A crash was captured on a previous run. Surface it so it can be read /
        // screenshotted on the headset, then clear it so it only shows once.
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var preview = crash.Length > 1500 ? crash.Substring(0, 1500) + "\n…" : crash;
            await DisplayAlert("Previous crash detected", preview, "Dismiss");
            Platforms.Android.CrashLog.Clear();
        });
    }

    protected override void OnDisappearing()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
        base.OnDisappearing();
    }

    private void OnDeviceConnected(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            startButton.Text = "Stop";
            statusLabel.Text = "Receiving...";
            statusLabel.TextColor = Colors.LightGreen;
            _isTrackerServiceStarted = true;
        });
    }

    private void OnDeviceDisconnected(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            startButton.Text = "Start";
            statusLabel.Text = "Stopped";
            statusLabel.TextColor = Colors.Orange;
            _isTrackerServiceStarted = false;
        });
    }

    private void RefreshTrackerList()
    {
        CheckDiscoveryTip();
        var snapshot = TrackerListenerService.Instance?.GetTrackerSnapshot();
        MainThread.BeginInvokeOnMainThread(() => UpdateTrackerUI(snapshot));
    }

    private void UpdateTrackerUI(TrackerSnapshot? snapshot)
    {
        if (snapshot == null || snapshot.Dongles.Count == 0)
        {
            if (_lastTopologySignature != "")
            {
                _lastTopologySignature = "";
                _trackerRowCache.Clear();
                _previousAcceleration.Clear();
                trackerListContainer.Children.Clear();
            }
            trackerSummaryLabel.Text = "No trackers connected";
            return;
        }

        var total = snapshot.Dongles.Sum(d => d.Trackers.Count);
        trackerSummaryLabel.Text = $"{total} tracker(s) across {snapshot.Dongles.Count} dongle(s)";

        var orderedTrackers = snapshot.Dongles
            .SelectMany(d => d.Trackers.OrderBy(t => t.Id).Select(t => (Dongle: d, Tracker: t)))
            .ToList();
        var signature = string.Join("|", snapshot.Dongles.Select(d => d.DeviceKey + ":" + string.Join(",", d.Trackers.OrderBy(t => t.Id).Select(t => t.Id))));

        if (signature != _lastTopologySignature)
        {
            _lastTopologySignature = signature;
            _previousAcceleration.Clear();
            RebuildTrackerList(snapshot, orderedTrackers);
        }
        else
        {
            for (int i = 0; i < orderedTrackers.Count && i < _trackerRowCache.Count; i++)
            {
                var (dongle, t) = orderedTrackers[i];
                var (rotView, nameLabel, infoLabel, row) = _trackerRowCache[i];
                rotView.TrackerRotation = t.Rotation;
                nameLabel.Text = t.DisplayName;
                infoLabel.Text = $"{t.BatteryLevel:F0}% • {t.Status}";

                var key = i.ToString(); // Use index - unique per row, avoids ID collision between trackers
                var accel = t.Acceleration;
                var moving = false;
                if (_previousAcceleration.TryGetValue(key, out var prev))
                    moving = (accel - prev).Length() > AccelDeltaThreshold;
                _previousAcceleration[key] = accel;

                row.BackgroundColor = moving ? Color.FromArgb("#254ade80") : Colors.Transparent; // subtle green tint when moving
            }
        }
    }

    private void RebuildTrackerList(TrackerSnapshot snapshot, List<(DongleGroup Dongle, TrackerInfo Tracker)> orderedTrackers)
    {
        _trackerRowCache.Clear();
        trackerListContainer.Children.Clear();

        var dongleGroups = orderedTrackers.GroupBy(x => x.Dongle.DeviceKey).ToList();
        foreach (var grp in dongleGroups)
        {
            var dongle = grp.First().Dongle;
            var dongleHeader = new Frame
            {
                BackgroundColor = Color.FromArgb("#2a2a3e"),
                BorderColor = Color.FromArgb("#3a3a4e"),
                Padding = new Thickness(12, 8),
                CornerRadius = 6,
                Margin = new Thickness(0, 8, 0, 0),
                Content = new Label
                {
                    Text = $"📡 {dongle.DisplayName} ({dongle.Trackers.Count} trackers)",
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    FontSize = 14
                }
            };
            trackerListContainer.Children.Add(dongleHeader);

            foreach (var (_, t) in grp)
            {
                var row = new Grid
                {
                    Padding = new Thickness(12, 8),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(40) },
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = new GridLength(60) }
                    }
                };

                var rotView = new TrackerRotationView { TrackerRotation = t.Rotation };
                var nameLabel = new Label
                {
                    Text = t.DisplayName,
                    TextColor = Colors.White,
                    VerticalOptions = LayoutOptions.Center
                };
                var infoLabel = new Label
                {
                    Text = $"{t.BatteryLevel:F0}% • {t.Status}",
                    TextColor = Colors.Gray,
                    FontSize = 12,
                    VerticalOptions = LayoutOptions.Center
                };

                var rightStack = new VerticalStackLayout { Spacing = 2 };
                rightStack.Children.Add(nameLabel);
                rightStack.Children.Add(infoLabel);

                row.Add(rotView, 0, 0);
                row.Add(rightStack, 1, 0);
                trackerListContainer.Children.Add(row);
                _trackerRowCache.Add((rotView, nameLabel, infoLabel, row));
            }
        }
    }

    private void StartButton_Clicked(object? sender, EventArgs e)
    {
        var context = Platform.CurrentActivity ?? Android.App.Application.Context;

        if (!_isTrackerServiceStarted)
        {
            intent = new Intent(context, typeof(TrackerListenerService));
            var endpoint = ipEntry.Text?.Trim();
            
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                endpoint = "255.255.255.255";
            }

            if (IPAddress.TryParse(endpoint, out _))
            {
                UDPHandler.Endpoint = endpoint;
                if (endpoint != "255.255.255.255")
                {
                    File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "config.txt"), endpoint);
                    RecentServers.Add(endpoint);
                    RefreshRecents();
                }

                // Track discovery so we can nudge the user if no server answers.
                _serverConfirmed = endpoint != "255.255.255.255";
                _noServerTipShown = false;
                _discoveryStartedUtc = (endpoint == "255.255.255.255") ? DateTime.UtcNow : DateTime.MinValue;

                statusLabel.Text = (endpoint == "255.255.255.255") ? "Searching for SlimeVR…" : "Starting…";
                statusLabel.TextColor = Colors.LightGreen;

                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
                    context.StartForegroundService(intent);
                else
                    context.StartService(intent);

                _isTrackerServiceStarted = true;
                startButton.Text = "Stop";
            }
            else
            {
                statusLabel.Text = "Invalid IP address";
                statusLabel.TextColor = Colors.OrangeRed;
            }
        }
        else
        {
            TrackerListenerService.Instance?.StopTrackerWork();
            TrackerListenerService.Instance?.StopSelf();
            try { context?.StopService(intent); } catch { }
            _isTrackerServiceStarted = false;
            startButton.Text = "Start";
            statusLabel.Text = "Stopped";
            statusLabel.TextColor = Colors.Orange;
        }
    }

    private async void RefreshButton_Clicked(object? sender, EventArgs e)
    {
        try { UDPHandler.ForceUDPClientsToDoHandshake(); } catch { /* best effort */ }
        await ScanForServerAsync();
    }

    private void LoadConfig()
    {
        var configPath = Path.Combine(FileSystem.AppDataDirectory, "config.txt");
        if (File.Exists(configPath))
            ipEntry.Text = File.ReadAllText(configPath);
    }
}
