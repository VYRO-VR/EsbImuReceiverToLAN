# VYRO VR

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

The ESB receiver dongle (VID `0x1209`, PID `0x7690`) enumerates as a USB device
and emits fixed 16-byte packets containing per-tracker rotation, acceleration,
battery and signal data. The app reads those packets and re-emits them to a
SlimeVR server over UDP using the SlimeVR feeder protocol. SlimeVR Server itself
is the listener (it binds UDP `6969`); the app is just a client that impersonates
a normal SlimeVR Wi-Fi tracker, so no extra software is needed on the PC.

If no server IP is configured, the app scans the local network (every host on
the /24, on port 6969) to find the SlimeVR server automatically.

## Building

The app is a native Kotlin/Android project under `android/`:

```
cd android
gradle assembleDebug
```

The app targets Android API 34 (the highest API supported by Horizon OS) and
runs on API 26+.

Sideload the resulting APK with `adb install -r <path-to-apk>` (or SideQuest).
On Quest/Pico the app appears under *Apps → Unknown Sources*. Plug the receiver
into the headset's USB-C port (a powered adapter helps); the app launches on
attach and starts streaming. With no server IP entered it auto-discovers SlimeVR
on the LAN.

## Debugging

The app logs its full pipeline to both an on-screen activity log and Android's
system log under the tag `VyroVrConnect`:

```
adb logcat -s VyroVrConnect:* AndroidRuntime:*
```
