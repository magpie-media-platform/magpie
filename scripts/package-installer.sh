#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="$1"
app_archive="$2"
packages="$3"
output="$4"
shift 4

work="$(cygpath -m "$(mktemp -d)")"
trap 'rm -rf "$work"' EXIT

mkdir -p "$work/app" "$work/extensions"
tar -xzf "$(cygpath -u "$app_archive")" -C "$work/app"

{
    echo '<Include xmlns="http://wixtoolset.org/schemas/v4/wxs">'
    for id in "$@"; do
        package="$packages/$id.$version.nupkg"
        if [ "$(unzip -Z1 "$package" 'lib/**' | cut -d/ -f2 | sort -u | wc -l)" -ne 1 ]; then
            echo "$package does not have exactly one lib folder" >&2
            exit 1
        fi
        unzip -q "$package" 'lib/**' '*.nuspec' -d "$work/extensions/$id"
        echo "  <Feature Id=\"$id\" Title=\"$id\" AllowAdvertise=\"no\">"
        echo '    <Level Value="0" Condition="WIX_UPGRADE_DETECTED" />'
        (cd "$work/extensions" && find "$id" -type f) | while read -r file; do
            subdirectory="$(dirname "$file")"
            echo "    <Component Directory=\"EXTENSIONSFOLDER\" Subdirectory=\"${subdirectory//\//\\}\" Guid=\"\">"
            echo "      <File Source=\"$work/extensions/$file\" />"
            echo '    </Component>'
        done
        echo '  </Feature>'
    done
    echo '</Include>'
} > "$work/extensions.wxi"

dotnet build "$repo_root/src/Magpie.Installer/Magpie.Installer.wixproj" --no-incremental -c Release -o "$output" \
    -p:OutputName="magpie-$version-win-x64" \
    -p:ProductVersion="${version%%-*}" \
    -p:AppDir="$work/app" \
    -p:ExtensionFeatures="$work/extensions.wxi"
