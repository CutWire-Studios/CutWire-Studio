# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f deploy/Dockerfile .

FROM docker.io/library/node:24-slim AS web
RUN corepack enable && corepack prepare pnpm@12.15.0 --activate
WORKDIR /src/web
COPY web/package.json web/pnpm-lock.yaml web/pnpm-workspace.yaml web/tsconfig.base.json ./
COPY web/apps ./apps
COPY web/packages ./packages
RUN --mount=type=cache,target=/root/.local/share/pnpm/store \
    pnpm install --frozen-lockfile
RUN pnpm -r build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src/backend
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props backend/CutWire.Studio.slnx ./
COPY backend/src/CutWire.Studio.Domain/*.csproj src/CutWire.Studio.Domain/
COPY backend/src/CutWire.Studio.Application/*.csproj src/CutWire.Studio.Application/
COPY backend/src/CutWire.Studio.Infrastructure/*.csproj src/CutWire.Studio.Infrastructure/
COPY backend/src/CutWire.Studio.Api/*.csproj src/CutWire.Studio.Api/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/CutWire.Studio.Api -r linux-x64
COPY backend/src ./src
# The csproj embeds ../../../web/dist (relative to the Api project), i.e. /src/web/dist.
COPY --from=web /src/web/dist /src/web/dist
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/CutWire.Studio.Api -c Release -r linux-x64 --no-restore -o /out

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime

# Pinned deliberately: an extractor update can change behaviour, and that should
# be a decision rather than a surprise on the next rebuild.
ARG YTDLP_VERSION=2026.08.19
# Must match the potoken sidecar image major.
ARG BGUTIL_POT_VERSION=2.0.0

RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends ffmpeg ca-certificates curl tini; \
    curl -fsSL -o /usr/local/bin/yt-dlp \
      "https://github.com/yt-dlp/yt-dlp/releases/download/${YTDLP_VERSION}/yt-dlp_linux"; \
    chmod +x /usr/local/bin/yt-dlp; \
    mkdir -p /usr/local/share/yt-dlp-plugins; \
    curl -fsSL -o /usr/local/share/yt-dlp-plugins/bgutil-ytdlp-pot-provider.zip \
      "https://github.com/Brainicism/bgutil-ytdlp-pot-provider/releases/download/${BGUTIL_POT_VERSION}/bgutil-ytdlp-pot-provider.zip"; \
    rm -rf /var/lib/apt/lists/*

RUN set -eux; \
    useradd --system --uid 10001 --home-dir /data --shell /usr/sbin/nologin studio; \
    mkdir -p /data/blobs /data/keys /data/mail; \
    chown -R studio:studio /data

COPY --from=backend /out/CutWire.Studio.Api /usr/local/bin/cutwire-studio
COPY --from=backend /out/appsettings.json /app/appsettings.json

USER studio
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/dotnet-bundle \
    Storage__BlobRoot=/data/blobs \
    Auth__DataProtectionKeysPath=/data/keys \
    Email__DevDirectory=/data/mail \
    YTDLP_PLUGIN_DIRS=/usr/local/share/yt-dlp-plugins

VOLUME /data
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["curl", "-fsS", "http://127.0.0.1:8080/api/v1/healthz"]

ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/cutwire-studio"]
