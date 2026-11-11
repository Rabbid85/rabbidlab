# Rabbid Lab

Rabbid Lab is a self-hosted homelab dashboard. It runs as a Windows service or in a Docker container and is used
through a web UI.

**Status:** version 1.0. Links, Sensors, Active Directory tools, Seats, roles and automatic updates are all in, and
new builds come out on the Dev channel first.

What changed in each version: [CHANGELOG.md](CHANGELOG.md).

## Features

- Sign-in with a local account or Active Directory, with roles that allow exactly the parts of Rabbid Lab you pick
- Seats: every computer with the Rabbid Lab client, installed by hand, with Group Policy or with Intune
- Active Directory: users, groups and computers, with views for what needs a look and CSV export
- Local accounts: the Windows accounts on the server and every seat
- Links: the sites in your lab with their icons, IPs and whether they're up
- Sensors: checks on a timer (ping, port, web page, DNS, certificate, server hardware, storage, VMware, SNMP, Windows services, disk space) with email when something stays down
- Windows installer: service, HTTPS (self-signed or your own certificate), firewall rule, Start menu and desktop shortcuts, silent install
- Docker image for x64 and ARM
- Automatic updates from GitHub Releases, verified with a release signature
- Activity log

## Installation

1. Download `RabbidLab-Setup-<version>.exe` from the [latest release](https://github.com/Rabbid85/rabbidlab/releases/latest).
2. Run it.
3. Open `https://localhost:8420`.

Running the installer again updates Rabbid Lab and keeps its settings. Uninstall from Settings > Apps.
