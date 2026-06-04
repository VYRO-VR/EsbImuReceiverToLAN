# VYRO VR Connect

This project extends the range of smol slimes/butterflies by repeating their
data over UDP from a standalone headset. Plug the ESB receiver dongle into the
USB-C port of a Quest or Pico headset (or an Android phone) and the app relays
the trackers to your SlimeVR server over Wi-Fi.

Instead of the dongle being limited to ~10 meters from a PC, the trackers now
reach as far as the headset's own Wi-Fi connection.

Supported platforms: Meta Quest, Pico, and Android.

## Credits

Original concept and implementation by **[Sebane1](https://github.com/Sebane1)**
— see the original project at
[Sebane1/EsbImuReceiverToLAN](https://github.com/Sebane1/EsbImuReceiverToLAN).
This project builds on that idea. Full credit for the original design goes to
them.

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
| `EsbImuReceiverToLanAndroid/` | Android / Meta Quest / Pico | .NET 10 MAUI app. |
| `SlimeImuProtocol/` | shared | Git submodule with the SlimeVR UDP protocol. |

Clone with submodules (or run `git submodule update --init --recursive` after
cloning):

```
git clone --recursive <repo-url>
```

## Android / Meta Quest / Pico

Build the APK:

```
dotnet build EsbImuReceiverToLanAndroid/EsbImuReceiverToLanAndroid.csproj -c Release -f net10.0-android
```

The app targets Android API 34 (the highest API supported by Horizon OS) and
runs on API 29+.

Sideload the resulting APK with `adb install -r <path-to-apk>` (or SideQuest).
On Quest/Pico the app appears under *Apps → Unknown Sources*. Plug the receiver
into the headset's USB-C port (a powered adapter helps); the app launches on
attach and starts streaming. With no server IP entered it auto-discovers
SlimeVR on the LAN.

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
