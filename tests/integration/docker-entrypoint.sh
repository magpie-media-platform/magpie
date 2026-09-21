#!/usr/bin/env bash
set -euo pipefail

required_extension_packages=(
    "Magpie.Extensions.IntegrationTestShims"
    "Magpie.Extensions.Movies"
    "Magpie.Extensions.Tv"
    "Magpie.Extensions.Music"
    "Magpie.Extensions.Books"
)

for package_id in "${required_extension_packages[@]}"; do
    if ! find /app/test-packages -maxdepth 1 -name "${package_id}.*.nupkg" -print -quit | grep -q .; then
        echo "[entrypoint] required extension package ${package_id} is missing" >&2
        exit 1
    fi
done

echo "[entrypoint] trusting the package feed certificate"
for attempt in 1 2 3 4 5 6 7 8 9 10; do
    if openssl s_client -connect package-feed:8081 -servername package-feed </dev/null 2>/dev/null |
        openssl x509 >/usr/local/share/ca-certificates/package-feed.crt; then
        break
    fi
    if [[ "$attempt" -eq 10 ]]; then
        echo "[entrypoint] the package feed did not present a certificate" >&2
        exit 1
    fi
    sleep 2
done
update-ca-certificates >/dev/null

echo "[entrypoint] installing required extensions through the CLI"
for package_id in "${required_extension_packages[@]}"; do
    if find /app/data/extensions -mindepth 1 -maxdepth 1 -type d -name "${package_id}.*" -print -quit | grep -q .; then
        echo "[entrypoint] required extension ${package_id} is already installed"
        continue
    fi
    if [[ "$package_id" == "Magpie.Extensions.IntegrationTestShims" ]]; then
        extension_package="/app/test-packages/Magpie.Extensions.IntegrationTestShims.1.0.0.nupkg"
    else
        extension_package="$(find /app/test-packages -maxdepth 1 -name "${package_id}.*.nupkg" -print -quit)"
    fi
    /app/Magpie.App extensions upload --file "$extension_package" >/dev/null
done

echo "[entrypoint] starting host"
exec /app/Magpie.App sentinel
