#!/bin/bash
set -euo pipefail

version="$1"
shift
standard="magpie:$version"
bare="magpie:$version-bare"
upgrade="magpie-verify:$$-upgrade"
volume="magpie-verify-$$"
server="magpie-verify-$$-server"
log="$(mktemp)"
preset="$(for id in "$@"; do echo "$id true true $version"; done | sort)"

cleanup() {
    rm -f "$log"
    docker rm -f --volumes "$server" > /dev/null 2>&1 || true
    docker volume rm -f "$volume-standard" "$volume-bare" "$volume-switch" > /dev/null 2>&1 || true
    docker image rm -f "$upgrade" > /dev/null 2>&1 || true
}
trap cleanup EXIT

fail() {
    tail -n 50 "$log" >&2
    echo "verify-images: $*" >&2
    exit 1
}

magpie() {
    local image="$1"
    local data="$2"
    shift 2
    docker run --rm --volume "$data:/app/data" "$image" "$@" 2> "$log"
}

inventory() {
    magpie "$1" "$2" extensions list |
        jq -r '.[].extension | "\(.packageId) \(.enabled) \(.managed) \(.version)"' | sort
}

serves() {
    docker run --detach --name "$server" "$1" > /dev/null
    for _ in $(seq 60); do
        if docker exec "$server" bash -c 'exec 3<> /dev/tcp/127.0.0.1/8080 &&
            printf "GET /api/system/ready HTTP/1.0\r\n\r\n" >&3 && head -n 1 <&3' 2> /dev/null | grep -qF " 200 "; then
            docker rm -f --volumes "$server" > /dev/null
            return 0
        fi
        sleep 1
    done
    docker logs "$server" > "$log" 2>&1
    return 1
}

for image in "$standard" "$bare"; do
    docker run --rm "$image" version | grep -qF "Magpie $version" || fail "$image does not report version $version"
    serves "$image" || fail "a new $image instance does not serve /api/system/ready with its default command"
done

[[ "$(inventory "$standard" "$volume-standard")" == "$preset" ]] ||
    fail "a new Standard instance did not register the Standard extensions, enabled and managed, at $version"
for id in "$@"; do
    grep -F "The extension loader loaded extension" "$log" | grep -qF " from /app/data/extensions/$id/lib/" ||
        fail "a new Standard instance did not load $id"
done
[[ -z "$(inventory "$bare" "$volume-bare")" ]] || fail "a new Bare instance registered an extension"

[[ "$(inventory "$standard" "$volume-standard")" == "$preset" ]] ||
    fail "a Standard instance changed its extensions on a restart"

docker build --quiet --tag "$upgrade" - > /dev/null <<EOF
FROM $standard
RUN sed -i 's|<version>[^<]*</version>|<version>0.0.1-upgrade</version>|' /app/extensions/*/*.nuspec
EOF
[[ "$(inventory "$upgrade" "$volume-standard")" == "$preset" ]] ||
    fail "a Standard instance changed its extension versions on an image upgrade"

for id in $(magpie "$standard" "$volume-standard" extensions list | jq -r '.[].extension.id'); do
    magpie "$standard" "$volume-standard" extensions uninstall "$id" > /dev/null ||
        fail "the Standard instance did not uninstall extension $id"
done
[[ -z "$(inventory "$standard" "$volume-standard")" ]] ||
    fail "a Standard instance with every extension removed registered an extension on a restart"
[[ -z "$(inventory "$upgrade" "$volume-standard")" ]] ||
    fail "a Standard instance with every extension removed registered an extension on an image upgrade"

inventory "$standard" "$volume-switch" > /dev/null
[[ "$(inventory "$bare" "$volume-switch")" == "$preset" ]] ||
    fail "a Standard instance changed to Bare did not keep its extensions"
[[ -z "$(inventory "$standard" "$volume-bare")" ]] || fail "a Bare instance changed to Standard registered an extension"

echo "verify-images: $version passed"
