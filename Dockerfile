FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS bare

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY app/ .
RUN chmod +x magpie && ln -s /app/data/config /app/config && mkdir -p /app/extensions

COPY --chmod=755 <<'EOF' /usr/local/bin/magpie-entrypoint
#!/bin/sh
set -e
if [ "$(id -u)" = 0 ] && [ "${PUID:-$APP_UID}" != 0 ]; then
    uid="${PUID:-$APP_UID}"
    gid="${PGID:-$uid}"
    chown "$uid:$gid" /app/data 2> /dev/null || echo "magpie-entrypoint: /app/data could not be given to $uid:$gid" >&2
    find /app/data -mindepth 1 -maxdepth 1 ! -type l -writable -print |
        while IFS= read -r entry; do
            if [ "$(stat -c %u:%g "$entry")" != "$uid:$gid" ] &&
                ! { chown "$uid:$gid" "$entry" 2> /dev/null && [ "$(stat -c %u:%g "$entry")" = "$uid:$gid" ]; }; then
                echo "magpie-entrypoint: $entry could not be given to $uid:$gid" >&2
                continue
            fi
            find "$entry" ! -type l \( ! -user "$uid" -o ! -group "$gid" \) -writable \
                -exec chown "$uid:$gid" {} + 2> /dev/null ||
                echo "magpie-entrypoint: some of $entry could not be given to $uid:$gid" >&2
        done
    chown -R "$uid:$gid" /home/app
    export HOME=/home/app
    exec setpriv --reuid="$uid" --regid="$gid" --clear-groups "$0" "$@"
fi
mkdir -p /app/data/config
extensions=/app/data/extensions
if [ -e "$extensions/.bundled" ]; then
    find "$extensions" -maxdepth 1 -type d -name '.bundled-copy-*' -mmin +60 -exec rm -rf {} + 2> /dev/null || true
else
    mkdir -p "$extensions"
    if ls -d "$extensions"/.bundled-copy-* > /dev/null 2>&1 ||
        [ -z "$(ls -A "$extensions" | grep -vx lost+found)" ]; then
        staging="$(mktemp -d "$extensions/.bundled-copy-XXXXXX")"
        cp -R /app/extensions/. "$staging"
        for package in "$staging"/*; do
            [ -e "$package" ] || continue
            mv "$package" "$extensions/" 2> /dev/null || [ -d "$extensions/${package##*/}" ]
        done
        rm -rf "$staging"
    fi
    touch "$extensions/.bundled"
fi
exec /app/magpie "$@"
EOF

ENV Magpie__Extensions__Path=/app/data/extensions

EXPOSE 8080
VOLUME /app/data

ENTRYPOINT ["/usr/local/bin/magpie-entrypoint"]
CMD ["sentinel"]

FROM bare AS standard

COPY extensions/ /app/extensions/

FROM standard AS postgresql

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && install -d /usr/share/postgresql-common/pgdg \
    && curl -fsSL -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc https://www.postgresql.org/media/keys/ACCC4CF8.asc \
    && apt-get purge -y --auto-remove curl \
    && . /etc/os-release \
    && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt $VERSION_CODENAME-pgdg main" > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client-18 \
    && rm -rf /var/lib/apt/lists/*

FROM standard AS default
