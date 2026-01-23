ARG ALPINE_VERSION=3.23.2
ARG DOTNET_VERSION=9.0

################################################################################
# Builder stage
################################################################################
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-alpine AS build

ARG TARGETARCH
ARG BUILDPLATFORM

WORKDIR /src

# Restore with minimal context
COPY global.json ./
COPY JacRed.Api/JacRed.Api.csproj JacRed.Api/
COPY JacRed.Core/JacRed.Core.csproj JacRed.Core/
COPY JacRed.Infrastructure/JacRed.Infrastructure.csproj JacRed.Infrastructure/
RUN dotnet restore JacRed.Api/JacRed.Api.csproj

# Bring in the rest of the source
COPY . .

# Publish self-contained single file per architecture
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    set -eux; \
    case "${TARGETARCH}" in \
      amd64) RID=linux-musl-x64 ;; \
      arm64) RID=linux-musl-arm64 ;; \
      arm)   RID=linux-musl-arm ;; \
      *) echo "Unsupported architecture: ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    dotnet publish JacRed.Api/JacRed.Api.csproj \
      --configuration Release \
      --runtime "$RID" \
      --self-contained true \
      --output /dist \
      --verbosity minimal \
      -p:PublishTrimmed=false \
      -p:PublishSingleFile=true \
      -p:DebugType=None \
      -p:EnableCompressionInSingleFile=true \
      -p:OptimizationPreference=Speed \
      -p:SuppressTrimAnalysisWarnings=true \
      -p:IlcOptimizationPreference=Speed \
      -p:IlcFoldIdenticalMethodBodies=true

################################################################################
# Runtime stage
################################################################################
FROM alpine:${ALPINE_VERSION} AS runtime

ARG ALPINE_VERSION
ARG DOTNET_VERSION
ARG TARGETARCH

LABEL org.opencontainers.image.title="JacRed" \
      org.opencontainers.image.description="JacRed torrent tracker aggregator" \
      org.opencontainers.image.vendor="jacred" \
      org.opencontainers.image.base.name="alpine:${ALPINE_VERSION}"

RUN set -eux; \
    apk add --no-cache --update \
      ca-certificates \
      curl \
      dumb-init \
      ffmpeg \
      icu-libs \
      libgcc \
      libintl \
      libstdc++ \
      krb5-libs \
      tzdata \
      wget \
      bash \
    && addgroup -g 1000 -S jacred \
    && adduser -u 1000 -S jacred -G jacred -s /sbin/nologin -h /app \
    && mkdir -p /app \
    && chown -R jacred:jacred /app \
    && chmod -R 750 /app

WORKDIR /app

# Application binaries
COPY --from=build --chown=jacred:jacred --chmod=550 /dist/ /app/

# Runtime configuration
COPY --chown=jacred:jacred --chmod=640 JacRed.Api/appsettings.json /app/appsettings.json

# Entrypoint
COPY --chown=jacred:jacred --chmod=550 entrypoint.sh /entrypoint.sh

# Environment defaults
ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0 \
    DOTNET_USE_POLLING_FILE_WATCHER=1 \
    ASPNETCORE_URLS=http://0.0.0.0:9117 \
    ASPNETCORE_ENVIRONMENT=Production \
    TZ=UTC \
    UMASK=0027 \
    HEALTHCHECK_PORT=9117

USER jacred:jacred

EXPOSE 9117/tcp

HEALTHCHECK --interval=30s \
    --timeout=15s \
    --start-period=45s \
    --retries=3 \
    --start-interval=5s \
    CMD /bin/sh -c "wget --quiet --spider http://127.0.0.1:${HEALTHCHECK_PORT:-9117} || exit 1"

ENTRYPOINT ["dumb-init", "--", "/entrypoint.sh"]
CMD ["./JacRed.Api"]
