#!/usr/bin/env bash
# What publish.sh and dev.sh both do: the release signature, release.json, and putting the files on the GitHub
# release. Sourced by each of them (they set $repo and run from the repository's root); not run on its own.

# sign_release <installer> <version> <product>
# Prints the release signature (base64), made with the offline release key on this PC and checked against the key built
# into Rabbid Lab. product is "Rabbid Lab" or "Rabbid Lab Dev": it is part of what's signed. Returns 1 when it can't;
# ReleaseSign's own words for why go to the screen.
sign_release() {
  local setup="$1" version="$2" product="$3" signature
  signature=$(dotnet run --project Tools/ReleaseSign -v q -- sign "$setup" "$version" "$product" | tail -1) || return 1
  dotnet run --project Tools/ReleaseSign -v q -- verify "$setup" "$version" "$signature" "$product" >/dev/null || return 1
  printf '%s\n' "$signature"
}

# write_manifest <release.json> <product> <version> <installer on disk> <installer's name on GitHub> <Docker zip's name on GitHub> <tag> <signature>
# Writes release.json, which Rabbid Lab and Setup read to find a newer version.
write_manifest() {
  local manifest="$1" product="$2" version="$3" setup="$4" file="$5" docker="$6" tag="$7" signature="$8" size sha
  size=$(stat -c %s "$setup")
  sha=$(sha256sum "$setup" | cut -d' ' -f1)
  cat > "$manifest" <<EOF
{
  "product": "$product",
  "version": "$version",
  "windows": {
    "version": "$version",
    "file": "$file",
    "size": $size,
    "sha256": "$sha",
    "url": "$repo/releases/download/$tag/$file",
    "signature": "$signature"
  },
  "docker": {
    "version": "$version",
    "file": "$docker",
    "url": "$repo/releases/download/$tag/$docker"
  }
}
EOF
}

# put_release <tag> <title> <notes> <prerelease: yes|no> <file>... <release.json>
# Creates the GitHub release with the files, or uploads them over the ones it has. A pre-release (the dev build) gets
# the new title and notes each time. release.json goes last, so it never describes an installer that isn't there yet.
put_release() {
  local tag="$1" title="$2" notes="$3" prerelease="$4"
  shift 4
  if gh release view "$tag" >/dev/null 2>&1; then
    if [ "$prerelease" = yes ]; then
      gh release edit "$tag" --prerelease --title "$title" --notes "$notes" >/dev/null
    fi
    gh release upload "$tag" "${@:1:$#-1}" --clobber
    gh release upload "$tag" "${@: -1}" --clobber
  elif [ "$prerelease" = yes ]; then
    gh release create "$tag" "$@" --verify-tag --prerelease --title "$title" --notes "$notes" >/dev/null
  else
    gh release create "$tag" "$@" --verify-tag --title "$title" --notes "$notes"
  fi
}
