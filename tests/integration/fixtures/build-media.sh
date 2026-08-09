#!/usr/bin/env bash
set -euo pipefail

manifest="${1:?usage: build-media.sh <manifest.json> <output-dir>}"
out_dir="${2:?usage: build-media.sh <manifest.json> <output-dir>}"

mkdir -p "$out_dir"

count="$(jq '.fixtures | length' "$manifest")"
for i in $(seq 0 $((count - 1))); do
    name="$(jq -r ".fixtures[$i].name" "$manifest")"
    duration="$(jq -r ".fixtures[$i].durationSeconds // 1" "$manifest")"
    width="$(jq -r ".fixtures[$i].width // 1280" "$manifest")"
    height="$(jq -r ".fixtures[$i].height // 720" "$manifest")"
    vcodec="$(jq -r ".fixtures[$i].videoCodec // \"libx264\"" "$manifest")"
    acodec="$(jq -r ".fixtures[$i].audioCodec // \"aac\"" "$manifest")"
    kind="$(jq -r ".fixtures[$i].kind // \"video\"" "$manifest")"
    target="$out_dir/$name"

    if [[ "$kind" == "audio" ]]; then
        echo "[build-media] generating $target (${duration}s, ${acodec})"
        ffmpeg -nostdin -y -hide_banner -loglevel error \
            -f lavfi -i "sine=frequency=440:duration=${duration}" \
            -c:a "$acodec" "$target"
    else
        echo "[build-media] generating $target (${width}x${height}, ${duration}s, ${vcodec}/${acodec})"
        ffmpeg -nostdin -y -hide_banner -loglevel error \
            -f lavfi -i "testsrc=size=${width}x${height}:rate=24:duration=${duration}" \
            -f lavfi -i "sine=frequency=440:duration=${duration}" \
            -c:v "$vcodec" -pix_fmt yuv420p -c:a "$acodec" -shortest \
            "$target"
    fi
done

echo "[build-media] done: $(find "$out_dir" -type f | wc -l) fixture(s) in $out_dir"
