# Rabbid Lab

Rabbid Lab is a self-hosted homelab dashboard. It runs as a Windows service or in a Docker container and is used
through a web UI.

**Status:** version 1.0. Links, Sensors, Active Directory tools, Devices, roles and automatic updates are all in, and
new builds come out on the Dev channel first.

What changed in each version: [CHANGELOG.md](CHANGELOG.md).

## Features

- Sign-in with a local account or Active Directory, with roles that allow exactly the parts of Rabbid Lab you pick
- Devices: every computer with the Rabbid Lab client, installed by hand, with Group Policy or with Intune
- Active Directory: users, groups and computers, with views for what needs a look and CSV export
- Local accounts: the Windows accounts on the server and every device
- Links: the sites in your lab with their icons, IPs and whether they're up
- Sensors: checks on a timer (ping, port, web page, DNS, certificate, server hardware, storage, VMware, SNMP, Windows services, disk space) with email when something stays down
- Windows installer: service, HTTPS (self-signed or your own certificate), firewall rule, a Start menu and a desktop shortcut, silent install
- Docker image for x64 and ARM
- Automatic updates from GitHub Releases, verified with a release signature
- Activity log

## Installation

1. Download `RabbidLab-Setup-<version>.exe` from the [latest release](https://github.com/Rabbid85/rabbidlab/releases/latest).
2. Run it.
3. Open `https://localhost:8420`.

Running the installer again updates Rabbid Lab and keeps its settings. Uninstall from Settings > Apps.

### HTTPS

Rabbid Lab won't take a password over plain HTTP from another computer, so your password and session never cross the network unencrypted. Signing in on the server itself works either way.

- **Windows:** pick a certificate in Setup (self-signed or your own). If you picked "None (HTTP only)", run Setup again and pick one.
- **Docker:** the container serves plain HTTP on port 8420. Put a reverse proxy that does HTTPS in front of it (Caddy, nginx, Traefik) and set `Proxy__Trusted` to the proxy's address or network, such as `172.16.0.0/12`. Rabbid Lab only believes `X-Forwarded-Proto` from proxies listed there, and from the machine it runs on.
- **No HTTPS at all:** set `AllowPlainHttp` to `true` (in `console.json`, or as an environment variable). Passwords then cross the network unencrypted, so only do this on a network you trust completely. The sign-in page warns about it.

The client on each computer is locked to its server, so uninstall it from Devices before you remove the server. If the server is already gone, run [Tools/Remove-RabbidLabClient.ps1](Tools/Remove-RabbidLabClient.ps1) on the computer as administrator. It also takes `-ComputerName` for many at once, and works as a Group Policy startup script for the ones that are off.

## How releases are built

Releases are built from this repository on the maintainer's PC and uploaded here. Nothing is built on GitHub. The installers aren't code-signed yet, so Windows may warn the first time you run one. Each release does carry Rabbid Lab's own signature, and the built-in updater checks it before installing anything.

## Privacy

Rabbid Lab doesn't send data anywhere you haven't told it to. The one exception is the update check: it asks [GitHub](https://github.com/Rabbid85/rabbidlab/releases) for the latest release when it starts, every 12 hours, when you click **Check for updates**, and every 5 minutes while the sign-in page is open. Setup checks once when it opens. Nothing about you or your network is sent, only what any web request includes: your IP address and a `RabbidLab/<version>` user agent.

## License

[MIT](LICENSE)
