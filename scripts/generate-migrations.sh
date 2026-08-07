#!/bin/bash
set -e

if [[ -z "$1" ]]; then
    echo "Usage: $0 <migration-name> [provider]"
    exit 1
fi

name="$1"
provider="$2"

declare -A projects=([sqlite]=SQLite [sqlserver]=SqlServer [postgresql]=PostgreSQL)
providers=(sqlite sqlserver postgresql)
[[ -n "$provider" ]] && providers=("$provider")

dotnet build src/Magpie.sln -v q

for p in "${providers[@]}"; do
    echo "Generating migration '$name' for $p..."
    dotnet ef migrations add "$name" --project "src/Magpie.Data.${projects[$p]}" --output-dir Migrations --no-build &
done

wait
echo "Done."
