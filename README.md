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
- 🪟 **A window and a tray icon** — one row per gun with its connection,
  calibration and feeder state, a live log, and every action as a button; the
  close button hides it to the notification area rather than exiting
- 🎯 **Hot recalibration** anytime with **F12**, the toolbar button or the tray menu
- 🧭 **Five-point calibration system** (four corners + center) that fits two aiming
  mappings from one capture — the original linear rectangle and a projective one
  that stays accurate when the gun is held off the screen's axis — switchable live
  with **H** to compare
- ✅ **Check before you keep it** — after the five shots the window shows both
  mappings as crosshairs at once; nothing is written until you accept, so a bad
  capture never replaces a good one
- 🖥️ **Multi-monitor** — the calibration window opens on the main window's monitor
  (in `--console` mode, on the console's monitor — the primary under Windows
  Terminal) and can be moved to any other; the aim follows the monitor it was
  calibrated on
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
  `stick_centre.txt`, `settings.txt`)
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
# self-contained — one exe, no runtime needed, ~160 MB
dotnet publish src/Guncon3Console/Guncon3Console.csproj -c Release -r win-x64 \
  --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# framework-dependent — ~0.3 MB, needs the .NET 8 Desktop Runtime
dotnet publish src/Guncon3Console/Guncon3Console.csproj -c Release -r win-x64 \
  --self-contained false -p:PublishSingleFile=true
```

CI builds this shape on every push and checks it is one file.

The framework-dependent build needs the **Desktop** Runtime, not the plain one —
the application is WinForms, so it requires `Microsoft.WindowsDesktop.App`.
Installing only the plain runtime gives a startup error naming a framework that
cannot be found.

---

## Running

```
Guncon3Console.exe                       the window and the tray icon
Guncon3Console.exe --console             the console mode, as before
Guncon3Console.exe keys                  print the keycode table and exit
Guncon3Console.exe dump [count] [file]   capture raw USB frames (default 500, to packets.txt) and exit
```

Only one copy runs at a time, per Windows user session. Starting the window again
— a second double-click, or the logon entry firing while it is already running —
brings the copy that is already running to the front (from the tray too) and
exits, rather than opening a second one to fight the first for the gun. A second
`--console` or `dump` start prints "GUNCON3 is already running." and exits with
code 1. `keys` only prints a table and is never blocked.

### The window

Starting with no argument opens the main window and puts an icon in the
notification area. The toolbar, the tray menu and the keys **F12** (recalibrate
every gun), **R** (reload the mapping files) and **H** (toggle every gun between
the linear and projective calibration mapping) all do the same three things;
the keys work while the **window** has focus, and the tray menu works from
anywhere. Calibration mode is not remembered across runs — every launch starts
on the linear mapping.

- **Status** shows one row per gun: the device, whether it is connected, which
  calibration it has, and whether each of the three virtual devices is still
  accepting reports.
- **Log** shows everything the application says, warnings in amber and errors in
  red, with **Clear** and **Copy all**.
- **Test** shows one gun live: where it is aiming under both calibrations, every
  button and stick direction the decoder sees, and what the mouse, keyboard and
  joystick the game reads were last told.
- **ESC** or the close button hides the window to the tray (the first time, a
  notification says so); double-clicking the tray icon brings it back.
- **Exit** — **File → Exit** or the tray's **Exit** — stops
  the guns, releases every held virtual button and ends the process. So does
  logging out of Windows.
- A start that finds no gun opens on the Log tab and says so, instead of
  disappearing — and offers **Search again** in the **File** menu (the tray menu
  has the same item). Plug a gun in, press it, and the session carries on as if
  it had started that way: the rows appear, the toolbar comes back and **F12**
  works. No restart.

The Test tab only watches. It sends nothing, changes nothing and writes no file —
what it shows is what the gun is doing anyway, so a game can be running while you
look. The aim picture is the calibrated screen with three crosshairs: grey is the
raw gun value placed on the screen (no calibration involved, so it is only a
rough guide), white is the linear mapping, cyan is the projective one, and the
one the game is being given is drawn thicker. The border turns red while the gun
cannot see the screen, and the box says so when there is no calibration, no
projective calibration, or no frame for the last half second. It refreshes about
thirty times a second while it is the tab you are on and the window is open;
switch tabs or hide to the tray and it stops completely, including the work the
gun threads were doing to feed it.

### Settings

**File → Settings…** writes `settings.txt` next to the executable:

| setting | effect |
|---|---|
| Start minimised to the tray | the next start shows only the tray icon |
| Run at Windows logon | adds `Guncon3` to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with the quoted exe path; unticking removes it |
| Write the log to `guncon3.log` | appends every line to `guncon3.log` next to the exe, from the moment it is ticked |

`settings.txt` is `key=value`, one per line, `#` for comments, keys
case-insensitive, and keys this version does not know are left alone:

```
StartMinimized=false
LogToFile=false
```

### The console mode

