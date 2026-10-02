#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="$1"
output="$2"
shift 2

for project in "$@"; do
    dotnet pack "$repo_root/src/$project/$project.csproj" -c Release -o "$output" -p:Version="$version"
done
