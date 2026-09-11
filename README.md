# PCRemote

Open-source starter project for controlling a Windows PC from Android over a local network.

## What it does

1. Android sends **Wake-on-LAN** to wake the PC.
2. The Windows host starts automatically with Windows.
3. Android connects to the Windows host.
4. The host sends periodic JPEG screenshots.
5. Touches on the Android screen are translated into Windows mouse actions.
6. A small text box can send keyboard text.

> Important: a phone cannot receive a live picture from a PC while the PC is completely powered off. Wake-on-LAN wakes the PC first; the live screen starts as soon as the Windows host is running.

## Security

This prototype is intended for a trusted LAN. It uses a random token in every request. **Do not expose the HTTP port directly to the Internet.** For remote access from outside your home, use a VPN such as Tailscale/WireGuard or add a properly configured TLS/reverse proxy.

## Windows requirements

- Windows 10/11
- .NET 8 SDK
- Enable Wake-on-LAN in BIOS/UEFI and the network adapter if you want the wake button.
- Build:
  `dotnet build windows/PCRemote.Host/PCRemote.Host.csproj -c Release`

Run:
`dotnet run --project windows/PCRemote.Host`

Set the host URL and token shown by the Windows program in the Android app.

## Android requirements

- Android Studio
- Android 8+
- Kotlin/Java support through Gradle
- Build with Android Studio or Gradle.

The Android side uses the Windows host's `/screen` endpoint for JPEG frames and `/mouse` + `/key` for control.

## Limitations of this starter

- Screenshot streaming is intentionally simple and not as efficient as RDP/WebRTC.
- It is LAN-oriented.
- It does not yet implement clipboard synchronization, audio, file transfer, multi-monitor selection, or hardware-accelerated video.
- Windows coordinate mapping assumes the screenshot dimensions match the desktop coordinate space.
