# VYRO VR Connect — Receiver firmware (HolyIOT nRF52840 USB dongle)

This folder fixes the **~10 cm range** problem on the small HolyIOT nRF52840 USB
receiver by providing a correct SlimeVR/SlimeNRF **receiver** board definition
for it.

## TL;DR

The dongle is a **plain nRF52840 with a chip antenna and no power amplifier**
(confirmed from the manufacturer schematics in [`docs/`](docs/)). SlimeVR's
stock `holyiot_21017` target is for a *different* board that has an external
**Skyworks SKY66112‑11 front‑end module (PA/LNA)**. Flashing the 21017 firmware
configures the radio for a +22 dB amplifier that isn't there and drives PA
control pins (P0.24/P0.22) that go nowhere — so range collapses to ~10 cm.

**Fix:** build with a board target that has the **FEM removed**, so the radio
drives the antenna directly at the nRF52840 internal maximum (+8 dBm).

## Which board is it?

The board is the **HolyIOT YJ‑17120** (confirmed): an nRF52840‑QIAA with a chip
antenna, a single red status LED on **P1.01**, and **no PA/FEM**. The
`holyiot_yj17120` board definition here matches it.

| Model | Chip | PA/FEM? | Notes |
| --- | --- | --- | --- |
| **YJ‑17120** (this one) | nRF52840‑QIAA | **No** | Single red LED on P1.01. |
| YJ‑17076 | nRF52840 | **No** | Other near‑identical HolyIOT stick; schematic in `docs/` for reference. |

## Prebuilt UF2

CI builds a flashable `holyiot_yj17120.uf2` on every change under `firmware/`
(see `.github/workflows/receiver-firmware.yml`). Grab it from the workflow run's
artifacts, or from the rolling `receiver-firmware-latest` prerelease after merge
to `main`. To flash: double‑tap the dongle's reset to enter the UF2 bootloader,
then copy the `.uf2` onto the mounted drive.

## Fastest confirmation (no new files)

The stock plain‑dongle target has no FEM and is electrically equivalent to this
board's RF path. Building it should immediately restore range:

```
west build -b nrf52840dongle/nrf52840 -p
```

If range jumps from ~10 cm to metres, the diagnosis is confirmed. (Status LED
may be on a different pin — cosmetic only.)

## Proper target: `holyiot_yj17120`

1. Clone the receiver firmware and check out the revision you were building:
   ```
   git clone --recursive https://github.com/SlimeVR/SlimeVR-Tracker-nRF-Receiver
   ```
2. Copy `boards/holyiot/holyiot_yj17120/` from here into that repo's
   `boards/holyiot/`.
3. Build and flash:
   ```
   west build -b holyiot_yj17120/nrf52840 -p
   west flash      # or drag the generated UF2 onto the dongle's bootloader drive
   ```

The receiver enumerates over USB as the HID device VYRO VR Connect reads
(VID `0x1209`, PID `0x7690`).

## What changed vs. holyiot_21017

Removed everything that assumes an external amplifier:

- **`.dts`:** deleted the `nrf_radio_fem: skyFem { … }` node and the
  `&radio { fem = <&nrf_radio_fem>; }` reference (direct‑drive antenna). LED set
  reduced to the single red LED this board actually has (P1.01); RGB PWM LEDs
  and the unpopulated button removed.
- **`_defconfig`:** dropped `CONFIG_LED_RGB_COLOR`.
- **`Kconfig`:** dropped the `MPSL_FEM` / `BOARD_ENABLE_FEM` selects, so
  `CONFIG_MPSL_FEM*` is **not** enabled and the radio runs at full internal
  power.

## TX power

With no FEM, set the SlimeNRF application's ESB/RADIO TX power to the nRF52840
maximum, **+8 dBm** (`RADIO_TXPOWER_TXPOWER_Pos8dBm`). The board files above stop
the *under*‑driving caused by the FEM model; make sure the app isn't separately
capping TX power lower.

## Caveats

- **RF/range fix is certain** (FEM removed). **LED/button pins** are the only
  board‑specific guess and don't affect range — verify against `docs/` if you
  want the status LED correct.
- **Flash partitioning** assumes the Nordic nRF5 / Adafruit UF2 bootloader, like
  the other UF2 dongle targets. If you flash a full image over SWD/J‑Link the
  layout is irrelevant; if your bootloader sits at a different offset, adjust
  accordingly.
- These files are **not compile‑tested here** (no Zephyr/west toolchain in this
  environment). Build locally against the SlimeVR firmware tree as above.

## docs/

- `docs/YJ-17120-schematic.pdf` — manufacturer schematic (nRF52840‑QIAA, no PA;
  source for the antenna network and the P1.01 LED).
- `docs/YJ-17076-schematic.pdf` — the other candidate, for reference.
