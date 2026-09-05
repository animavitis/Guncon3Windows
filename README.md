# Guncon3Windows

> Use **GunCon 3** on Windows, with calibration built in.

A fork ported to **.NET 10**: the input path reworked, one thread per gun, both sticks
exposed digitally and as a virtual joystick, and a window with a tray icon. Install the
two drivers, run the executable, and it calibrates itself on first launch.

## Features

- A window and a tray icon: a row per gun with its connection, calibration and feeder
  state, a live log, a mapping editor and a live test view. Closing hides it to the tray.
- Five-point calibration (four corners and the centre) fits two mappings from one
  capture: the linear rectangle and a projective one that holds up when the gun is off
  the screen's axis. **H** switches live, **F12** recalibrates, and nothing is written
  until you accept.
- Both analog sticks digitalized and mappable, plus a virtual joystick with both sticks,
  the depth axis and the nine buttons. Stick deadzones are measured from each stick's own
  resting position on first run.
- An unplugged gun is reported, releases its buttons and reconnects by itself.
- Multiple guns, each on its own thread. Multi-monitor: the aim follows the monitor it
  was calibrated on.
- Local configuration files, with mapping errors reported by line number.

Three virtual devices, through the TetherScript drivers:

| device | carries |
|---|---|
| absolute mouse | aiming, plus any button mapped to `MOUSE.*` |
| keyboard | any button mapped to `KEYBOARD.*` |
| joystick | both sticks (`X`/`Y`, `rX`/`rY`), depth (`Z`) and buttons 0-8 (Trigger, A1, A2, B1, B2, C1, C2, AClick, BClick) — always, no mapping needed |

One gun button can produce a keyboard key and a joystick button at once. The joystick
assignment is fixed.

## Requirements

Windows x64, the **GunCon 3 WinUSB driver** ([`drivers/`](drivers/)), and the
**TetherScript HID Virtual Driver Kit**, installed as Administrator, then reboot. The
framework-dependent build also needs the **.NET 10 *Desktop* Runtime**; the plain runtime
is not enough, the app is WinForms.

> ⚠️ **TetherScript is a dead end, worth knowing before you invest in it.** Its SDK
> installs only on 64-bit Windows 7, 8, 8.1 or 10, and the certificate signing the
> drivers expired in spring 2023, so on Windows 11 they cannot be installed at all.
> This application's whole output path depends on them.

## Building

```
dotnet build src/Guncon3.sln
dotnet test tests/Guncon3.Core.Tests/Guncon3.Core.Tests.csproj

# release, one exe: --self-contained (~160 MB) or --self-contained false (~0.3 MB)
dotnet publish src/Guncon3Console/Guncon3Console.csproj -c Release -r win-x64 \
  --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

On macOS or Linux add `-p:EnableWindowsTargeting=true`; the tests run anywhere because
the logic lives in the platform-neutral `Guncon3.Core`. Use the official Microsoft SDK —
Homebrew's `dotnet@8` ships no `Microsoft.NET.Sdk.WindowsDesktop`.

## Running

```
Guncon3Console.exe                       the window and the tray icon
Guncon3Console.exe --console             the console mode, as before
Guncon3Console.exe keys                  print the keycode table and exit
Guncon3Console.exe dump [count] [file]   capture raw USB frames (default 500, packets.txt)
```

Only one copy runs per Windows user session: starting it again brings the running copy
to the front, from the tray too, and exits. A second `--console` or `dump` says so and
exits with code 1; `keys` is never blocked.

### The window

**F12** recalibrates every gun, **R** reloads the mapping files, **H** toggles the
linear and projective mapping. The toolbar, the tray menu and those keys do the same;
the keys need the window focused. The mode is not remembered across runs.

- **Status** — device, connection, which calibration, and whether each virtual device
  still accepts reports.
- **Log** — everything the app says, warnings amber, errors red, with Clear and Copy all.
- **Test** — one gun live: both calibrated aims as crosshairs (grey raw, white linear,
  cyan projective, the live one thicker), every button and stick direction, and what the
  three virtual devices were last told. It only watches, refreshes about thirty times a
  second while open, and stops when you leave the tab.
- **Mapping** — edits `mapping*.txt`; see below.
- **ESC** or the close button hides to the tray; double-clicking the icon brings it back.
  **File → Exit** or the tray's **Exit** releases every held button and ends the process,
  as does logging out.
- A start with no gun opens on the Log tab and offers **Search again** in the **File**
  and tray menus. Plug one in, press it, and the session carries on. No restart.

**File → Settings…** writes `settings.txt`: start minimised to the tray, run at Windows
logon (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), and write the log to
`guncon3.log`. It is `key=value`, `#` for comments, keys case-insensitive, unknown keys
left alone.

