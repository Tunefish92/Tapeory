# syntax=docker/dockerfile:1

# The web bundle and the .NET publish output are both architecture-independent (the publish output
# carries SkiaSharp's native libraries for every Linux architecture under runtimes/), so both
# build stages run natively on the build machine. Only the final stage is per-architecture, which
# keeps multi-arch builds fast instead of emulating npm and the .NET SDK under QEMU.
FROM --platform=$BUILDPLATFORM node:22-alpine AS web-build
WORKDIR /web

COPY Tapeory.Web/package.json Tapeory.Web/package-lock.json ./
RUN npm ci

COPY Tapeory.Web/ ./
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Tapeory.Api/Tapeory.Api.csproj Tapeory.Api/
RUN dotnet restore Tapeory.Api/Tapeory.Api.csproj

COPY Tapeory.Api/ Tapeory.Api/
RUN dotnet publish Tapeory.Api/Tapeory.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

LABEL org.opencontainers.image.title="Tapeory" \
      org.opencontainers.image.description="Design, store, and print Brother P-touch labels from your browser" \
      org.opencontainers.image.source="https://github.com/Tunefish92/Tapeory"

# - curl: container health check
# - libfontconfig1: SkiaSharp's font matching (label rendering)
# - fonts-*: the base image ships no fonts at all, and the editor and renderer can only offer
#   fonts installed here. Liberation is metric-compatible with Arial, Times New Roman and
#   Courier New, which most imported .lbx files use; DejaVu covers a wide range of scripts.
#   Add more font packages in a derived image if you need them.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl libfontconfig1 fonts-liberation fonts-dejavu-core \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /data

COPY --from=build /app/publish .
COPY --from=web-build /web/dist ./wwwroot
COPY Tapeory.Api/docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
# Strip CR in case the script was checked out with Windows line endings.
RUN sed -i 's/\r$//' /usr/local/bin/docker-entrypoint.sh \
 && chmod 755 /usr/local/bin/docker-entrypoint.sh

ENV ASPNETCORE_URLS=http://+:8080 \
    TAPEORY_STORAGE_PATH=/data \
    PUID=1000 \
    PGID=1000

# Starts as root only long enough for docker-entrypoint.sh to give /data to PUID:PGID, then
# runs the app as that user.
VOLUME /data
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8080/api/health || exit 1

# The web UI and API ship as a single container: this image serves both the built React app
# (from wwwroot, see Program.cs's static file + SPA fallback middleware) and the REST API.
ENTRYPOINT ["docker-entrypoint.sh"]
