#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

publish() {
    local name="$1"
    local output="$repo_root/publish/extensions/$(echo "$name" | tr '[:upper:]' '[:lower:]')"
    echo "Publishing $name -> $output"
    dotnet publish "$repo_root/src/Magpie.Extensions.$name" -c Release -o "$output"
}

if [[ -n "${1:-}" ]]; then
    publish "$1"
    exit 0
fi

skip=("IntegrationTestShims")

for name in "${skip[@]}"; do
    output="$repo_root/publish/extensions/$(echo "$name" | tr '[:upper:]' '[:lower:]')"
    if [[ -e "$output" || -L "$output" ]]; then
        echo "Removing $name test-only output -> $output"
        rm -rf -- "$output"
    fi
done

for csproj in "$repo_root"/src/Magpie.Extensions.*/*.csproj; do
    name="$(basename "$csproj" .csproj)"
    short="${name#Magpie.Extensions.}"
    if printf '%s\n' "${skip[@]}" | grep -qx "$short"; then
        echo "Skipping $short (test-only harness shim)"
        continue
    fi
    publish "$short"
done
