# syntax=docker/dockerfile:1

# From the base image
ARG APP_UID=1654
ARG UID=$APP_UID

ARG VERSION=EDGE
ARG RELEASE=0
ARG BUILD_CONFIGURATION=Release

# Runtime base image, pinned once and shared by the base and download stages
ARG BASE_IMAGE=mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0

########################################
# Base stage
########################################
FROM ${BASE_IMAGE} AS base

# RUN mount cache for multi-arch: https://github.com/docker/buildx/issues/549#issuecomment-1788297892
ARG TARGETARCH
ARG TARGETVARIANT

WORKDIR /app

ARG UID
# ffmpeg
COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.1 /ffmpeg /usr/bin/
COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.1 /ffprobe /usr/bin/

# BgUtil POT provider
COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /bgutil-pot /usr/bin/

# BgUtil POT client
COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /client /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider

# yt-dlp
ADD --link --chown=$UID:0 --chmod=775 https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux /usr/bin/yt-dlp

# Deno (runtime dependency for yt-dlp)
COPY --link --chown=$UID:0 --chmod=775 --from=docker.io/denoland/deno:bin /deno /usr/bin/deno

########################################
# Build stage
########################################
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /source

# Copy csproj and restore dependencies
COPY SoundButtons/SoundButtons.csproj ./SoundButtons/
ARG TARGETARCH
RUN dotnet restore -a $TARGETARCH "SoundButtons/SoundButtons.csproj"

########################################
# Publish stage
########################################
FROM build AS publish

ARG BUILD_CONFIGURATION

# Copy the rest of the source files
COPY SoundButtons/ ./SoundButtons/

ARG TARGETARCH
RUN dotnet publish "SoundButtons/SoundButtons.csproj" -a $TARGETARCH -c $BUILD_CONFIGURATION -o /app --no-restore

########################################
# Test stage
########################################
# Runs unit + integration tests on the build platform (tests execute natively, so no
# cross-arch emulation). The static ffmpeg/ffprobe binaries enable the encoder
# integration tests; coverage is enforced via the coverlet.msbuild threshold configured
# in the test project. Results are written to /testresults for the report stage to export.
FROM build AS test

# ffmpeg/ffprobe for the encoder integration tests (no network required: media is
# synthesized with lavfi virtual inputs).
COPY --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.1 /ffmpeg /usr/local/bin/
COPY --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.1 /ffprobe /usr/local/bin/

# yt-dlp for the generic download-path integration test (driven against a local file://
# URL, so still no network is required at test time).
ADD --chmod=775 https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux /usr/local/bin/yt-dlp

WORKDIR /source

# Restore the test project (and its reference to the production project) for the build
# platform so the test host runs natively.
COPY SoundButtons.Tests/SoundButtons.Tests.csproj ./SoundButtons.Tests/
RUN dotnet restore "SoundButtons.Tests/SoundButtons.Tests.csproj"

# Copy the rest of the source files (production project already copied is not, so copy both)
COPY SoundButtons/ ./SoundButtons/
COPY SoundButtons.Tests/ ./SoundButtons.Tests/

# Run tests with coverage. The coverlet.msbuild Threshold (85, line+branch, total) in the
# test csproj fails the build if coverage regresses. Cobertura + TRX are emitted for CI.
RUN dotnet test "SoundButtons.Tests/SoundButtons.Tests.csproj" \
        -c Debug \
        --results-directory /testresults \
        --logger "trx;LogFileName=test-results.trx" \
        -p:CollectCoverage=true \
        "-p:CoverletOutputFormat=cobertura%2cjson" \
        -p:CoverletOutput=/testresults/

########################################
# Report stage
########################################
# Minimal scratch image whose sole purpose is to export the test results/coverage to the
# host via `docker build --target report --output type=local,dest=...`.
FROM scratch AS report
COPY --from=test /testresults /testresults

########################################
# Download stage
########################################
FROM --platform=$BUILDPLATFORM ${BASE_IMAGE} AS download

ARG TARGETARCH