`--console` is exactly the behaviour of earlier versions: the header, the log in
the console window, and **F12** / **R** / **H** / **ESC** as console keys (the
console window must have focus). **Ctrl+C** and **Ctrl+Break** also exit and
release every held virtual button. Because the executable is now a Windows
application rather than a console one, it attaches to the console of the shell
that started it, or opens one of its own if there is none.

### The calibration window

Five targets, shot in order: top-left, top-right, bottom-right, bottom-left,
centre. Then a check phase where you aim freely and see where each mapping puts
you — white is the mapping the game will get, grey dashed is the other one. Accept
with the mapping you prefer and it becomes the live mode, exactly as **H** would.

| phase | gun | keyboard | does |
|---|---|---|---|
| shooting | Trigger | Space | capture the current target |
| any | A1 | Backspace | start the five shots over (in the check phase: discard and shoot again) |
| check | A2 | H | switch the white crosshair between linear and projective |
| check | C2 | Enter | **save** and close |
| any | B1 / B2 | ← / → | move the window to the previous / next monitor (restarts the capture) |
| any | — | D | show the raw gun coordinates |
| any | — | ESC | cancel; the previous calibration stays untouched |

Five shots that do not span a rectangle are thrown away with a message; shoot
again. A mouse click does nothing in this window — with two guns the other gun's
virtual mouse is live. If the gun disconnects while the window is open the window
says so; ESC cancels.

The check phase also prints the projective fit's centre error — how far the fifth
shot lands from the centre after mapping through the four corners, as a fraction
of the screen. Above 0.05 (5 %) it is flagged as suspect; redo the capture rather
than trust it.

---

## Button mapping

Button and stick mappings live in **`mapping.txt`**, next to the executable. Each
line follows the format

`DEVICE.COMMAND = GUNCOMMAND`

for example `KEYBOARD.30 = C1` or `MOUSE.Left = Trigger`. Gun 2 reads
`mapping_2.txt`, gun 3 `mapping_3.txt`, and so on.

### Editing it in the window

The **Mapping** tab edits the same files. Pick the file at the top, set a
keyboard code and/or a mouse button for each gun button, and **Save**: the file
is written with its comment header kept, and the mapping is reloaded for every
gun straight away — no restart. **Revert** re-reads the file, and **Open in
editor** opens it in whatever application Windows uses for `.txt`.

Two things the tab tells you in a yellow banner: lines it could not understand
(they are dropped when you save, and it asks first), and any keycode used by two
gun buttons at once — which the format allows and which is sometimes what you
want.

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

A keycode that is not in that table is reported on startup with its line number
and ignored.

The same list is kept in [docs/keycodes.txt](docs/keycodes.txt) for convenience.

---

## Configuration files

| file | written by | notes |
|---|---|---|
| `mapping.txt` | you | gun 2 reads `mapping_2.txt`, gun 3 `mapping_3.txt`, … |
| `calibration_rect.txt` | the calibration window | per gun (`calibration_rect_2.txt`, …); stores the five raw captured points plus the derived rectangle, so both mappings come from one capture, and the position of the monitor it was captured on; files from earlier versions have no points and still load, on the linear mapping and the primary monitor, until the gun is recalibrated |
| `stick_centre.txt` | measured automatically | per gun (`stick_centre_2.txt`, …); delete it to re-measure |
| `settings.txt` | the Settings dialog | `StartMinimized`, `LogToFile`; unknown keys are kept |
| `guncon3.log` | the app, when Write the log to a file is on | appended, never rotated; delete it when it gets big |

**Diagnostic files** (written next to the executable only when something fails):
`cal_capture_error.txt` and `cal_save_error.txt` carry the exception and the points
captured so far; `cal_read_error.txt` (written from the background read thread)
carries the exception only. The console prints the path when one is written.

Stick centres are measured from the first 60 frames after startup and printed to the
console. **Do not hold a stick while the app is starting** — the resting position it
measures is whatever the stick is doing at the time. If the printed values look
wrong, delete `stick_centre.txt` and it measures again on the next run.

---

## Notes
- Recalibrate after changing monitor, resolution or monitor arrangement — the
  calibration remembers where its monitor sat on the desktop. If that monitor is
  no longer there the app says so at startup and aims screen-relative, as older
  versions always did, until you recalibrate.
- Multi-monitor: the app assumes the virtual mouse driver spans the whole virtual
  desktop, which is how Windows treats an absolute HID mouse, and offsets the aim
  onto the calibrated monitor accordingly. On a single monitor this changes nothing.
  If aiming on a secondary monitor lands on the wrong screen, the driver maps to the
  primary only; report it; the mapping onto the virtual desktop is a compile-time
  constant (`GunWorker.MapToVirtualDesktop`), not a setting.
- An uncalibrated gun (calibration file missing or the window cancelled) does not
  move the cursor at all; its buttons still work. Calibrate with F12.
- All code and binaries licensed under **GPL-2.0**, inherited from the original work
  by [sonik-br](https://github.com/sonik-br/GunconUSB).

---

© 2026 AnimaVitis
Fork of [sonik-br/GunconUSB](https://github.com/sonik-br/GunconUSB)
