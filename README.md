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
- Windows installer: service, HTTPS (self-signed or your own certificate), firewall rule, a Start menu and a desktop shortcut, silent install
- Docker image for x64 and ARM
- Automatic updates from GitHub Releases, verified with a release signature
- Activity log

## Installation

1. Download `RabbidLab-Setup-<version>.exe` from the [latest release](https://github.com/Rabbid85/rabbidlab/releases/latest).
2. Run it.
3. Open `https://localhost:8420`.

Running the installer again updates Rabbid Lab and keeps its settings. Uninstall from Settings > Apps.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

| Role | Members |
|---|---|
| Committers and reviewers | [Rabbid85](https://github.com/Rabbid85) |
| Approvers | [Rabbid85](https://github.com/Rabbid85) |

Releases are built by [GitHub Actions](.github/workflows/build.yml) from this repository. The installer, the uninstaller and Rabbid Lab's own files (`RabbidLab.Server.exe`, `RabbidLab.Server.dll`, `RabbidLab.Updater.dll`) are signed, and every signing request is approved by hand.

## Privacy

Rabbid Lab doesn't send data anywhere you haven't told it to. The one exception is the update check: it asks [GitHub](https://github.com/Rabbid85/rabbidlab/releases) for the latest release when it starts, every 12 hours, when you click **Check for updates**, and every 5 minutes while the sign-in page is open. Setup checks once when it opens. Nothing about you or your network is sent, only what any web request includes: your IP address and a `RabbidLab/<version>` user agent.

## License

[MIT](LICENSE)
