# PCRemote V2

Windows + Android remote-control project.

## Features
- Wake-on-LAN button
- Automatic LAN discovery of running PCRemote hosts
- MJPEG-style continuous screen stream (much smoother than repeated single screenshots)
- Fullscreen remote view
- Touch mouse: move, left click, right click, drag
- Scroll gestures
- Text/keyboard input
- Random authentication token
- Works through Tailscale/VPN when the PC is online
- Designed so the Windows host can start with Windows

## Important
Wake-on-LAN is a Layer-2 LAN feature. A phone outside your home network cannot normally wake a completely powered-off PC through Tailscale alone. For remote wake, use a device in the home LAN that can send WoL (router/NAS/another always-on computer) or configure your router for WoL.

Do NOT port-forward port 8765 directly to the Internet.

## Build Windows host

Install the .NET SDK. Then in PowerShell:

```powershell
cd windows/PCRemote.Host
dotnet build -c Release
dotnet run
```

The host prints its token and LAN addresses.

To publish a self-contained EXE:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

The EXE will be under `bin/Release/net8.0-windows/win-x64/publish/`.

## Windows Firewall

Allow the app/port on your private network. Run PowerShell as administrator:

```powershell
New-NetFirewallRule -DisplayName "PCRemote 8765" -Direction Inbound -Protocol TCP -LocalPort 8765 -Action Allow -Profile Private
```

## Start with Windows

After publishing the EXE, create a shortcut to it and place the shortcut in:

```text
shell:startup
```

For a production app, replace this with a proper Windows service/tray app.

## Android

Open the `android` folder in Android Studio and let Gradle sync. Android Studio is the official IDE for Android development.

Connect the phone by USB with Developer Options + USB debugging enabled, then Run.

Enter the Windows PC's LAN/Tailscale IP and the token. The Discover button can find running hosts on the same LAN.

## Tailscale

Install Tailscale on both devices and sign into the same tailnet. Use the PC's Tailscale 100.x address in the app when the PC is running.

This gives encrypted private networking without exposing port 8765 to the Internet.

## Production roadmap
For a polished TeamViewer-like application, the next major step is WebRTC/H.264 hardware encoding, clipboard/file transfer, multi-monitor selection, audio, input-method keyboard support, pairing QR codes, and a real Windows tray/service architecture.
