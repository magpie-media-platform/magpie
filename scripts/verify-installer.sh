#!/bin/bash
set -euo pipefail
export MSYS_NO_PATHCONV=1

msi="$(cygpath -wa "$1")"
shift
install_dir="$(cygpath -u "$ProgramW6432")/Magpie"
log="$(mktemp)"

cleanup() {
    rm -f "$log"
}
trap cleanup EXIT

fail() {
    echo "verify-installer: $*" >&2
    exit 1
}

installer() {
    if ! msiexec "$1" "$msi" /qn /l*v "$(cygpath -w "$log")"; then
        tr -d '\0' < "$log" | grep -B 40 -m 1 "Return value 3" >&2 || true
        return 1
    fi
}

serves() {
    for _ in $(seq 60); do
        if curl --silent --fail --max-time 5 http://127.0.0.1:8080/api/system/ready > /dev/null; then
            return 0
        fi
        sleep 1
    done
    sc.exe queryex Magpie >&2 || true
    curl --verbose --max-time 5 http://127.0.0.1:8080/api/system/ready >&2 || true
    wevtutil qe Application "/q:*[System[Provider[@Name='Magpie.App']]]" /f:text /c:20 /rd:true >&2 || true
    return 1
}

installer /i || fail "the installer did not install $msi"
serves || fail "the installed service does not serve /api/system/ready"
sc.exe qfailure Magpie | grep -qF "RESTART -- Delay = 5000 milliseconds" ||
    fail "the installed service has no restart recovery action"
sc.exe qfailureflag Magpie | grep -qE "FAILURE_ACTIONS_ON_NONCRASH_FAILURES: +TRUE" ||
    fail "the installed service does not apply its recovery actions to a stop with an error"

installer /x || fail "the installer did not uninstall $msi"
[[ -n "$(ls -A "$install_dir/data")" ]] || fail "the uninstall did not keep the data folder"
for id in "$@"; do
    [[ -d "$install_dir/extensions/$id" ]] || fail "the uninstall did not keep extension $id"
done

echo "verify-installer: $msi passed"
