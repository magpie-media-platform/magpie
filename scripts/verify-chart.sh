#!/bin/bash
set -euo pipefail

chart="$1"
database=(--set database.existingSecret=magpie-database)
keys=(--set keys.existingSecret=magpie-keys --set secretKey.existingSecret=magpie-secret-key)
shared=(--set persistence.shared.existingClaim=magpie-shared)
host=(--set persistence.shared.volume.hostPath.path=/mnt/magpie)
postgresql=(--set database.provider=postgresql "${database[@]}")
cluster=(--set cluster.enabled=true --set replicaCount=3 "${postgresql[@]}")
package="$(mktemp -d)"
trap 'rm -rf "$package"' EXIT
command -v cygpath > /dev/null && package="$(cygpath -m "$package")"
export MSYS_NO_PATHCONV=1

fail() {
    echo "verify-chart: $*" >&2
    exit 1
}

renders() {
    local name="$1"
    shift
    helm lint --strict "$chart" "$@" > /dev/null || fail "$name does not lint"
    local manifest
    manifest="$(helm template magpie "$chart" "$@")" || fail "$name does not render"
    if command -v kubeconform > /dev/null; then
        kubeconform -strict -summary=false <<< "$manifest" || fail "$name is not valid Kubernetes"
    fi
}

refuses() {
    local name="$1"
    local reason="$2"
    shift 2
    local output
    if output="$(helm template magpie "$chart" "$@" 2>&1)"; then
        fail "$name renders"
    fi
    grep -qF "$reason" <<< "$output" || fail "$name does not fail with \"$reason\": $output"
}

refuses "a chart from a checkout without image.tag" "Set image.tag, or install the chart package"
renders "a chart from a checkout with image.tag" --set image.tag=1.0.0

helm package "$chart" --version 1.0.0 --app-version 1.0.0 --destination "$package" > /dev/null
chart="$package/magpie-1.0.0.tgz"

renders "single node on SQLite"
renders "single node on SQLite with keys from Secrets" "${keys[@]}"
renders "single node on SQLite with an existing claim and an ingress" \
    --set persistence.data.existingClaim=magpie-data --set ingress.enabled=true --set 'ingress.hosts[0]=magpie.local'
renders "single node on PostgreSQL" "${postgresql[@]}"
renders "single node on SQL Server" --set database.provider=sqlserver "${database[@]}"
renders "cluster on PostgreSQL" "${cluster[@]}" "${shared[@]}"
renders "cluster on SQL Server" "${cluster[@]}" "${shared[@]}" --set database.provider=sqlserver
renders "cluster with keys from Secrets" "${cluster[@]}" "${shared[@]}" "${keys[@]}"
renders "cluster with a hostPath shared volume" "${cluster[@]}" "${host[@]}"
term='affinity.podAntiAffinity.preferredDuringSchedulingIgnoredDuringExecution[0]'
renders "a mode change approval without cluster access" "${cluster[@]}" "${shared[@]}" --set modeChange.to=cluster
renders "every optional value" --set 'imagePullSecrets[0].name=registry' --set 'knownProxies[0]=10.42.0.0/16' \
    --set 'env[0].name=TZ' --set 'env[0].value=UTC' --set resources.limits.memory=2Gi --set nodeSelector.disk=ssd \
    --set 'tolerations[0].operator=Exists' --set service.annotations.team=media \
    --set "$term.weight=1" --set "$term.podAffinityTerm.topologyKey=kubernetes.io/hostname" \
    --set 'extraVolumes[0].name=media' --set 'extraVolumes[0].hostPath.path=/mnt/media' \
    --set 'extraVolumeMounts[0].name=media' --set 'extraVolumeMounts[0].mountPath=/media'

manifest="$(helm template magpie "$chart" "${cluster[@]}" "${host[@]}")"
grep -qF "path: /mnt/magpie" <<< "$manifest" && ! grep -qF "claimName:" <<< "$manifest" ||
    fail "a cluster with a hostPath shared volume does not use only that volume"
[[ "$(grep -cE "subPath: (extensions|keys|backups)$" <<< "$manifest")" == 3 ]] ||
    fail "a cluster does not mount extensions, keys and backups from the shared volume"
[[ "$(grep -A 1 -F "name: Magpie__Cluster__Mode" <<< "$manifest")" == *"value: MultiNodeSharedExtensions"* ]] ||
    fail "a cluster does not set Magpie__Cluster__Mode to MultiNodeSharedExtensions"
grep -qxF "    magpie/mode: cluster" <<< "$manifest" || fail "a cluster does not record its mode for the mode change check"
grep -qF "kind: PodDisruptionBudget" <<< "$manifest" || fail "a cluster has no PodDisruptionBudget"
manifest="$(helm template magpie "$chart" "${cluster[@]}" "${host[@]}" "${keys[@]}")"
grep -qF "secretName: \"magpie-keys\"" <<< "$manifest" && ! grep -qF "subPath: keys" <<< "$manifest" ||
    fail "a cluster with keys from Secrets does not mount the keys Secret"

restricted=(--set podSecurityContext.runAsNonRoot=true --set podSecurityContext.runAsUser=1654
    --set podSecurityContext.runAsGroup=1654 --set podSecurityContext.fsGroup=1654
    --set-json 'securityContext.capabilities={"drop":["ALL"],"add":[]}')
renders "the PodSecurity restricted override" "${restricted[@]}"
! grep -qE "CHOWN|DAC_OVERRIDE|SETUID|SETGID" <<< "$(helm template magpie "$chart" "${restricted[@]}")" ||
    fail "the PodSecurity restricted override keeps the added capabilities"

manifest="$(helm template magpie "$chart" "${postgresql[@]}")"
! grep -qF "kind: PodDisruptionBudget" <<< "$manifest" || fail "a single node has a PodDisruptionBudget"
grep -qF "image: ghcr.io/magpie-media-platform/magpie:1.0.0-postgresql" <<< "$manifest" ||
    fail "PostgreSQL mode does not use the -postgresql image by default"

refuses "a cluster on SQLite" "SQLite is for single-node mode only" --set cluster.enabled=true "${shared[@]}"
refuses "a cluster without a shared volume" "Cluster mode needs a shared volume" "${cluster[@]}"
refuses "single node with 2 replicas" "Single-node mode runs 1 replica" --set replicaCount=2
refuses "an unknown database provider" "database.provider must be" --set database.provider=mysql
refuses "PostgreSQL without a connection string Secret" "needs database.existingSecret" \
    --set database.provider=postgresql
refuses "a cluster with an existing data claim" "for single-node mode only, because each cluster node" \
    "${cluster[@]}" "${shared[@]}" --set persistence.data.existingClaim=magpie-data
refuses "keys from a Secret without MAGPIE_SECRET_KEY" "needs secretKey.existingSecret" \
    --set keys.existingSecret=magpie-keys
refuses "a mode change approval for the other mode" "modeChange.to must be the new mode" --set modeChange.to=cluster
refuses "an ingress without hosts" "needs at least 1 host" --set ingress.enabled=true
refuses "an ingress host that is not a string" "must be a host name" \
    --set ingress.enabled=true --set 'ingress.hosts[0].host=magpie.local'

echo "verify-chart: $chart passed"
