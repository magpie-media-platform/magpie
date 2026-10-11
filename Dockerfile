FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS bare

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY app/ .
RUN chmod +x Magpie.App && ln -s /app/data/config /app/config && mkdir -p /app/extensions

COPY --chmod=755 <<'EOF' /usr/local/bin/magpie-entrypoint
#!/bin/sh
set -e
mkdir -p /app/data/config
if [ ! -e /app/data/extensions ]; then
    rm -rf /app/data/.extensions
    cp -R /app/extensions /app/data/.extensions
    mv /app/data/.extensions /app/data/extensions
fi
exec /app/Magpie.App "$@"
EOF

ENV ASPNETCORE_URLS=http://+:8080 \
    Magpie__Extensions__Path=/app/data/extensions

EXPOSE 8080
VOLUME /app/data

ENTRYPOINT ["/usr/local/bin/magpie-entrypoint"]
CMD ["sentinel"]

FROM bare AS standard

COPY extensions/ /app/extensions/
