# Space Remote

Press one button on your phone, your PC powers on, and as soon as Windows is up you see its screen and control it by touch.

| Part | Tech | Folder |
|---|---|---|
| Windows app (tray, runs on the PC you control) | C# / .NET 8, WinForms, SendInput, GDI capture | `windows/SpaceRemote` |
| Windows viewer (runs on the PC you control *from*) | C# / .NET 8, WinForms | `windows/SpaceRemoteViewer` |
| Android app | Kotlin, Jetpack Compose | `android` |
| Build | GitHub Actions, no local setup needed | `.github/workflows/build.yml` |

## Building

Push this to a GitHub repo. Under **Actions → Build → Artifacts** you get:

- `SpaceRemote-Windows` → `SpaceRemote.exe` (single file, no .NET install required)
- `SpaceRemote-Viewer` → `SpaceRemoteViewer.exe` (control the PC from another PC)
- `SpaceRemote-Android` → `SpaceRemote.apk`

Push a tag like `v1.0.0` and a GitHub release with both files is created automatically.

## Setting up the PC (once)

1. Put `SpaceRemote.exe` somewhere permanent (e.g. `C:\Tools\SpaceRemote\`) and run it.
   SmartScreen will complain because the file isn't signed → "More info" → "Run anyway".
2. On first launch it shows your **address, port, password and MAC address**. Later: right-click the tray icon → "Show connection details".
3. **Firewall:** accept the Windows prompt for private networks, or use "Allow through firewall (admin)" in the tray menu.
   Your home network must be set to **Private** under Settings → Network → Properties.
4. **Autostart** is enabled automatically ("Start with Windows" in the tray menu).

### Auto sign-in, so you get a picture right away

Space Remote only starts once you're signed in to Windows. To get the screen without typing a password first:

1. `Win + R` → `netplwiz` → untick "Users must enter a user name and password" → confirm your password.
2. If the tickbox is missing: Settings → Accounts → Sign-in options → turn off "For improved security, only allow Windows Hello sign-in" and reopen `netplwiz`.

Note that anyone who powers the machine on can then use it.

## Using it away from home

You need **Tailscale**, a free VPN that works without port forwarding and without touching your router.

1. Install it on the PC from `tailscale.com/download` and sign in (a Google or GitHub login is enough).
2. Install the **Tailscale** app on your phone and sign in with the **same account**.
3. In the Windows app: tray → "Show connection details" → it now lists a **Tailscale address** starting with `100.`
4. Enter that address in Space Remote as the PC address. Port and password stay the same.
5. Tailscale has to be active on the phone when you connect.

The Tailscale address also works on your own Wi-Fi, so you can leave it set permanently.

### Powering on from outside

Wake-on-LAN only works inside the same network — a broadcast never reaches your home network from outside, and when the PC is off there's no Tailscale running on it either.

**Desktop PCs:** use a smart plug.

1. Plug the PC into the smart plug.
2. In the BIOS set **"Restore on AC Power Loss"** / "AC Back Function" to **Power On**, so the machine starts as soon as it gets power.
3. Switch the plug on from its own app, then tap "Start PC" in Space Remote.

With **Shelly** or **Tasmota** you can do it straight from Space Remote: put the switch URL in the **Wake URL** field and the app calls it before connecting.

- Shelly Plus/Pro: `http://<plug-ip>/relay/0?turn=on`
- Tasmota: `http://<plug-ip>/cm?cmnd=Power%20On`

Always shut the PC down through ⏻ in the app before switching the plug off, otherwise you're cutting power mid-run.

**Laptops:** a smart plug won't help, because a laptop has a battery and won't boot just because power arrives. Laptops also rarely support Wake-on-LAN from a full shutdown. Leave it running instead:

1. Keep it plugged into the charger.
2. Settings → System → Power: set "Make my device sleep after" to **Never** while plugged in.
3. Control Panel → Power Options → "Choose what closing the lid does" → **Do nothing** while plugged in. Now you can close it and it keeps running.
4. Enable **"Keep PC awake"** in the tray menu. That overrides sleep and screen lock for as long as Space Remote runs.

Idle draw is roughly 10-15 W. Don't leave it closed in a bag or under a blanket, it still needs airflow.

## Controlling it from another PC

`SpaceRemoteViewer.exe` is the desktop counterpart of the Android app. Run it on the *other* PC, no install needed.

It speaks the exact same protocol as every version of Space Remote on the host, so **nothing on the host PC has to be updated or reinstalled** for it to work.

1. Start `SpaceRemoteViewer.exe` and enter the same address, port and password as on the phone. At home use the home network IP; anywhere else install Tailscale on this PC too and use the `100.x.x.x` address.
2. Optional: MAC address and wake URL, which work exactly like on the phone.
3. Click **Connect**. If the host is off and a MAC is set, the viewer wakes it and waits.

Only one device can be connected at a time. Connecting from the viewer kicks the phone off, and vice versa.

| Input | What happens |
|---|---|
| Mouse move, click, wheel | Passed straight through |
| Keyboard | Passed through as real keys, including AltGr combinations on German layouts |
| System keys **On** | Win, Alt+Tab, Ctrl+Esc and Alt+F4 go to the remote PC too |
| System keys **Off** | Those stay on your local PC, everything else still goes remote |
| Ctrl+Alt+Enter | Fullscreen on/off (never forwarded). In fullscreen, touch the top edge to bring back the toolbar |
| Ctrl+Alt+Del | Always handled by your local Windows. Use Keys ▾ → Task Manager instead |
| Keys ▾ → Type my clipboard text | Types your local clipboard on the remote PC |

Your saved password is encrypted with Windows' own per-user protection (DPAPI).

Known limits: holding Ctrl or Shift while clicking (for multi-select) doesn't reach the remote PC, because the host's protocol sends modifiers together with a key, not on their own.

## Setting up the phone

1. Install `SpaceRemote.apk` (you'll need to allow installs from unknown sources).
2. Enter the address, port, password and — for Wake-on-LAN — the **MAC address of the wired adapter**.
3. On your home network the phone has to be on the **same Wi-Fi** as the PC. For anywhere else, see the Tailscale section above.

Tip: give the PC a fixed IP in your router so the address never changes.

## Controls

The screen behaves like a laptop touchpad by default: your finger moves the cursor relatively, so you can lift and reposition without the pointer jumping.

| Gesture | Action |
|---|---|
| Move one finger | Move the cursor |
| Tap | Left click |
| Double tap | Double click |
| Two-finger tap | Right click |
| Hold still, then move | Drag with the button held |
| Two fingers up/down | Scroll |
| Two fingers left/right | Scroll sideways |

Toolbar buttons:

| Button | Meaning |
|---|---|
| ⌨ | Show the built-in QWERTZ keyboard |
| ◍ / ✛ | Switch between touchpad mode and direct touch (cursor jumps to your finger) |
| Fn | Shortcut bar (Alt+Tab, Ctrl+C/V, F5, F11 …) |
| ⏻ | Shut down, restart, sleep, lock |
| ✕ | Disconnect |

### The keyboard

⌨ opens a real Windows keyboard inside the app, not the Android one. German QWERTZ layout with umlauts, and Ctrl, Alt, Win, Shift and Caps Lock that behave like physical keys.

- Tap **Ctrl**, then **C** → Ctrl+C. The modifier arms itself for one key and turns blue.
- Tap it **twice** → it locks (turns green) and stays down until you tap it a third time. Useful for things like Ctrl+Shift+Esc.
- **Caps** and **Shift** both switch the letters; Caps inverts what Shift does, same as on a real keyboard.

## Tuning

`%AppData%\SpaceRemote\config.json` (tray → "Open settings folder"), then restart Space Remote:

```json
{
  "Port": 47800,
  "Password": "ABCD2345",
  "MaxWidth": 1280,
  "JpegQuality": 60,
  "Fps": 25
}
```

Choppy? Drop `MaxWidth` to 960 or `JpegQuality` to 45. Too blurry? Raise `MaxWidth` to 1600-1920. Over mobile data the lower settings are worth it.

## Troubleshooting

- **"Wrong password"** — copy it exactly from "Show connection details".
- **Waits forever, PC never powers on** — check the Wake-on-LAN steps; use the MAC of the *wired* adapter, not Wi-Fi.
- **PC is on but won't connect** — check the firewall and that the network profile is Private. Is the tray icon running?
- **"No image — PC locked?"** — a normal app can't capture the lock screen or UAC prompts.
- **Clicks don't land in admin windows (e.g. Task Manager)** — Windows blocks that; run Space Remote as administrator.
- **Black screen in exclusive-fullscreen games** — switch the game to borderless windowed.

## Limits

- Primary monitor only, no audio.

## Security

The password never goes over the wire in the clear (HMAC-SHA256 challenge-response), but the **image stream is unencrypted**. That's fine on your own Wi-Fi, and over Tailscale the whole connection is encrypted on top.

Never forward the port in your router. An open port gets found by automated scanners within hours, and then a stranger is watching your screen. That's exactly why Tailscale is the right answer here: there's no open port, only your own signed-in devices get through.

---
Finanzinstitut
