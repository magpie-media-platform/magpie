#!/bin/bash
set -euo pipefail

version="$1"
shift
standard="magpie:$version"
bare="magpie:$version-bare"
postgresql="magpie:$version-postgresql"
upgrade="magpie-verify:$$-upgrade"
volume="magpie-verify-$$"
server="magpie-verify-$$-server"
log="$(mktemp)"
preset="$(for id in "$@"; do echo "$id true true $version"; done | sort)"

cleanup() {
    rm -f "$log"
    docker rm -f --volumes "$server" > /dev/null 2>&1 || true
    docker volume rm -f "$volume-standard" "$volume-bare" "$volume-switch" "$volume-root" "$volume-shared" \
        "$volume-earlier" > /dev/null 2>&1 || true
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

extensions_in() {
    docker run --rm --volume "$1:/app/data" --entrypoint ls "$standard" -A /app/data/extensions | sort
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
            uid="$(docker exec "$server" awk '/^Uid:/ { print $2 }' /proc/1/status)"
            docker rm -f --volumes "$server" > /dev/null
            return 0
        fi
        sleep 1
    done
    docker logs "$server" > "$log" 2>&1
    return 1
}

for image in "$standard" "$bare" "$postgresql"; do
    docker run --rm "$image" version | grep -qF "Magpie $version" || fail "$image does not report version $version"
    serves "$image" || fail "a new $image instance does not serve /api/system/ready with its default command"
    [[ "$uid" == 1654 ]] || fail "$image runs Magpie as UID $uid, not as the app user"
done
for tool in pg_dump pg_restore; do
    docker run --rm --user 1654 --entrypoint "$tool" "$postgresql" --version > /dev/null ||
        fail "$postgresql does not run $tool as UID 1654"
done

[[ "$(inventory "$standard" "$volume-standard")" == "$preset" ]] ||
    fail "a new Standard instance did not register the Standard extensions, enabled and managed, at $version"
for id in "$@"; do
    grep -F "The extension loader loaded extension" "$log" | grep -qF " from /app/data/extensions/$id/lib/" ||
        fail "a new Standard instance did not load $id"
done
[[ -z "$(inventory "$bare" "$volume-bare")" ]] || fail "a new Bare instance registered an extension"

inventory "$standard" "$volume-root" > /dev/null
docker run --rm --entrypoint chown --volume "$volume-root:/app/data" "$standard" -R 0:0 /app/data
[[ "$(inventory "$standard" "$volume-root")" == "$preset" ]] || fail "a Standard instance did not take over data that root owns"
docker run --rm --env PUID=1000 --env PGID=1001 --volume "$volume-root:/app/data" "$standard" version > /dev/null
[[ "$(docker run --rm --entrypoint stat --volume "$volume-root:/app/data" "$standard" -c %u:%g /app/data/config)" == 1000:1001 ]] ||
    fail "a Standard instance with PUID and PGID did not give its data to that user"

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

pids=()
for _ in 1 2 3; do
    docker run --rm --volume "$volume-shared:/app/data" "$standard" version > /dev/null &
    pids+=("$!")
done
for pid in "${pids[@]}"; do
    wait "$pid" || fail "a Standard instance that started together with others on one volume failed"
done
[[ "$(extensions_in "$volume-shared")" == "$(printf '%s\n' .bundled "$@" | sort)" ]] ||
    fail "Standard instances that start together on one volume did not copy each extension once"

docker run --rm --volume "$volume-shared:/app/data" --entrypoint sh "$standard" \
    -c "cd /app/data/extensions && mkdir .bundled-copy-old .bundled-copy-new &&
        touch -d '2 hours ago' .bundled-copy-old"
docker run --rm --volume "$volume-shared:/app/data" "$standard" version > /dev/null
[[ "$(extensions_in "$volume-shared")" == "$(printf '%s\n' .bundled .bundled-copy-new "$@" | sort)" ]] ||
    fail "a Standard instance did not remove only the .bundled-copy-* folders older than 60 minutes"

docker run --rm --volume "$volume-earlier:/app/data" --entrypoint sh "$standard" \
    -c "mkdir /app/data/extensions && cp -R /app/extensions/$1 /app/data/extensions/"
docker run --rm --volume "$volume-earlier:/app/data" "$standard" version > /dev/null
[[ "$(extensions_in "$volume-earlier")" == "$(printf '%s\n' .bundled "$1" | sort)" ]] ||
    fail "a Standard instance copied extensions to an extensions folder from an earlier image"

echo "verify-images: $version passed"
