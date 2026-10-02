FROM mcr.microsoft.com/dotnet/aspnet:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY . .
RUN chmod +x Magpie.App && ln -s /app/data/config /app/config

COPY --chmod=755 <<'EOF' /usr/local/bin/magpie-entrypoint
#!/bin/sh
set -e
mkdir -p /app/data/config /app/data/extensions
for bundled in /app/extensions/*/; do
    target="/app/data/extensions/$(basename "$bundled")"
    [ -e "$target" ] || [ -L "$target" ] || ln -s "${bundled%/}" "$target"
done
exec /app/Magpie.App "$@"
EOF

ENV ASPNETCORE_URLS=http://+:8080 \
    Magpie__Extensions__Path=/app/data/extensions

EXPOSE 8080
VOLUME /app/data

ENTRYPOINT ["/usr/local/bin/magpie-entrypoint"]
CMD ["sentinel"]
