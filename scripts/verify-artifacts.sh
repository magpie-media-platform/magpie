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
trap 'rm -rf "$work"' EXIT

fail() {
    echo "verify-artifacts: $*" >&2
    exit 1
}

magpie() {
    local app="$1"
    shift
    (cd "$app" && dotnet Magpie.App.dll "$@") > "$work/out" 2> "$work/log"
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

for package in "$packages"/*.nupkg; do
    nuspec="$(unzip -p "$package" '*.nuspec')"
    grep -qF "<version>$version</version>" <<< "$nuspec" || fail "$package does not have version $version"
    if grep -o '<dependency id="Magpie\.[^"]*" version="[^"]*"' <<< "$nuspec" |
        grep -vF "version=\"[$version, $next)\"" > /dev/null; then
        fail "$package has a Magpie dependency with a range other than [$version, $next)"
    fi
    case "$(basename "$package")" in
        Magpie.Extensions.*) required=Magpie.Sdk ;;
        Magpie.Sdk.*) required=Magpie.Models ;;
        *) required= ;;
    esac
    if [[ -n "$required" ]] && ! grep -qF "<dependency id=\"$required\" version=\"[$version, $next)\"" <<< "$nuspec"; then
        fail "$package has no $required dependency with the range [$version, $next)"
    fi
done

mkdir "$work/app" "$work/placed"
tar -xzf "$app_archive" -C "$work/app"
tar -xzf "$app_archive" -C "$work/placed"

magpie "$work/app" version
reported="$(tr -d '\r' < "$work/out")"
[[ "$reported" == "Magpie $version" || "$reported" == "Magpie $version+"* ]] ||
    fail "the application reports '$reported', not version $version"

magpie "$work/app" extensions list
bundled="$(find "$work/app/extensions" -mindepth 1 -maxdepth 1 -type d | wc -l)"
loaded="$(jq --arg version "$core" '[.[].extension | select(.enabled and .version == $version)] | length' "$work/out")"
[[ "$bundled" -gt 0 && "$loaded" -eq "$bundled" ]] ||
    fail "$loaded of $bundled bundled extensions loaded with version $core"

dotnet publish "$fixture_project" -c Release -o "$work/placed/extensions/lifecycle" \
    --artifacts-path "$work/build/placed" -p:Version="$next" > "$work/fixture-placed.log" ||
    fail "the placed fixture did not publish"
magpie "$work/placed" extensions list
[[ "$(extension_state "$fixture_id")" == "false $next" ]] &&
    grep -qF "Extension $fixture_id is built against ABI $next, which is not compatible with host ABI $core" "$work/log" ||
    fail "the application did not reject the extension built against ABI $next in its extensions directory"

for package in "$packages"/Magpie.Extensions.*.nupkg; do
    magpie "$work/app" extensions upload --file "$package" || fail "the application did not install $package"
done
magpie "$work/app" extensions list
for package in "$packages"/Magpie.Extensions.*.nupkg; do
    id="$(basename "$package" ".$version.nupkg")"
    [[ "$(extension_state "$id")" == "true $version" ]] || fail "the application did not load $id $version"
done

package="$(pack_fixture exact -p:Version="$version" -p:TargetAbi="[$core]")"
magpie "$work/app" extensions upload --file "$package" || fail "the application did not install $package"
magpie "$work/app" extensions list
[[ "$(extension_state "$fixture_id")" == "true $version" ]] ||
    fail "the application did not load the fixture with target ABI [$core]"

above="[$major.$minor.$((patch + 1)),$next)"
package="$(pack_fixture above -p:Version="$version" -p:TargetAbi="${above/,/%2C}")"
magpie "$work/app" extensions upload --file "$package" || fail "the application did not install $package"
magpie "$work/app" extensions list
[[ "$(extension_state "$fixture_id")" == "false $version" ]] &&
    grep -qF "Extension Lifecycle fixture targets ABI $above, which is not compatible with host ABI $core" "$work/log" ||
    fail "the application did not reject the fixture with target ABI $above"

package="$(pack_fixture next-major -p:Version="$next")"
if magpie "$work/app" extensions upload --file "$package"; then
    fail "the application installed the fixture built against ABI $next"
fi
grep -qF "Extension package $fixture_id is built against ABI $next, which is not compatible with host ABI $core" \
    "$work/log" || fail "the application did not reject the package built against ABI $next"

echo "verify-artifacts: $version passed"
