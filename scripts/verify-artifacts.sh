#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="$1"
app_archive="$2"
packages="$3"

core="${version%%-*}"
IFS=. read -r major minor patch <<< "$core"
next="$((major + 1)).0.0"
fixture_project="$repo_root/tests/integration/fixtures/LifecycleExtension/Magpie.Extensions.IntegrationTestLifecycle.csproj"
fixture_id=Magpie.Extensions.IntegrationTestLifecycle
work="$(mktemp -d)"
host="magpie-verify-$$"

cleanup() {
    docker rm -f "$host" > /dev/null 2>&1 || true
    rm -rf "$work"
}
trap cleanup EXIT

fail() {
    echo "verify-artifacts: $*" >&2
    exit 1
}

magpie() {
    local app="$1"
    shift
    docker exec --workdir "$app" "$host" "$app/Magpie.App" "$@" > "$work/out" 2> "$work/log"
}

upload() {
    docker cp "$1" "$host:/packages/$(basename "$1")" &&
        magpie /app extensions upload --file "/packages/$(basename "$1")"
}

extension_state() {
    jq -r --arg id "$1" '.[].extension | select(.packageId == $id) | "\(.enabled) \(.version)"' "$work/out"
}

pack_fixture() {
    local name="$1"
    shift
    dotnet pack "$fixture_project" -c Release -o "$work/fixtures/$name" --artifacts-path "$work/build/$name" "$@" \
        > "$work/fixture-$name.log" || fail "the $name fixture did not pack"
    echo "$work/fixtures/$name/$fixture_id".*.nupkg
}

loaded_from_package() {
    grep -qE "The extension loader loaded extension .* from .*/lib/[^/]+/${1//./[.]}[.]dll" "$work/log"
}

place_fixture() {
    docker cp "$work/placed" "$host:/placed/extensions/$fixture_id"
}

serves() {
    for _ in $(seq 60); do
        if docker exec "$host" bash -c 'exec 3<> /dev/tcp/127.0.0.1/8080 &&
            printf "GET /api/system/ready HTTP/1.0\r\n\r\n" >&3 && head -n 1 <&3' 2> /dev/null | grep -qF " 200 "; then
            return 0
        fi
        sleep 1
    done
    return 1
}

stops_gracefully() {
    for _ in $(seq 20); do
        if docker exec "$host" grep -qF "The job runtime worker stopped" /serve/out.log; then
            return 0
        fi
        sleep 1
    done
    return 1
}

