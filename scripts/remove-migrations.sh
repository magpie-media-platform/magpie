#!/bin/bash
set -e

provider="$1"

declare -A projects=([sqlite]=SQLite [sqlserver]=SqlServer [postgresql]=PostgreSQL)
providers=(sqlite sqlserver postgresql)
[[ -n "$provider" ]] && providers=("$provider")

dotnet build src/Magpie.sln -v q

for p in "${providers[@]}"; do
    echo "Removing last migration for $p..."
    dotnet ef migrations remove --project "src/Magpie.Data.${projects[$p]}" --no-build --force
done
