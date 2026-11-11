#!/usr/bin/env bash
# Builds Rabbid Lab Dev and its uninstaller (build.ps1 -Dev) and its Docker download (a zip of the dev branch with
# docker-compose.dev.yml, which Compose builds on the Docker host beside the release), signs the installer, and makes
# them the "Rabbid Lab Dev" pre-release on GitHub (tag dev-latest), under fixed file names so each upload replaces the
# last; the version and commit are in the title and notes. Rabbid Lab Dev installs update to it. Only the newest dev
# build stays in artifacts\.
#   bash Installer/dev.sh              build, then upload
#   bash Installer/dev.sh --no-build   upload what artifacts\ already has for this version
set -euo pipefail
cd "$(dirname "$0")/.."
. Installer/release-lib.sh

repo="https://github.com/Rabbid85/rabbidlab"
[ "$(git rev-parse --abbrev-ref HEAD)" = dev ] || { echo "Dev builds are made from the dev branch: git checkout dev first."; exit 1; }
# The installer is built from the files here and the Docker download from the commit: they have to be the same.
[ -z "$(git status --porcelain)" ] || { echo "Commit or put away the changes first: the dev build is what the dev branch holds."; exit 1; }
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Src/RabbidLab.Server/RabbidLab.Server.csproj | head -1)
setup="artifacts/RabbidLab-Dev-Setup-$version.exe"
remover="artifacts/RabbidLab-Dev-Uninstall-$version.exe"
docker="artifacts/RabbidLab-Dev-Docker-$version.zip"

if [ "${1:-}" != "--no-build" ]; then
  # Every dev build has a version of its own: <Version> is raised with each change, and a version already built isn't
  # built again (--no-build uploads it again).
  built=$(gh release view dev-latest --json name --jq .name 2>/dev/null | sed -n 's/^Rabbid Lab Dev \([0-9.]*\) .*/\1/p' || true)
  [ "$built" != "$version" ] || { echo "Rabbid Lab Dev $version is already built: raise <Version> in Src/RabbidLab.Server/RabbidLab.Server.csproj first."; exit 1; }
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File Installer/build.ps1 -Dev -Release
  git archive --format=zip --prefix=RabbidLab-Dev/ -o "$docker" HEAD Dockerfile docker-compose.dev.yml .dockerignore README.md Src/RabbidLab.Server Src/Utilities
fi
for f in "$setup" "$remover" "$docker"; do
  [ -f "$f" ] || { echo "$f is missing: build it first (bash Installer/dev.sh)."; exit 1; }
done

# The release signature, made here with the offline release key, as for a release: Rabbid Lab Dev installs no update
# without it. Signed as "Rabbid Lab Dev", so a dev build never passes for a release.
signature=$(sign_release "$setup" "$version" "Rabbid Lab Dev") \
  || { echo "The dev build could not be signed with the release key, so nothing was uploaded."; exit 1; }

# On GitHub under fixed names, so each upload replaces the last.
files=$(mktemp -d)
trap 'rm -rf "$files"' EXIT
cp "$setup" "$files/RabbidLab-Dev-Setup.exe"
cp "$remover" "$files/RabbidLab-Dev-Uninstall.exe"
cp "$docker" "$files/RabbidLab-Dev-Docker.zip"
write_manifest "$files/release.json" "Rabbid Lab Dev" "$version" "$setup" "RabbidLab-Dev-Setup.exe" "RabbidLab-Dev-Docker.zip" dev-latest "$signature"
commit=$(git rev-parse --short HEAD)
title="Rabbid Lab Dev $version ($commit)"
notes="The newest dev build: $(git rev-parse --abbrev-ref HEAD) at $commit. Installs beside Rabbid Lab as Rabbid Lab Dev (port 8421); on Docker, docker compose -f docker-compose.dev.yml up -d."

git tag -f dev-latest >/dev/null
git push -q -f origin refs/tags/dev-latest
put_release dev-latest "$title" "$notes" yes "$files/RabbidLab-Dev-Setup.exe" "$files/RabbidLab-Dev-Uninstall.exe" "$files/RabbidLab-Dev-Docker.zip" "$files/release.json"
echo "Rabbid Lab Dev $version ($commit) is on GitHub: $(gh release view dev-latest --json url --jq .url)"

# Only the newest dev build stays in artifacts\.
for f in artifacts/RabbidLab-Dev-Setup-[0-9]*.exe; do
  [ "$f" = "$setup" ] || rm -f "$f" "${f/-Setup-/-Uninstall-}"
done
for f in artifacts/RabbidLab-Dev-Docker-[0-9]*.zip; do
  [ "$f" = "$docker" ] || rm -f "$f"
done