for package in "$packages"/*.nupkg; do
    nuspec="$(unzip -p "$package" '*.nuspec')"
    grep -qF "<version>$version</version>" <<< "$nuspec" || fail "$package does not have version $version"
    if grep -o '<dependency id="Magpie\.[^"]*" version="[^"]*"' <<< "$nuspec" |
        grep -vF "version=\"[$version, $next)\"" > /dev/null; then
        fail "$package has a Magpie dependency with a range other than [$version, $next)"
    fi
    tagged=false
    if grep -qE '<tags>([^<]* )?magpie( [^<]*)?</tags>' <<< "$nuspec"; then
        tagged=true
    fi
    case "$(basename "$package")" in
        Magpie.Extensions.*) required=Magpie.Sdk tag_expected=true ;;
        Magpie.Sdk.*) required=Magpie.Models tag_expected=false ;;
        *) required= tag_expected=false ;;
    esac
    if [[ -n "$required" ]] && ! grep -qF "<dependency id=\"$required\" version=\"[$version, $next)\"" <<< "$nuspec"; then
        fail "$package has no $required dependency with the range [$version, $next)"
    fi
    if [[ "$tag_expected" == true && "$tagged" == false ]]; then
        fail "$package does not have the magpie tag"
    fi
    if [[ "$tag_expected" == false && "$tagged" == true ]]; then
        fail "$package has the magpie tag"
    fi
done

docker run --detach --name "$host" mcr.microsoft.com/dotnet/runtime-deps:10.0 sleep infinity > /dev/null
docker exec "$host" mkdir -p /app /placed/extensions /packages
docker cp - "$host:/app" < "$app_archive"
docker cp - "$host:/placed" < "$app_archive"

magpie /app version
reported="$(tr -d '\r' < "$work/out")"
[[ "$reported" == "Magpie $version" || "$reported" == "Magpie $version+"* ]] ||
    fail "the application reports '$reported', not version $version"

magpie /app extensions list
[[ "$(jq length "$work/out")" -eq 0 ]] || fail "a new instance of the archive registered an extension"

unzip -q "$(pack_fixture placed -p:Version="$next")" 'lib/*' '*.nuspec' -d "$work/placed"
place_fixture
magpie /placed extensions list
[[ "$(extension_state "$fixture_id")" == "true $next" ]] &&
    grep -qF "Extension $fixture_id is built against ABI $next, which is not compatible with host ABI $core" "$work/log" ||
    fail "the application changed the extension built against ABI $next in its extensions directory"
placed_id="$(jq -r --arg id "$fixture_id" '.[].extension | select(.packageId == $id) | .id' "$work/out")"

magpie /placed extensions list
[[ "$(jq length "$work/out")" -eq 1 ]] || fail "the application registered the placed fixture again on a restart"

magpie /placed extensions uninstall "$placed_id" || fail "the application did not uninstall the placed fixture"
place_fixture
magpie /placed extensions list
[[ "$(jq length "$work/out")" -eq 0 ]] ||
    fail "the application registered a placed extension after every extension was removed"

for package in "$packages"/Magpie.Extensions.*.nupkg; do
    upload "$package" || fail "the application did not install $package"
done
magpie /app extensions list
for package in "$packages"/Magpie.Extensions.*.nupkg; do
    id="$(basename "$package" ".$version.nupkg")"
    [[ "$(extension_state "$id")" == "true $version" ]] && loaded_from_package "$id" ||
        fail "the application did not load $id $version from its package layout"
done

package="$(pack_fixture exact -p:Version="$version" -p:TargetAbi="[$core]")"
upload "$package" || fail "the application did not install $package"
magpie /app extensions list
[[ "$(extension_state "$fixture_id")" == "true $version" ]] && loaded_from_package "$fixture_id" ||
    fail "the application did not load the fixture with target ABI [$core]"

above="[$major.$minor.$((patch + 1)),$next)"
package="$(pack_fixture above -p:Version="$version" -p:TargetAbi="${above/,/%2C}")"
upload "$package" || fail "the application did not install $package"
magpie /app extensions list
[[ "$(extension_state "$fixture_id")" == "true $version" ]] &&
    grep -qF "Extension Lifecycle fixture targets ABI $above, which is not compatible with host ABI $core" "$work/log" ||
    fail "the application changed the fixture with target ABI $above"

package="$(pack_fixture next-major -p:Version="$next")"
if upload "$package"; then
    fail "the application installed the fixture built against ABI $next"
fi
grep -qF "Extension package $fixture_id is built against ABI $next, which is not compatible with host ABI $core" \
    "$work/log" || fail "the application did not reject the package built against ABI $next"

docker exec "$host" mkdir -p /serve
docker cp - "$host:/serve" < "$app_archive"
docker exec --detach --workdir /serve --env ASPNETCORE_URLS=http://127.0.0.1:8080 "$host" \
    bash -c 'echo $$ > /serve/sentinel.pid && exec /serve/Magpie.App sentinel > /serve/out.log 2>&1'
if ! serves; then
    docker exec "$host" tail -n 50 /serve/out.log >&2
    fail "a new instance of the archive does not serve /api/system/ready"
fi

docker exec "$host" bash -c 'kill -TERM "$(< /serve/sentinel.pid)"'
if ! stops_gracefully; then
    docker exec "$host" tail -n 50 /serve/out.log >&2
    fail "the sentinel did not stop the host gracefully on SIGTERM"
fi

echo "verify-artifacts: $version passed"
