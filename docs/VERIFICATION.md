# Developer and verification notes

Working notes on how OpenMirror was built and what was verified, kept out of the README. For what
the app is and how to use it, read [../README.md](../README.md).

The design rule behind all of it: **no app installed on the phone, no root, no activation, no vendor
account and no vendor server anywhere in the path** - only `adb` plus a shell-privileged capture
payload that is pushed fresh on every connect.

## Where things live

| What | Path |
|---|---|
| Program | `bin\OpenMirror.exe` |
| Source | `src\*.cs` |
| Engine (scrcpy 5.0.1 + its own adb) | `bin\engine\scrcpy-win64-v5.0.1\` |
| Key profiles | `bin\profiles\*.json` |
| Screenshots | `bin\screenshots\` |
| Icon and logo | `assets\icon.ico`, `logo.png`, `wordmark.png`, `sizes.png` |
| Icon source | `tools\IconGen.cs`, regenerate with `.\tools\MakeIcon.ps1` |
| Build helper | `tools\WhoLocks.ps1` - names whatever is holding the exe if a build cannot replace it |
| CLI output logs | `bin\cli-transcript.txt` |

Make a desktop shortcut to `bin\OpenMirror.exe`. If you move the folder, keep `bin\engine`
next to the exe (or set `OPENMIRROR_ENGINE` to the engine folder).

## Quick start

```powershell
cd OpenMirror
.\setup-engine.ps1        # downloads/verifies the engine (idempotent; reuses a cached zip)
.\build.ps1               # compiles src -> bin\OpenMirror.exe (uses the stock .NET compiler)
.\bin\OpenMirror.exe      # the window
```

Then pick your device in **Device** and press **Connect**. The mirror appears inside the window
(uncheck **Embed** to use the engine's own resizable window instead).

## What it does today

- **Device management** - USB and Wi-Fi. **Wireless...** walks you through it: `adb tcpip` (USB
  needed once), auto-detects the phone's Wi-Fi address, `adb connect`, `adb pair` for Android 11+
  wireless debugging, disconnect, and "back to USB mode". The phone and PC only need to be on the
  same network.
- **Screen control** - tick **Phone screen off** to blank the panel while mirroring (it
  automatically switches **Keep awake** off, because Android's `stay_on_while_plugged_in` would
  force the panel straight back on). **Screen off** / **Screen on** buttons do it live, on a
  running mirror, through the engine's own shortcut, so you never have to reconnect.
- **Window modes** - **Pop out mirror** moves the mirror into its own window (so OpenMirror can be
  minimised and you still see the phone); **Compact view** (`Ctrl+M`) hides the control panel;
  **Fullscreen** (`F11`, `Esc` to leave) keeps only the phone screen; **Always on top** now applies
  immediately. Minimising while the mirror is docked pops it out automatically and docks it again
  when the window comes back.

- **Mirroring** - resolution cap, bitrate, audio on/off, keep-awake, phone-screen-off,
  always-on-top, embedded or free-floating window.
- **Input** - full keyboard/mouse passthrough from the engine (click, drag, scroll, text,
  clipboard sync) because the engine injects real Android input events.
- **Screenshots** - one press, saved to `bin\screenshots\`, works even with no mirror running.
- **Key mapping (the Wormhole "game mode" feature)** - a global keyboard hook turns keys into
  synthetic touch input on the mirror window:
  - `Stick` - WASD-style multi-key analogue drag (left stick),
  - `Hold` - press-and-hold at a point (aim/ADS),
  - `Tap` - momentary tap (fire),
  - `F8` toggles mapping on/off; **Show mapping overlay** draws click-through widgets over the
    mirror so you can line them up with the game's own buttons,
  - profiles are plain JSON in `bin\profiles\`, editable by hand.
- **Run checks** - engine, payload, adb, device, model, Android version, screen size, and
  whether the old `com.viper.wormhole` app is still installed.

## Command line (no GUI, scriptable)

The exe is a GUI binary, so use the wrapper; it waits properly and prints the transcript:

```powershell
.\run-cli.ps1                        # environment + device report (same as -Mode probe)
.\run-cli.ps1 -Mode selftest         # profile/mapper maths self test, no device needed
.\run-cli.ps1 -Mode layout           # window layout report
.\run-cli.ps1 -Mode headless -Extra 6               # mirror 6s, record, no window
.\run-cli.ps1 -Mode embedtest -Extra 4              # real mirror reparented into the real window
.\run-cli.ps1 -Mode screentest -Extra '--mode off'  # proves screen-off; use awake to see the conflict
.\run-cli.ps1 -Mode screenshot -Extra C:\Users\me\Pictures\phone.png
```

Verified on the development machine (a OnePlus 12 running Android 16):

```
engine version  : scrcpy 5.0.1
server payload  : present
adb version     : Android Debug Bridge version 1.0.41
devices         : 1   <model> [usb | <serial> | device]   android=16 sdk=36
verdict: ready to mirror
SELFTEST PASS
```

## How it works

```
Windows: OpenMirror.exe (WinForms)
  |  adb push scrcpy-server -> /data/local/tmp
  |  app_process + CLASSPATH launch as the shell user (uid 2000)
  |  video / audio / control sockets over adb
  v
