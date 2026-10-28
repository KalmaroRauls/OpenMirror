# OpenMirror

![OpenMirror](assets/wordmark.png)

![platform: Windows](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6)
![license: MIT](https://img.shields.io/badge/license-MIT-blue)
![no phone-side app](https://img.shields.io/badge/phone--side%20app-none-brightgreen)
![no activation](https://img.shields.io/badge/activation-none-brightgreen)

Mirror and control an Android phone from a Windows PC with **nothing installed on the phone**: no
app, no root, no activation, no account, no vendor server anywhere in the path. It is a thin
Windows host around `adb` and [scrcpy](https://github.com/Genymobile/scrcpy), both fetched from
their official GitHub releases by the setup script.

**Free and open source, no donations wanted. Take it, change it, ship it, relicense it - it is MIT.**

## Why this exists

I wanted a free alternative to a desktop phone-mirroring app I had been using, and three things
about that one kept bothering me:

* **It ran slower than the hardware should.** Mirroring felt laggy and stayed laggy - the delay
  was not the phone and it was not my PC.
* **It needed a second app - on the phone.** The PC half was never enough on its own; every device
  had to have its own client installed before anything worked.
* **I could not tell what it was doing.** The backend was a black box that talked to servers I had
  no window into. When it misbehaved there was nothing to look at: no commands, no log, no reason
  given. It leaned on activation servers too, so an install that worked today could stop working
  tomorrow for a reason that had nothing to do with me.

OpenMirror goes the other way on all of it:

| What bothered me | What OpenMirror does instead |
|---|---|
| Slow | Hardware-accelerated decode (`d3d11va`), a direct adb transport, no extra processing layer. The engine is the same open-source one the rest of the ecosystem uses, so the ceiling is your hardware, not a vendor's pipeline |
| Second app on the phone | **Nothing** is installed on the phone. The capture payload is pushed over adb on every connect and cleaned up with the session |
| Opaque backend | Every adb command, every engine line and every error is printed in the window and mirrored to `bin\cli-transcript.txt`. There are CLI modes that verify each layer separately, so you can watch it work - and prove it does |
| Activation | None. No account, no licence key, no phone-home. The app's only network use is the one-time engine download; after that it speaks to your phone and nothing else |

It is deliberately a *thin* program: a WinForms host that runs `adb`, drives `scrcpy`, and reparents
the engine's own viewer window into its panel. No drivers, no kernel components, no shell
extensions, no background service, no telemetry.

## What it does

* **Mirror** any Android device over USB or Wi-Fi, with the phone's screen embedded in the app
  window - or popped out into its own window.
* **Control** it: mouse and keyboard pass into the phone, with `Ctrl`/`Alt`/`Shift` chords mapped
  through to the device.
* **Screen control**: blank the phone's panel while mirroring (**Screen off** / **Screen on** work
  live, without reconnecting), or keep it awake when you want that. The two are mutually exclusive
  by design - see the notes below for why.
* **Window modes**: docked panel, **Pop out mirror** into its own window, **Compact view**
  (`Ctrl+M`) to hide the control panel, **fullscreen** (`F11`, `Esc` to leave), always-on-top that
  applies immediately, and an automatic pop-out when you minimise so the phone never disappears
  along with the window.
* **Wireless in one dialog**: flip the phone to TCP/IP over USB once, auto-detect its Wi-Fi
  address, connect over the network, disconnect, go back to USB mode, and pair with the 6-digit
  code for Android 11+ wireless debugging.
* **Key profiles and a mapper**: named profiles, per-profile JSON you can edit, and a click-through
  overlay that draws your mapped controls over the mirror so you can line them up with a game's own
  on-screen buttons.
* **Screenshots** of the phone from the toolbar or from one command, straight via adb.
* **A log that explains itself**: device discovery, each adb call, engine startup, embedding, and
  any error, in plain language, as it happens.

## How it works

1. `setup-engine.ps1` downloads the [scrcpy](https://github.com/Genymobile/scrcpy) release built for
   your CPU (plus the `adb` that ships inside it) into `bin\engine` and verifies it. Nothing is
   vendored into this repository - third-party binaries are fetched from their own official
   releases, under their own licences.
2. `build.ps1` compiles `src\*.cs` with the .NET Framework compiler that is already on Windows. No
   SDK, no NuGet, no installer; it also bakes in the app icon.
3. On connect, OpenMirror asks `adb` for the device list, picks your phone, and starts `scrcpy`
   against it. The video the engine renders lands in a child window, which OpenMirror reparents into
   its own panel - so what you see is the engine's own unmodified output.
4. adb is the transport for everything else too: key events, text input, screenshots, the wireless
   handover, and the screen-state shortcuts. Nothing is injected into the phone outside that.

## Requirements

* **Windows 10 or 11, 64-bit.** ARM64 works too - the setup script picks the matching engine build.
* **.NET Framework 4.x** and **PowerShell 5.1** - both already on any normal Windows install.
* **An Android phone with USB debugging enabled** (Settings, Developer options). Wireless needs the
  phone and PC on the same network; Android 11+ adds wireless debugging with pairing codes.
* **Internet once**, for the engine download (~40 MB from GitHub releases). After that OpenMirror
  itself makes no network calls at all.

## Getting started

```powershell
git clone https://github.com/KalmaroRauls/OpenMirror.git
cd OpenMirror
.\setup-engine.ps1     # once: downloads scrcpy + adb into bin\engine and checks adb sees the phone
.\build.ps1            # compiles bin\OpenMirror.exe (no SDK needed)
.\bin\OpenMirror.exe   # start it
```

Enable **USB debugging** on the phone, plug it in, accept the *Allow USB debugging* prompt (tick
"always" so it only asks once), then hit **Refresh** and **Connect** in the window. The log tells
you what it found: serial, model, Android version, and anything that went wrong.

Nothing else to install, no service to run, no config to edit. Unplugging is fine - the app is a
normal window that starts when you start it.

## Using it

**Mirroring.** Pick your device, press **Connect**. The mirror appears in the panel with the phone's
real resolution and your hardware decoder. **Stop** ends the session and cleans up the payload.

**Screen off / keep awake.** *Phone screen off* blanks the panel while you keep using the mirror;
*Keep awake* holds it on. They cannot both be on - when you tick one the other switches itself off
and the log says why (Android's `stay_on_while_plugged_in` overrides a screen-off request, so the
combination would just silently do nothing). With a session running you can also use the live
**Screen off** / **Screen on** buttons, which are the reliable ones on phones with aggressive
always-on-display behaviour.

**Window modes.** *Pop out mirror* moves the mirror into its own window, which is what you want if
you like minimising OpenMirror but still watching the phone; minimising while docked does that
automatically, and restoring docks it back. *Compact view* (`Ctrl+M`) hides the side panel. `F11`
or the fullscreen button keeps only the phone screen; `Esc` leaves it.

**Wireless.** Open the **Wireless...** dialog once with the cable plugged in and press **1. Enable
TCP/IP (USB)**: it reads the phone's Wi-Fi address (preferring the real WLAN interface over a VPN),
switches the phone to TCP/IP mode, and then connects over the network itself. That is the one moment
the cable is needed, because switching adbd to TCP mode drops the USB transport with it. The address
is remembered, and a Wi-Fi transport is a connection adb has to be told to make rather than something
it discovers - so if it is dropped (a `tcpip` restart, or another adb on the machine killing the server
that held it) OpenMirror re-issues the connect by itself instead of losing the phone. There are buttons
to disconnect and to put the phone back into USB mode, plus Android 11+ pairing if you use the phone's
own wireless debugging. Note that TCP/IP mode does not survive a phone reboot - plug in once again and
repeat.

**Key mapping.** Load a profile, tick *Enable mapping*, and tick *Overlay* to see where your mapped
controls sit. The overlay is click-through and always on top, so you can drag it over the mirror
until it lines up with the game's own buttons. Profiles are plain JSON under `bin\profiles\`.

**Screenshots.** The camera button writes a PNG from the phone's own capture, no engine involved.

## Seeing what it does

This is the part that the app I left behind never gave me. The exe is a normal GUI, but it also has
CLI modes that exercise one layer at a time and print what they find - the same code paths the GUI
uses, just driven from the command line:

```powershell
.\run-cli.ps1                            # environment, device and engine report (--probe)
.\run-cli.ps1 -Mode selftest             # key-mapper and profile maths, no phone needed
.\run-cli.ps1 -Mode headless -Extra 6    # real 6-second capture with no window (pipeline proof)
.\run-cli.ps1 -Mode layout               # window/panel geometry, plus Wi-Fi address detection
.\run-cli.ps1 -Mode embedtest -Extra 4   # real mirroring, reparented into the real window
.\run-cli.ps1 -Mode screentest -Extra '--mode off'   # screen-off round trip and verdict
.\run-cli.ps1 -Mode screenshot -Extra C:\Users\me\Pictures\phone.png
```

Every mode also writes `bin\cli-transcript.txt`, so a report is a file you can read, diff or paste
into an issue. Add `--serial <device>` to target a specific phone.

## Troubleshooting and known limits

1. **No device in the list.** The phone has to be unlocked at least once, with USB debugging on and
   the *Allow USB debugging* prompt accepted. If the phone shows up as `unauthorized`, accept the
   prompt (or revoke the USB debugging authorisations and re-plug). Most phones need no extra
   Windows driver; if yours enumerates as an unknown device, install the vendor's USB driver.
2. **The list stays empty though the phone is unlocked, plugged in and authorised.** Something else
   reached the phone first: a second adb server - often left behind by a phone tool you have since
   removed - is holding its USB handle, and Windows hands that handle out once. If the server is only
   out of date OpenMirror takes the port over itself (the log says so). If it cannot - a server started
   elevated cannot be replaced without the rights - stop it yourself (reboot, or end the process from an
   elevated prompt) and press **Refresh**. The log names the port it was looking on.
3. **Screen-off on OnePlus/OxygenOS**: the engine confirms the panel is switched off, but the
   manufacturer's software may wake it on its own. Keep-awake is the usual culprit - and the UI
   will not let you enable both - so if the panel comes back, press **Screen off** again, which
   re-issues the request on the live session.
4. **adb behaves strangely or hangs.** Something else owns the default adb server - usually a second
   adb install on the same machine (Android Studio's `platform-tools`, or a phone tool that bundles
   one). The two then kill each other's server on port 5037 over and over, which looks like `adb server
   version (40) doesn't match this client (41)`, `protocol fault` or `connection reset`, and every one
   of those kills takes the connections you had established with it. OpenMirror self-heals onto its own
   server (see above), which no other tool touches, and re-connects the Wi-Fi transport by itself. To
   be rid of the culprit for good, uninstall that copy of platform-tools (or keep it off `PATH`),
   reboot, or stop the offending process from an elevated prompt.
5. **Wireless drops out, or the device list empties right after you enable TCP/IP.** Switching adbd to
   TCP mode restarts it, and that takes the USB transport - plus any Wi-Fi transport - with it, so for
   a moment the phone has no connection at all. **1. Enable TCP/IP (USB)** now waits for the phone to
   answer on TCP and attaches it again; if it is still missing, press **2. Connect over Wi-Fi** once
   and read the log: `not answering` means nothing is listening (TCP/IP mode does not survive a
   reboot), `connection reset` means the adb war described in the item above.
6. **Wireless stops working after a phone reboot.** TCP/IP mode is not persistent. Plug the cable in
   once, use **Wireless...** again, unplug.
7. **A build cannot replace `bin\OpenMirror.exe`.** Another process holds the file open; run
   `.\tools\WhoLocks.ps1 -Path ..\bin\OpenMirror.exe` and it will name it. OpenMirror itself can hold
   it if it is running, and so can a shell that has `LoadFile()`-ed the exe.
8. **Engine download fails.** `.\setup-engine.ps1 -Force` retries; `-ScrcpyVersion <x.y.z>` pins a
   different release; you can also drop the official zip into `bin\engine` yourself and re-run it.
9. **Headless mode ignores keep-awake/screen-off.** Deliberate: the engine refuses those flags when
   control is disabled, which is what makes headless a clean pipeline test.

More detail - including exactly what was verified on the machine this was written on, and how - is
in [docs/VERIFICATION.md](docs/VERIFICATION.md).

## What it will not do

* It will not install anything on the phone, and it will not root or unlock anything.
* It will not bypass an activation, licence or DRM check - there is none here, and it makes no
  attempt to touch one anywhere else.
* It will not capture protected video (streaming and banking apps black out on **any** capture path,
  by their own design).
* It will not hide from a game's anti-cheat. That is not what it is for - input from a PC is still
  input from a PC.
* It will never ask you for money, an account, a key, or your email address.

## Privacy

* No telemetry, no analytics, no crash reporting, no update check, no "phone home". Grep the source
  if you like - there is no HTTP client in it at all.
* The only outbound requests come from `setup-engine.ps1` (GitHub releases) and from the adb/scrcpy
  it downloads.
* Everything else is local: adb talks to your phone, the app prints what it did, and the transcript
  stays on your disk.

## Credits and licence

* [scrcpy](https://github.com/Genymobile/scrcpy) by Genymobile and contributors (**Apache-2.0**) -
  the mirroring engine. Downloaded by `setup-engine.ps1` from its official releases, not bundled
  here, so its licence stays its own.
* [Android platform-tools](https://developer.android.com/tools/releases/platform-tools) (Google) -
  `adb`, the thing that actually talks to your phone. Also downloaded, not bundled.
* OpenMirror itself: **MIT** (see [LICENSE](LICENSE)) - **no donations wanted**. Use it, fork it,
  sell it, relicense it; the only requirement is that the licence text comes along.

Issues and pull requests are welcome. There is no CLA to sign.
