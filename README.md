# Guncon3Windows

> Use **GunCon 3** on Windows — Calibration tool integrated

Ported to **.NET 8**, with the input path reworked, one thread per gun, and the
sticks exposed both digitally and as a virtual joystick.


---

## Overview
This fork provides a working implementation of GunCon 3 for Windows, with built-in
calibration, both analog sticks, hot recalibration, and a virtual joystick output.

No manual setup or coding is required to use it — install the two drivers, run the
executable, and it calibrates itself on first launch.

---

## Features
- 🔧 **Automatic calibration on first launch**
- 🎯 **Hot recalibration** anytime with **F12**
- 🧭 **Five-point calibration system** (four corners + center) that fits two aiming
  mappings from one capture — the original linear rectangle and a projective one
  that stays accurate when the gun is held off the screen's axis — switchable live
  with **H** to compare
- 🎮 **Both analog sticks, digitalized** — left (LUp / LDown / LLeft / LRight) and
  right (RUp / RDown / RLeft / RRight), mappable like any button
- 🕹️ **Virtual joystick output** — both sticks as analog axes, plus the gun's depth
  axis and the nine physical buttons, alongside the mouse and keyboard
- 📐 **Self-measuring stick deadzones** — centred on each stick's actual resting
  position rather than an assumed one, measured on first run and remembered
- 🔌 **Survives a disconnect** — an unplugged gun is reported, releases its held
  buttons, and reconnects by itself
- 👥 **Multiple guns**, each on its own thread so they never wait on each other
- 🗒️ **Mapping errors are reported** with their line number instead of being
  skipped in silence
- 💾 **Local configuration files** (`mapping.txt`, `calibration_rect.txt`,
  `stick_centre.txt`)
- 🖥️ Works with most PC arcade and emulator lightgun setups

---

## What the gun looks like to Windows

Three virtual devices, all at once, through the TetherScript drivers:

| device | carries |
|---|---|
| absolute mouse | aiming, plus any button mapped to `MOUSE.*` |
| keyboard | any button mapped to `KEYBOARD.*` |
| joystick | both sticks, the depth axis and the nine physical buttons — always, no mapping needed |

A gun button can therefore produce both a keyboard key and a joystick button at the
same time. That is intended.

The joystick assignment is fixed and not configurable:

| field | source |
|---|---|
| `X`, `Y` | left stick |
| `rX`, `rY` | right stick |
| `Z` | the gun's depth axis |
| buttons 0–8 | Trigger, A1, A2, B1, B2, C1, C2, AClick, BClick |

---

## Requirements

1. **GunCon 3 WinUSB driver** — [`drivers/`](drivers/) in this repository, or the
   upstream [Releases](https://github.com/gameotaku79/Guncon3Windows/releases/latest)
   page.
2. **TetherScript HID Virtual Driver Kit** *(archived, discontinued)* — run its
   installer as Administrator and reboot. The joystick output needs that kit's
   virtual joystick driver; if it is absent the app logs a connect failure and
   carries on without it.
3. **Windows x64.**
4. **.NET 8 Desktop Runtime** — only for the framework-dependent build. The
   self-contained build needs nothing.

> ⚠️ **TetherScript is a dead end and it is worth knowing before you invest in it.**
> Its own SDK states the drivers install only on Windows 7, 8, 8.1 or 10 64-bit, and
> that the certificate signing them expired in spring 2023 — on Windows 11 they
> cannot be installed at all. The whole output path of this application depends on
> them, so it works on a machine that already has them and may not be installable on
> a new one.

---

## Building

The solution builds with the .NET 8 SDK and no IDE:

```
dotnet build src/Guncon3.sln
dotnet test tests/Guncon3.Core.Tests/Guncon3.Core.Tests.csproj
```

On macOS or Linux add `-p:EnableWindowsTargeting=true` — the Windows-targeted
projects still compile there, and the test suite runs anywhere because the pure
logic lives in a platform-neutral `Guncon3.Core` library. Note this needs the
official Microsoft .NET 8 SDK; Homebrew's `dotnet@8` is built from source and ships
no `Microsoft.NET.Sdk.WindowsDesktop`, so WinForms cannot build with it.

Two release shapes:

```
# self-contained — one exe, no runtime needed, ~162 MB
dotnet publish src/Guncon3Console/Guncon3Console.csproj -c Release -r win-x64 \
  --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# framework-dependent — ~285 KB, needs the .NET 8 Desktop Runtime
dotnet publish src/Guncon3Console/Guncon3Console.csproj -c Release -r win-x64 \
  --self-contained false -p:PublishSingleFile=true
```

The framework-dependent build needs the **Desktop** Runtime, not the plain one —
the calibration window is WinForms, so it requires `Microsoft.WindowsDesktop.App`.
Installing only the plain runtime gives a startup error naming a framework that
cannot be found.

---

## Running

```
Guncon3Console.exe          normal operation
Guncon3Console.exe keys     print the keycode table and exit
Guncon3Console.exe dump     capture raw USB frames to packets.txt
```

While running: **F12** recalibrates every gun, **R** reloads the mapping files,
**H** toggles every gun between the linear and projective calibration mapping
(not remembered across runs — every launch starts on the linear mapping),
**ESC** exits.

---

## Button mapping

Button and stick mappings live in **`mapping.txt`**, next to the executable. Each
line follows the format

`DEVICE.COMMAND = GUNCOMMAND`

for example `KEYBOARD.30 = C1` or `MOUSE.Left = Trigger`. Gun 2 reads
`mapping_2.txt`, gun 3 `mapping_3.txt`, and so on.

Valid gun commands are the nine physical buttons — `Trigger`, `A1`, `A2`, `B1`,
`B2`, `C1`, `C2`, `AClick`, `BClick` — plus the eight digitalized stick directions,
`LUp` / `LDown` / `LLeft` / `LRight` and `RUp` / `RDown` / `RLeft` / `RRight`.

Names are case-insensitive on both sides of the `=`. Every line that cannot be
understood is reported on startup with its line number, so a typo tells you where
it is instead of silently doing nothing.

To list the keyboard key codes:

```
Guncon3Console.exe keys
```

The same list is kept in [docs/keycodes.txt](docs/keycodes.txt) for convenience.

---

## Configuration files

| file | written by | notes |
|---|---|---|
| `mapping.txt` | you | per gun: `mapping_2.txt`, `mapping_3.txt`, … |
| `calibration_rect.txt` | the calibration window | per gun; stores the five raw captured points plus the derived rectangle, so both the linear and projective mappings come from one capture; files from earlier versions have no points and still load, on the linear mapping only, until the gun is recalibrated |
| `stick_centre.txt` | measured automatically | per gun; delete it to re-measure |

Stick centres are measured from the first 60 frames after startup and printed to the
console. **Do not hold a stick while the app is starting** — the resting position it
measures is whatever the stick is doing at the time. If the printed values look
wrong, delete `stick_centre.txt` and it measures again on the next run.

---

## Notes
- Recalibrate after changing monitor or resolution.
- Multi-monitor setups are supported.
- Design notes, the implementation plan and the handover checklist are in
  [docs/superpowers/](docs/superpowers/).
- All code and binaries licensed under **GPL-2.0**, inherited from the original work
  by [sonik-br](https://github.com/sonik-br/GunconUSB).

---

© 2025 gameotaku79
Fork of [sonik-br/GunconUSB](https://github.com/sonik-br/GunconUSB)