Phone: shell-privileged capture process -> H.264 -> local decode + render
```

Nothing is installed into the phone's app list, nothing persists after you unplug, and no
component of the old product is required or reused.

## Known limits (honest list)

1. **One touch pointer at a time (v1 mapper).** The mapping layer injects Windows mouse input,
   which the engine maps onto a *single* touch pointer. True simultaneous multi-touch (moving
   the stick while dragging to aim) requires speaking the engine's control protocol directly -
   that is the top roadmap item, and this build does not pretend to do it.
2. **Strict games / anti-cheat** may reject synthetic input. The engine also supports real HID
   modes (`--keyboard=uhid`, `--mouse=uhid`) which Android sees as a physical keyboard/mouse;
   wire them through `Engine.BuildArgs` if you hit this.
3. Audio forwarding needs Android 11+ (fine on this phone, not on older devices).
4. The overlay is for calibration only; it does not yet let you drag widgets with the mouse.
   Edit the JSON profile instead - positions are fractions of the window (`X`/`Y` 0..1).
5. An `unauthorized` device means the USB-debugging prompt was not accepted (or the RSA key
   changed); re-accept it on the phone.
6. Headless mode disables `--stay-awake`/`--turn-screen-off` on purpose: the engine refuses
   those when control is disabled.
7. **Screen-off on OnePlus/OxygenOS**: the engine confirms the panel is switched off, but the OEM
   may wake it again on its own. Keep-awake is the classic culprit (the UI no longer allows the
   two together) - if the panel still comes back, press **Screen off** again, which re-issues the
   request live.
8. **A wedged foreign adb server.** If another program's old adb holds port 5037 (here: the
   abandoned Wormhole service's adb, version 40, running elevated), every adb call hangs.
   OpenMirror detects that and moves itself - and the viewer - to its own server on port 5057
   (`ADB_SERVER_SOCKET`), so mirroring keeps working: verified on this machine with the broken
   server still running. Killing that foreign adb once and for all needs an elevated command:
   `Stop-Process -Id <pid> -Force` as administrator, or a reboot.


## Roadmap

1. **Multi-touch via the control protocol** - our own client on the engine's video/control
   sockets, so mapping can hold a stick *and* aim *and* fire. This is what makes OpenMirror a
   true Wormhole peer.
2. **Own device-side payload** - compile our own server (Java -> dex, run via `app_process`) so
   not even the engine's server jar is borrowed, then add Wormhole-era features: per-app
   profiles, scripted tap sequences, macro recording.
3. **Optional interop with the old APK** - the installed `com.viper.wormhole` app speaks a
   local-socket JSON IPC protocol (`ipc.core.impl.SocketSession/SocketHeart/SocketReader/
   SocketWriter`, `ClientSocketSession`, `KEY_TOKEN`, `KEY_SESSION_TOKEN2_BUNDLE`,
   `DEVICE_ID`). A future *Wormhole APK backend* could drive that app when its exact rendering
   path is wanted. Deliberately not a priority: it inherits the vendor's activation dependency,
   which is the thing we are escaping.
4. Engine auto-update check (GitHub releases) and a settings file that remembers your last
   device and options.

## Notes on the original product (from the RE work)

- `Wormhole.exe` drives adb the same way this app does: `CLASSPATH=%1`, `app_process`,
  `/data/local/tmp/kill_copy.sh`, `localabstract:%1`, `forward`/`reverse`/`shell`/`install`,
  `dumpsys package com.viper.wormhole | grep versionCode`, `am start -n`.
- The phone app (`com.viper.wormhole`, versionCode 200, targetSdk 29) does not survive
  Android-16 hardening: 11 hidden-API denials at startup, and
  `WormholeInputService.onCreate` builds a `Dialog` which NPEs inside OxygenOS
  `OplusDarkModeManager.needForceLightNavbarColor` (null `IOplusActivityManager`), producing a
  roughly 1 Hz `com.viper.wormhole:x` crash loop *while the desktop client is connected*.
  Expect the old product to keep degrading; this replacement has no such failure mode.
- **Boundary:** OpenMirror is not - and will not become - an activation or licence bypass for
  the commercial product. It is an independent implementation that needs no activation at all.

## Build details

- No .NET SDK needed: `build.ps1` compiles with
  `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` at language level C# 5.
  9 source files, ~308 KB exe, zero NuGet packages, zero installs.
- JSON via `System.Web.Extensions` (`JavaScriptSerializer`); P/Invoke for window embedding,
  synthetic input and the low-level keyboard hook.
- Source map: `Native.cs` (Win32), `Adb.cs` (device transport + server-port self-healing),
  `Engine.cs` (payload + viewer process + embedding), `KeyMapper.cs` (profiles, hook, touch
  injection), `OverlayForm.cs` (calibration widgets), `MainForm.cs` (UI), `WirelessForm.cs`
  (wireless helper), `AppIcon.cs` (window icon), `Program.cs` (entry + CLI modes).
- Icon and logo: the mark is **drawn**, not imported - `tools\IconGen.cs` builds the blue ring
  whose top-right fragment has swung out dark (`Ring` and `Lift` are the two colours, `IconSizes`
  the size list), then `.\tools\MakeIcon.ps1` writes `assets\icon.ico` (16-256 px, DIB for the
  small entries and PNG for the large ones), `logo.png`, `wordmark.png` and the `sizes.png`
  check sheet. `build.ps1` picks the .ico up automatically (`/win32icon` for Explorer and the
  taskbar, `/resource:...app.ico` for the windows), and falls back to a plain exe if it is missing.
  `build.ps1` compiles to `bin\OpenMirror.staging.exe` and swaps the result into place, retrying a
  few times - a scanner, indexer or another shell can hold the previous exe open for a moment, and
  `.\tools\WhoLocks.ps1 -Path ..\bin\OpenMirror.exe` names the culprit (it uses the Restart Manager,
  so it also sees services). Never `LoadFile()` the exe in a shell you keep open: that maps the file
  and blocks every later build.
