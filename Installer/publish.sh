#!/usr/bin/env bash
# Releases Rabbid Lab on GitHub:
#   1. tags the version (v1.2.3) and pushes the tag,
#   2. builds the installer and uninstaller: here, or once SignPath is set up, by starting the Build workflow
#      (.github/workflows/build.yml) for the tag, which builds on GitHub and has them code-signed, then downloading them,
#   3. adds the release signature (Tools/ReleaseSign, with the release key on this PC) and writes release.json,
#   4. makes the Docker download and creates the GitHub release with all of it attached.
#   bash Installer/publish.sh            release what main holds: built on GitHub once SignPath signs there, else here
#   bash Installer/publish.sh --local    build here anyway (unsigned)
#   bash Installer/publish.sh --github   build on GitHub anyway
# Needs gh, signed in (gh auth login).
set -euo pipefail
cd "$(dirname "$0")/.."
. Installer/release-lib.sh

repo="https://github.com/Rabbid85/rabbidlab"
[ "$(git rev-parse --abbrev-ref HEAD)" = main ] || { echo "Releases are made from main: merge dev into main first."; exit 1; }
[ -z "$(git status --porcelain)" ] || { echo "Commit or put away the changes first: the release is what main holds."; exit 1; }

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Src/RabbidLab.Server/RabbidLab.Server.csproj | head -1)
# The release page says what CHANGELOG.md says for every version since the last release (dev builds in between).
last=$(git tag -l 'v*' --sort=-v:refname | grep -vx "v$version" | head -1 | sed 's/^v//')
changes=$(awk -v v="$version" -v last="$last" '
  $1 == "##" { if ($2 == v) on = 1; else if ($2 == last) on = 0; if (on) { out = out (out ? "\n" : "") "### " $2 "\n"; n++ } next }
  on && NF { out = out $0 "\n" }
  END { if (n == 1) sub(/^### [^\n]*\n/, "", out); printf "%s", out }' CHANGELOG.md)
[ -n "$changes" ] || { echo "CHANGELOG.md has nothing for $version yet: add it first."; exit 1; }
tag="v$version"
setup="artifacts/RabbidLab-Setup-$version.exe"
remover="artifacts/RabbidLab-Uninstall-$version.exe"
client="artifacts/RabbidLab-Client-$version.msi"
policy="artifacts/RabbidLab-Client-Policy-$version.zip"
docker="artifacts/RabbidLab-Docker-$version.zip"
manifest="artifacts/release.json"
mkdir -p artifacts

git rev-parse -q --verify "refs/tags/$tag" >/dev/null || git tag "$tag"
git push -q origin "refs/tags/$tag"

# GitHub's build only adds something when SignPath signs it there (its secret and variable are set; SignPath only signs
# what GitHub built from this repo). Until then the same unsigned files are built here, in a few minutes, not ten.
how="${1:-}"
if [ -z "$how" ]; then
  if gh secret list 2>/dev/null | grep -q '^SIGNPATH_API_TOKEN' && gh variable list 2>/dev/null | grep -q '^SIGNPATH_ORGANIZATION_ID'; then
    how=--github
  else
    how=--local
    echo "SignPath isn't set up on GitHub yet, so this builds here (unsigned, as GitHub's would be)."
  fi
fi
if [ "$how" = "--local" ]; then
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File Installer/build.ps1 -Release
else
  # The workflow only runs when started (a tag alone doesn't start it), so it's started here for this tag.
  gh workflow run build.yml --ref "$tag"
  echo "Waiting for the Build workflow for $tag..."
  run=""
  for _ in $(seq 1 30); do
    run=$(gh run list --workflow build.yml --json databaseId,headBranch --jq ".[] | select(.headBranch == \"$tag\") | .databaseId" | head -1)
    [ -n "$run" ] && break
    sleep 10
  done
  [ -n "$run" ] || { echo "No Build workflow started for $tag."; exit 1; }
  gh run watch "$run" --exit-status
  rm -f "$setup" "$remover" "$client" "$policy"
  gh run download "$run" --name "RabbidLab-$version" --dir artifacts
fi
for f in "$setup" "$remover" "$client" "$policy"; do
  [ -f "$f" ] || { echo "$f is missing, so nothing was published."; exit 1; }
done

# What the Dockerfile builds: Rabbid Lab and the update engine it's built with.
git archive --format=zip --prefix=RabbidLab/ -o "$docker" "$tag" Dockerfile docker-compose.yml .dockerignore README.md Src/RabbidLab.Server Src/Utilities

# The release signature, made here with the offline release key: Rabbid Lab installs no update without it.
signature=$(sign_release "$setup" "$version" "Rabbid Lab") \
  || { echo "The installer could not be signed with the release key, so nothing was published."; exit 1; }
write_manifest "$manifest" "Rabbid Lab" "$version" "$setup" "RabbidLab-Setup-$version.exe" "RabbidLab-Docker-$version.zip" "$tag" "$signature"

notes="$changes

[Changelog]($repo/blob/main/CHANGELOG.md) · [Code signing policy]($repo#code-signing-policy)"
put_release "$tag" "Rabbid Lab $version" "$notes" no "$setup" "$remover" "$client" "$policy" "$docker" "$manifest"
echo "Released Rabbid Lab $version: $repo/releases/tag/$tag"
