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
- Sensors: checks on a timer (ping, port, web page, DNS, certificate) with email when something stays down
- Windows installer: service, HTTPS (self-signed or your own certificate), firewall rule, Start menu and desktop shortcuts, silent install
- Docker image for x64 and ARM
- Automatic updates from GitHub Releases, verified with a release signature
- Activity log

## Installation

### Windows

1. Download `RabbidLab-Setup-<version>.exe` from the [latest release](https://github.com/Rabbid85/rabbidlab/releases/latest).
2. Run it. The service listens on port 8420 over HTTPS by default.
3. Open `https://localhost:8420`.

Running the installer again updates an existing installation and keeps its settings. Uninstall from Settings > Apps
or with `RabbidLab-Uninstall-<version>.exe`.

Silent install:

```
RabbidLab-Setup-<version>.exe /quiet [/port=8420] [/name=lab.home.lan] [/adddns] [/certsubject=...] [/http] [/nofirewall]
                              [/noredirect] [/dir=...] [/nostartmenu] [/desktop] [/nodefender] [/log=file]
RabbidLab-Setup-<version>.exe /uninstall /quiet [/everything]
RabbidLab-Uninstall-<version>.exe /quiet [/keepdata]
```

The same options can be placed in a `RabbidLab.xml` file next to the installer:

```xml
<RabbidLab>
  <Setup Quiet="yes" />
  <Install Folder="C:\Program Files\Rabbid Lab" StartMenu="yes" Desktop="no" />
  <Web Port="8420" Https="yes" Name="lab.home.lan" AddToDns="no" Firewall="yes" Redirect="yes" CertificateSubject="" />
</RabbidLab>
```

### Docker

```
docker compose up -d
```

Then open `http://<host>:8420`. Data is stored in the `rabbidlab` volume.

### From source

Requires the .NET 10 SDK.

```
dotnet run --project Src/RabbidLab.Server
```

## Configuration

| Setting | Default | Notes |
|---|---|---|
| Port | 8420 | Set by the installer, or `--urls` / `ASPNETCORE_URLS` when running from source |
| Data folder | `C:\ProgramData\Rabbid Lab` (Windows), `/data` (Docker), `/var/lib/rabbidlab` (Linux) | Override with `RABBIDLAB_DATA` |

## Updates

Rabbid Lab checks GitHub for a new release on startup, every 12 hours, and when you click **Check for updates**. While the
sign-in page is open it also checks when the page opens and every 5 minutes, and the page shows where updating stands.

- **Windows:** the new installer is downloaded and run automatically once the UI has been idle for two minutes. Turn that off with **Update automatically** under **Settings > Updates**, where **Install now** installs on demand and shows each step as it goes.
- **Docker:** the UI reports the new version. Update with `docker compose up -d --build`.

Releases are signed with an offline key. Rabbid Lab only installs updates whose signature verifies, so a compromised
GitHub account cannot push an update to installed copies. See `Src/Utilities/RabbidLab.Updater/Release.cs`.

## Sign-in

The first time Rabbid Lab is opened, it asks how people sign in:

- **Local account:** create an administrator account. Passwords are stored as salted PBKDF2 hashes in `accounts.json` in the data folder.
- **Active Directory:** pick the domain and a domain controller, or leave it on Automatic, then sign in with your own domain account. You become the Administrator, and everyone in the domain can sign in as a User until you change that under **Config > Roles and people**. Accounts from the other domains in its forest, and from domains it trusts, can sign in too: pick the domain under Change on the sign-in page. On Windows, sign-ins use Kerberos or NTLM with signing and sealing. In Docker, Rabbid Lab uses LDAPS, or plain LDAP when the domain controllers have no certificate.

Until setup is finished, anyone who can reach the port can do it, so finish it right after installing.

To sign in with a local account when Active Directory is set up, pick Local account on the sign-in page, or type `.\` before the username, as in Windows. Local accounts are Rabbid Lab's own, and on Windows also the computer's own accounts in its Administrators group.

### Roles

Roles live under **Config > Roles and people**. A role is a list of what it allows, part by part (see computers, let them in, sign people out, install updates, see the activity log, change roles, and so on), plus who's in it: Active Directory groups, single AD accounts, local accounts, or everyone in AD. People can be in several roles and get everything those roles allow.

Two roles are built in. **Administrator** is always at the top and allows everything. The Windows administrators of the server are always in it, and it always keeps at least one member. **User** is always at the bottom and everyone who signs in has it. An Active Directory account has to be in at least one role (User counts) to sign in at all.

Roles are in order, like Discord: drag them to reorder. Someone who can change roles only gets to change the ones below their own highest role, and can't give a role anything their own roles don't allow. Changes to what a role allows count straight away. A group added to a role counts the next time its members sign in.

Updating from 0.3 or earlier keeps everyone's access. The old admins group (or everyone, if there wasn't one) and local admins go in Administrator. The old users group (or everyone in the domain) goes in User. Roles are kept in `roles.json` in the data folder.

Rabbid Lab keeps the sign-ins itself, encrypted in `sessions.json` in the data folder, so restarting or updating signs nobody out.

If nobody can sign in, add a local administrator on the machine Rabbid Lab runs on. It prints a new password.

```
"C:\Program Files\Rabbid Lab\RabbidLab.Server.exe" --add-admin admin    (as administrator)
docker exec rabbidlab dotnet RabbidLab.Server.dll --add-admin admin
```

## Clients

The Rabbid Lab client puts a computer on the **Seats** page: whether it's online, who is signed in to Windows, its Windows and client version, and its address. It's a small Windows service with a tray icon, on .NET Framework 4.7.2, so Windows 10 and 11 need nothing else. All it needs is the server's address.

The easy way: open Rabbid Lab in a browser on the computer. If it isn't in Seats yet, the bar at the top offers the client. Install it, then click **Connect this computer**. The client opens with this server's address filled in, and Windows asks for an admin to OK it. The client's own window has **Connect to a server** too, for when it was installed without one.

For lots of computers, use **Config > Seats > Add computers**:

1. Install the client (`RabbidLab-Client-<version>.msi`, from the server or the release) by hand, or quietly:
   ```
   msiexec /i RabbidLab-Client-<version>.msi /qn SERVER=https://rabbidlab.example.com:8420 FINGERPRINT=<sha-256>
   ```
   `FINGERPRINT` is the SHA-256 fingerprint of the server's HTTPS certificate, shown in Add computers. You only need it when the computers don't already trust that certificate, such as Rabbid Lab's own self-signed one.
2. Or with **Group Policy**: assign the MSI under Computer Configuration > Policies > Software Settings > Software Installation, add the template (`RabbidLab-Client-Policy-<version>.zip`) to PolicyDefinitions, and set **Server address** (and **Server certificate fingerprint** if needed) under Administrative Templates > Rabbid Lab > Client. These win over what the installer was given. Intune takes the same MSI and command line.

New computers wait under Seats until someone with **Let new computers in** lets them in, unless **New computers wait for an admin** is turned off. Clients check in every minute and update themselves to the server's version.

## Active Directory

The **Active Directory** part of the rail has **Users**, **Groups** and **Computers**. Each has views down the side for what needs a look (locked out, expired and expiring passwords, inactive for 90 days, privileged accounts, empty groups, servers and workstations) and **Export** to CSV.

Rabbid Lab makes AD changes as its own account, not yours. The first time, click **Set it up** and sign in with an account that can make accounts, like a domain admin. Rabbid Lab uses that once to make `svc-rabbidlab` with a long random password nobody sees. It gives that account the rights Delegate Control gives to make, change and delete users, groups and computers, in the whole domain or one OU. Your sign-in isn't kept. Or pick **Use one I already have** and give it an account you made yourself.

After that you can make people and groups, set passwords, unlock, turn accounts and computers off and on, change who's in each group, and delete things. AD protects the accounts of admins itself (AdminSDHolder), so those are read-only here: change them in Active Directory Users and Computers. In Docker, setting passwords needs LDAPS.

## Local accounts

**Config > Local accounts** shows the Windows accounts on this server and on every computer in Seats. Make new ones, set passwords, turn them off, put them in groups like Administrators, or delete them. On a seat the client makes the change straight away: it keeps a connection open to the server for it, and only these account changes ever go down it. To stop them on some computers, set **Allow account changes** to Disabled in the Group Policy template.

Each of these has its own permissions in Roles, and every change goes in the activity log. Passwords never do: one Rabbid Lab makes is shown once and not kept.

## Wiki

**Help > Wiki** in the console has step-by-step guides for everything above, with links straight to the pages they talk about.

## Links

**Links** (under Lab) is a page for the sites in your lab: the router, the NAS, the hypervisor, the iDRACs. Paste an address and Rabbid Lab reads the site for its name, description and icon and looks up its IP. Change any of it, upload your own icon or show none, and put links in groups. Paste a whole list of addresses to add them in one go, and export the lot to a file to keep or to import into another Rabbid Lab. A dot on each one says whether it answers, and the cogwheel at the top of the page sets how often each link is checked and how long one has to be quiet before it shows as down. Links are kept in `links.json` in the data folder.

## Sensors

**Sensors** (under Lab) checks things on a timer and shows what's down: a machine that answers ping, an open port, a web page, a DNS name, or a certificate that's about to expire. See them as cards or as a grid with columns you can move and resize. Each sensor uses an alert rule (Critical, Normal, Relaxed or Quiet) that says how long it has to be down before Rabbid Lab emails you, so a blip doesn't. Email goes through Gmail with an app password, Microsoft 365 with an app registration, or any mail server that takes STARTTLS on port 587. Sensors are kept in `sensors.json`, their history in `sensor-state.json` and the mail settings in `mail.json`, all in the data folder.

## Security

Do not expose Rabbid Lab to the internet.

## Building

| Command | Result |
|---|---|
| `powershell -File Installer\build.ps1` | Installer and uninstaller in `artifacts\` (unsigned, for testing) |
| `dotnet test Tests/RabbidLab.Tests` | Runs the tests |
| `bash Installer/publish.sh` | Tags the version on `main`; GitHub Actions builds and signs it; then adds the release signature and creates the GitHub release (requires `gh`) |
| `bash Installer/dev.sh` | Builds the dev edition from `dev` and publishes it as the `dev-latest` pre-release |

The dev edition (`build.ps1 -Dev`) installs alongside the release with its own service, data folder and port (8421).

Work goes to the `dev` branch first. Each dev build gets a new version and goes out with `dev.sh`, and Rabbid Lab Dev
updates to it. To release, merge `dev` into `main` and run `publish.sh`. The release notes cover every version since
the last release.

Release signing requires the signing key. `dotnet run --project Tools/ReleaseSign -- init --replace-trusted-key`
generates one and prints the public key for `Release.cs` and `SetupUpdate.cs`. `export` and `import` back it up and
restore it.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

| Role | Members |
|---|---|
| Committers and reviewers | [Rabbid85](https://github.com/Rabbid85) |
| Approvers | [Rabbid85](https://github.com/Rabbid85) |

Signed files are built by [GitHub Actions](.github/workflows/build.yml) from this repository: the installer, the uninstaller, and Rabbid Lab's own program files (`RabbidLab.Server.exe`, `RabbidLab.Server.dll`, `RabbidLab.Updater.dll`). Every signing request is approved by hand.

## Privacy

Rabbid Lab sends no information to other networked systems unless the person installing or using it asks it to, with one exception: it checks [github.com](https://github.com/Rabbid85/rabbidlab/releases) for a newer release on startup, every 12 hours, when **Check for updates** is selected, and while the sign-in page is open (when it opens and every 5 minutes). Setup makes the same check when it opens. These requests download the newest release's `release.json` and carry nothing about you or your network beyond what every web request carries (your IP address and the user agent `RabbidLab/<version>`).

## License

[MIT](LICENSE)