`--console` keeps the old behaviour: the log in the console window, **F12** / **R** /
**H** / **ESC** as console keys while it has focus, **Ctrl+C** and **Ctrl+Break** to
exit cleanly. The executable is a Windows application now, so it attaches to the
shell's console or opens one.

### The calibration window

Five targets in order — top-left, top-right, bottom-right, bottom-left, centre — then a
check phase where you aim freely: white is the mapping the game will get, grey dashed
the other. Accept with the one you prefer and it becomes the live mode.

| phase | gun | keyboard | does |
|---|---|---|---|
| shooting | Trigger | Space | capture the current target |
| any | A1 | Backspace | start the five shots over |
| check | A2 | H | switch the white crosshair between linear and projective |
| check | C2 | Enter | **save** and close |
| any | B1 / B2 | ← / → | move to the previous / next monitor (restarts the capture) |
| any | — | D | show the raw gun coordinates |
| any | — | ESC | cancel; the previous calibration stays |

Five shots that do not span a rectangle are thrown away with a message. A mouse click
does nothing here, because with two guns the other gun's mouse is live. The check phase
prints the projective centre error as a fraction of the screen; above 0.05 it is
suspect, so redo the capture.

## Button mapping

`mapping.txt` sits next to the executable, one `DEVICE.COMMAND = GUNCOMMAND` per line,
for example `KEYBOARD.30 = C1` or `MOUSE.Left = Trigger`. Gun 2 reads `mapping_2.txt`,
and so on. Names are case-insensitive, and every line that cannot be understood is
reported on startup with its line number.

Gun commands are the nine buttons `Trigger`, `A1`, `A2`, `B1`, `B2`, `C1`, `C2`,
`AClick`, `BClick`, plus the stick directions `LUp` / `LDown` / `LLeft` / `LRight` and
`RUp` / `RDown` / `RLeft` / `RRight`. Keyboard codes come from `Guncon3Console.exe keys`,
also in [docs/keycodes.txt](docs/keycodes.txt).

The **Mapping** tab edits the same files: pick one, set a keyboard code and a mouse
button per gun button, and **Save** writes it with its comment header kept and reloads
every gun at once. A yellow banner reports lines it could not understand, dropped on
save after it asks, and any keycode shared by two buttons, which the format allows.

## Configuration files

| file | written by | notes |
|---|---|---|
| `mapping.txt` | you | gun 2 reads `mapping_2.txt`, … |
| `calibration_rect.txt` | the calibration window | per gun; the five raw points, the derived rectangle and the monitor's position. Older files have no points and load on the linear mapping until you recalibrate |
| `stick_centre.txt` | measured automatically | per gun; delete it to re-measure |
| `settings.txt` | the Settings dialog | unknown keys are kept |
| `guncon3.log` | the app, when that setting is on | appended, never rotated |

A failure also writes `cal_capture_error.txt`, `cal_save_error.txt` or
`cal_read_error.txt` next to the executable and logs the path. Stick centres come from
the first 60 frames after startup, so **do not hold a stick while it starts**; delete
`stick_centre.txt` to measure again.

## Notes

- Recalibrate after changing monitor, resolution or arrangement. If the calibrated
  monitor is gone the app says so and aims screen-relative until you do.
- The aim is offset onto the calibrated monitor because Windows treats an absolute HID
  mouse as spanning the whole virtual desktop. On one monitor this changes nothing. It
  is a compile-time constant (`GunWorker.MapToVirtualDesktop`), not a setting.
- An uncalibrated gun does not move the cursor at all; its buttons still work.
- Licensed **GPL-2.0**, inherited from [sonik-br](https://github.com/sonik-br/GunconUSB).

---

© 2026 AnimaVitis · Fork of [sonik-br/GunconUSB](https://github.com/sonik-br/GunconUSB)
