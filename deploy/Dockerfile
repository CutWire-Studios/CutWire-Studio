# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f deploy/Dockerfile .

# Base images are pinned by digest (tag in the comment); Dependabot's docker ecosystem bumps both.
# node:26-slim
FROM docker.io/library/node:26-slim@sha256:36793bcbb160a6a16cede1e62b93f84e26b78a972a8a4c0b879c1c12f22761ec AS web
# Node 25+ images no longer ship Corepack.
RUN npm install -g pnpm@12.15.0
WORKDIR /src/web
COPY web/package.json web/pnpm-lock.yaml web/pnpm-workspace.yaml web/tsconfig.base.json ./
COPY web/apps ./apps
COPY web/packages ./packages
RUN --mount=type=cache,target=/root/.local/share/pnpm/store \
    pnpm install --frozen-lockfile
RUN pnpm -r build

# dotnet/sdk:10.0
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS backend
WORKDIR /src/backend
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props backend/CutWire.Studio.slnx ./
COPY backend/src/CutWire.Studio.Domain/*.csproj backend/src/CutWire.Studio.Domain/packages.lock.json src/CutWire.Studio.Domain/
COPY backend/src/CutWire.Studio.Application/*.csproj backend/src/CutWire.Studio.Application/packages.lock.json src/CutWire.Studio.Application/
COPY backend/src/CutWire.Studio.Infrastructure/*.csproj backend/src/CutWire.Studio.Infrastructure/packages.lock.json src/CutWire.Studio.Infrastructure/
COPY backend/src/CutWire.Studio.Api/*.csproj backend/src/CutWire.Studio.Api/packages.lock.json src/CutWire.Studio.Api/
# Packages stay in this layer (no cache mount): with a CI layer cache the restore
# step is skipped, and publish --no-restore needs the packages to be in the layer.
RUN dotnet restore src/CutWire.Studio.Api --locked-mode
COPY backend/src ./src
# The csproj embeds ../../../web/dist (relative to the Api project), i.e. /src/web/dist.
COPY --from=web /src/web/dist /src/web/dist
RUN dotnet publish src/CutWire.Studio.Api -c Release -r linux-x64 --no-restore -o /out

# dotnet/runtime-deps:10.0
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0@sha256:1c20bc9ebda297198be069d82b6c5d1716faeeec8b8dca53ef963f9c92d3e2e3 AS runtime

# Pinned deliberately: an extractor update can change behaviour, and that should
# be a decision rather than a surprise on the next rebuild.
ARG YTDLP_VERSION=2026.08.19
# Must match the potoken sidecar image major.
ARG BGUTIL_POT_VERSION=2.0.0
# When bumping a version above, update the matching hash: yt-dlp publishes
# SHA2-256SUMS next to each release; bgutil's value is the asset digest shown on
# its GitHub release page (or sha256sum of the downloaded zip).
ARG YTDLP_SHA256=58162f9bfdc27458ea47bfcb311cf47028f17d8154a8bf7d689861d46399230a
ARG BGUTIL_POT_SHA256=bce874dfa25896c2798e0f4f8147b7b22e785479eb1e459ab232bf2506c95016

RUN set -eux; \
    apt-get update; \
    # The digest-pinned base lags Ubuntu security updates. Upgrade those
    # packages, and rebuild this layer so curl/ffmpeg dependencies such as
    # libbrotli1 resolve to the current security version instead of a cached one.
    apt-get upgrade -y --no-install-recommends; \
    apt-get install -y --no-install-recommends ffmpeg ca-certificates curl tini; \
    curl -fsSL -o /usr/local/bin/yt-dlp \
      "https://github.com/yt-dlp/yt-dlp/releases/download/${YTDLP_VERSION}/yt-dlp_linux"; \
    echo "${YTDLP_SHA256}  /usr/local/bin/yt-dlp" | sha256sum -c -; \
    chmod +x /usr/local/bin/yt-dlp; \
    mkdir -p /usr/local/share/yt-dlp-plugins; \
    curl -fsSL -o /usr/local/share/yt-dlp-plugins/bgutil-ytdlp-pot-provider.zip \
      "https://github.com/Brainicism/bgutil-ytdlp-pot-provider/releases/download/${BGUTIL_POT_VERSION}/bgutil-ytdlp-pot-provider.zip"; \
    echo "${BGUTIL_POT_SHA256}  /usr/local/share/yt-dlp-plugins/bgutil-ytdlp-pot-provider.zip" | sha256sum -c -; \
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
    Stock__Sfx__CachePath=/data/sfx-cache.json \
    Auth__DataProtectionKeysPath=/data/keys \
    Email__DevDirectory=/data/mail \
    YTDLP_PLUGIN_DIRS=/usr/local/share/yt-dlp-plugins

VOLUME /data
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["curl", "-fsS", "http://127.0.0.1:8080/api/v1/healthz"]

ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/cutwire-studio"]
