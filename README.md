# VYRO VR Connect

This projects allows extending the range of smol slimes/butterflies by allowing their data to be repeated over UDP via the side of a quest headset, cellphone, ESP32-S3 or any other device with USB port support, and a wifi antenna.

Potential use cases are extending the effective range of the trackers from just 10 meters to the entire range of cell tower service, or the range of wifi routers.

Also useful for ShadowPC users.

Current supported platforms are Windows, Quest, Android, and ESP32-S3

## How it works

The ESB receiver dongle (VID `0x1209`, PID `0x7690`) enumerates as a USB HID
device and emits fixed 16-byte packets containing per-tracker rotation,
acceleration, battery and signal data. The host app reads those packets and
re-emits them to a SlimeVR server over UDP using the SlimeVR feeder protocol
(`SlimeImuProtocol` submodule). SlimeVR Server itself is the listener (it binds
UDP `6969`); the app is just a client that impersonates a normal SlimeVR Wi-Fi
tracker, so no extra software is needed on the PC.

If no server IP is configured, the app scans the local network (every host on
the /24, on port 6969) to find the SlimeVR server automatically. If more than
one SlimeVR server is found — e.g. two players on the same network — the app
lists each PC (hostname + IP) and lets you pick the right one rather than
guessing.

## Projects

| Folder | Platform | Notes |
| --- | --- | --- |
| `EsbImuReceiverToLAN/` | Windows | .NET 10 console app (`HidSharp`). |
| `EsbImuReceiverToLanAndroid/` | Android / Meta Quest | .NET 10 MAUI app. |
| `EsbImuReceiverToLanESP32/` | ESP32-S3 | PlatformIO WiFi relay firmware. |
| `SlimeImuProtocol/` | shared | Git submodule with the SlimeVR UDP protocol. |

Clone with submodules (or run `git submodule update --init --recursive` after
cloning):

```
git clone --recursive <repo-url>
```

## Android / Meta Quest

Build configurations:

- `Release-Phone` — phone build, uses `Platforms/Android/AndroidManifest.xml`.
- `Release-Quest` — Quest build, uses `Platforms/Android/AndroidManifest.Quest.xml`.

```
dotnet build EsbImuReceiverToLanAndroid/EsbImuReceiverToLanAndroid.csproj -c Release-Quest -f net10.0-android
```

Sideload the resulting APK with `adb install -r <path-to-apk>` (or SideQuest).
On Quest the app appears under *Apps → Unknown Sources*. Plug the receiver into
the headset's USB-C port (a powered adapter helps); the app launches on attach
and starts streaming. With no server IP entered it auto-discovers SlimeVR on
the LAN.

### Plug-and-play behaviour

Plugging in the dongle fires the `USB_DEVICE_ATTACHED` intent, which launches
the app and starts the foreground streaming service. If you have not entered a
server IP, the app scans the local network for a SlimeVR server (see below) so
it works without any setup.

### Troubleshooting a crash on Quest

Unhandled exceptions are captured to:

```
/sdcard/Android/data/com.vyrovr.connect/files/crash.log
```

Pull it with `adb pull` (or a file browser over MTP) — no root required. The
most recent crash is also shown in a dialog the next time the app opens, and
mirrored to logcat under the `EsbCrash` tag (`adb logcat -s EsbCrash`).

## Windows

A lightweight system-tray app (WinForms). It runs in the background with a tray
icon and **auto-discovers your SlimeVR server** — in the common case you never
type an IP. Right-click the tray icon for:

- **Recent servers** — pick a previously used server.
- **Set SlimeVR IP…** — enter the server address manually (blank = auto-discover).
  Shows this PC's IP to help you check you're on the same network.
- **Test connection** — confirms the SlimeVR server actually responds.
- **Re-discover server** — scan the local network to find SlimeVR again.
- **Open data folder** — opens the folder containing `config.txt`.
- **Exit**.

The status line shows *Searching → Connected to 192.168.x.x*, this PC's IP, and
whether trackers are detected. If no server is found after ~10s it nudges you to
check SlimeVR is running and the PC is on the same network. Build/run on Windows:

```
dotnet run --project EsbImuReceiverToLAN/EsbImuReceiverToLAN.csproj
```