# Download the official Yelp/dumb-init static binary (arch-aware) and verify its
# SHA256 against the upstream-published checksum; the build fails on mismatch.
# curl and ca-certificates are already provided by the base image.
RUN case "${TARGETARCH}" in \
      amd64) DUMBINIT_ARCH="x86_64"; DUMBINIT_SHA256="e874b55f3279ca41415d290c512a7ba9d08f98041b28ae7c2acb19a545f1c4df" ;; \
      arm64) DUMBINIT_ARCH="aarch64"; DUMBINIT_SHA256="b7d648f97154a99c539b63c55979cd29f005f88430fb383007fe3458340b795e" ;; \
      *) echo "unsupported architecture: ${TARGETARCH}" && exit 1 ;; \
    esac && \
    curl -fsSL --retry 3 --retry-all-errors --connect-timeout 15 "https://github.com/Yelp/dumb-init/releases/download/v1.2.5/dumb-init_1.2.5_${DUMBINIT_ARCH}" \
      -o /dumb-init && \
    echo "${DUMBINIT_SHA256}  /dumb-init" | sha256sum -c -

########################################
# Final stage
########################################
FROM base AS final

ARG UID
# Support arbitrary user ids (OpenShift best practice)
# https://docs.openshift.com/container-platform/4.14/openshift_images/create-images.html#use-uid_create-images
RUN chown -R $UID:0 /azure-functions-host && \
    chmod -R g=u /azure-functions-host

# Create directories with correct permissions (/home/.cache services runtime tool caches such as Deno and yt-dlp)
RUN install -d -m 775 -o $UID -g 0 /home/site/wwwroot && \
    install -d -m 775 -o $UID -g 0 /home/.cache && \
    install -d -m 775 -o $UID -g 0 /licenses && \
    install -d -m 775 -o $UID -g 0 /tmp

# dumb-init
COPY --link --chown=$UID:0 --chmod=775 --from=download /dumb-init /usr/bin/

# Copy licenses (OpenShift Policy)
COPY --link --chown=$UID:0 --chmod=775 LICENSE /licenses/LICENSE

# Copy dist
COPY --link --chown=$UID:0 --chmod=775 --from=publish /app /home/site/wwwroot

ENV PATH="/home/site/wwwroot:/home/$UID/.local/bin:$PATH"

ENV AzureWebJobsScriptRoot=/home/site/wwwroot
ENV FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
ENV AzureFunctionsJobHost__Logging__Console__IsEnabled=true
ENV AzureFunctionsJobHost__Logging__LogLevel__Default=Information

# Set this to the connection string for the online storage account or the local emulator
# https://learn.microsoft.com/zh-tw/azure/storage/common/storage-use-azurite#http-connection-strings
ENV AzureWebJobsStorage=""

# Issue: Azure Durable Function HttpStart failure: Webhooks are not configured
# https://stackoverflow.com/a/64404153/8706033
ENV WEBSITE_HOSTNAME=localhost:8080

ENV Seq_ServerUrl=""
ENV Seq_ApiKey=""
ENV AzureStorage=""
ENV OpenAI_ApiKey=""

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

USER $UID:0

VOLUME [ "/tmp" ]

WORKDIR /tmp

STOPSIGNAL SIGINT

# Use dumb-init as PID 1 to handle signals properly
ENTRYPOINT [ "dumb-init", "--", "/opt/startup/start_nonappservice.sh" ]

ARG VERSION
ARG RELEASE
LABEL name="sound-buttons/sound-buttons_upload-backend" \
    # Authors for SoundButtons
    vendor="SoundButtons" \
    # Maintainer for this docker image
    maintainer="jim60105" \
    # Dockerfile source repository
    url="https://github.com/sound-buttons/sound-buttons_upload-backend" \
    version=${VERSION} \
    # This should be a number, incremented with each change
    release=${RELEASE} \
    io.k8s.display-name="SoundButtons" \
    summary="SoundButtons: 一個 Vtuber 聲音按鈕網站實作之音檔投稿系統後端，提交表單後能自動剪輯 Youtube 音訊並生成按鈕。以 Azure Functions 實作，上傳音檔並更新 JSON 設定檔至 Azure Blob Storage。" \
    description="For more information about this tool, please visit the following website: https://github.com/sound-buttons"
