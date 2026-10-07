# (syntax frontend removed 2026-09-28: every directive here is covered by BuildKit's built-in
#  frontend, and fetching docker/dockerfile:1.7 requires an auth.docker.io round-trip that is
#  unreachable from CN networks without a proxy - the image must build offline.)
#
# NovaraSync - the server half of Novara Sync, as a container image.
#
# Two stages, because the SDK is ~1.7 GB and the application is a few MB of managed code plus the
# static reader: the runtime stage carries only what the process actually loads. The runtime base is
# Debian (glibc), not Alpine (musl) - SQLite's native library has linux-x64/arm64 builds for glibc
# and musl has never been measured in this project, so the smaller image would be an untested
# variable for no operational gain.
#
# The image and the bare binary are the same code with the same environment variables; "the Docker
# image and a bare binary behave identically" is a standing requirement, and the integration checks
# assert the same status codes against both.
#
# Build (amd64; arm64 is deliberately not built or tested here):
#   docker buildx build --platform linux/amd64 -t novara-sync:9.0.0 .
#
# Behind a proxy, the two predefined build args are honoured by the restore and publish steps:
#   docker buildx build --platform linux/amd64 \
#     --build-arg HTTP_PROXY=http://host.docker.internal:7897 \
#     --build-arg HTTPS_PROXY=http://host.docker.internal:7897 -t novara-sync:9.0.0 .
# (With a TUN-style proxy the container is already routed and neither arg is needed.)

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Declared so the predefined proxy args reach the restore. BuildKit invalidates the layers below
# when their value changes, which is what keeps a cached restore from outliving the proxy setting.
ARG HTTP_PROXY
ARG HTTPS_PROXY

WORKDIR /src

# global.json is copied first and on purpose: it pins the SDK band (10.0.400, rollForward
# latestFeature), so the image cannot quietly build with a different toolchain than the developer
# and CI machines. A base image older than that pin fails here, loudly, which is the wanted outcome.
COPY global.json ./

# Restore before the sources: the layer then survives every source-only change.
COPY Novara.Core/Novara.Core.csproj Novara.Core/
COPY Novara.Sync.Server/Novara.Sync.Server.csproj Novara.Sync.Server/
COPY Novara.Server/Novara.Server.csproj Novara.Server/
RUN dotnet restore Novara.Server/Novara.Server.csproj

# Novara.Web and the two SnapshotViewer files are build inputs, not extras: Novara.Server.csproj
# links them into the output as Novara.Web/**, which is where the reader's default root points.
COPY Novara.Core/ Novara.Core/
COPY Novara.Sync.Server/ Novara.Sync.Server/
COPY Novara.Server/ Novara.Server/
COPY Novara.Web/ Novara.Web/
COPY SnapshotViewer/viewer.css SnapshotViewer/viewer.js SnapshotViewer/

RUN dotnet publish Novara.Server/Novara.Server.csproj \
        -c Release --no-restore -o /app

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

LABEL org.opencontainers.image.title="NovaraSync" \
      org.opencontainers.image.description="Novara Sync server: end-to-end encrypted snapshot sync and the read-only web reader." \
      org.opencontainers.image.source="https://github.com/Novara-owner/Novara-Vault" \
      org.opencontainers.image.licenses="MIT"

WORKDIR /app
COPY --from=build /app ./

# NOVARA_SYNC_DATA is the one variable the whole deployment hangs on: it is the directory the
# operator bind-mounts, backs up and restores.
# ASPNETCORE_HTTP_PORTS rather than ASPNETCORE_URLS: the base image already sets HTTP_PORTS=8080, so
# overriding a *different* variable leaves Kestrel printing "Overriding HTTP_PORTS '8080' ... Binding
# to values defined by URLS instead" on every single start. One variable for the port also means the
# health check below reads the same value the process binds, so the two cannot drift apart.
# HTTP_PORTS binds every interface (http://*:5180), which is required: the process sits behind Caddy
# in the compose file, and a loopback-only bind would be unreachable from the proxy container.
ENV NOVARA_SYNC_DATA=/data \
    ASPNETCORE_HTTP_PORTS=5180

# /data is created and chowned *in the image* rather than left to the operator. This is what makes
# the named-volume path work with no manual step: Docker seeds a fresh named volume from the image's
# directory, ownership included. A bind mount is the other documented shape, and there the host's
# ownership wins - which is exactly the trap the startup check in SyncHostSetup.ValidateDataRoot
# reports as a sentence instead of letting SQLite fail on every write.
RUN mkdir -p /data && chown "$APP_UID":"$APP_UID" /data
VOLUME /data

# Non-root by default. APP_UID is set by the .NET base image and is also what the
# startup check names in its chown hint, so the two cannot drift.
USER $APP_UID

EXPOSE 5180

# Probing /healthz needs no extra binary: the .NET runtime images ship no curl or wget, and the
# project deliberately ships no diagnostic tooling in the image. Bash is in every Debian/Ubuntu
# base, and /dev/tcp is enough to read one status line. A health check that reported "the process is
# alive" instead would pass while the API answered nothing, which is the failure mode that matters.
#
# The Host header is not decoration. As soon as the operator sets NOVARA_SYNC_ALLOWED_HOSTS, host
# filtering turns on with AllowEmptyHosts=false, and a header-less probe is rejected by the server's
# own filter - the container reports unhealthy while the service is perfectly fine (observed in
# practice before the probe derived its Host header from the allow-list). The value
# is derived from the operator's own allow-list, so the probe answers to the same name the service
# does; with no allow-list there is no filter and the fallback is accepted too. Stripping the spaces
# keeps a comma-separated list from producing an invalid header.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD ["/bin/bash","-c","h=${NOVARA_SYNC_ALLOWED_HOSTS%%,*}; h=${h// /}; h=${h:-localhost}; exec 3<>/dev/tcp/127.0.0.1/${ASPNETCORE_HTTP_PORTS:-5180} && printf 'GET /healthz HTTP/1.1\\r\\nHost: %s\\r\\nConnection: close\\r\\n\\r\\n' $h >&3 && head -1 <&3 | grep -q ' 200 '"]

ENTRYPOINT ["dotnet","NovaraSync.dll"]
