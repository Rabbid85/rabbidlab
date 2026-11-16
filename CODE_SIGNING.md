# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## What gets signed

Releases only, built by GitHub Actions from this repository ([build.yml](.github/workflows/build.yml)) from a version tag:

- the Rabbid Lab console (`RabbidLab.Server.exe` and its own DLLs)
- the client (`RabbidLab-Client.exe`) and its installer (`RabbidLab-Client.msi`)
- the installer and uninstaller (`RabbidLab-Setup-<version>.exe`, `RabbidLab-Uninstall-<version>.exe`)

Dev builds (the `dev-latest` pre-release) are not signed.

## Team roles

- Committers and reviewers: [Rabbid85](https://github.com/Rabbid85)
- Approvers: [Rabbid85](https://github.com/Rabbid85)

Every signing request is approved by hand before it's signed.

## Privacy

Rabbid Lab only talks to other computers when the person who installs or runs it sets it up to:

- It asks GitHub for new releases, to offer updates. Update automatically can be turned off in Settings, Updates.
- The client connects only to the Rabbid Lab server it's given when it's installed.
- Links, sensors, email and chat notifications reach only the addresses an admin adds.

Nothing is sent to the project or anyone else.
