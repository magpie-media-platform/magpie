#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="$1"
rid="$2"
output="$3"

name="magpie-$version-$rid"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
app="$work/$name"

dotnet publish "$repo_root/src/Magpie.App/Magpie.App.csproj" -c Release -r "$rid" --self-contained true -p:Version="$version" -o "$app"

mkdir -p "$output"
tar -czf "$output/$name.tar.gz" -C "$app" .
