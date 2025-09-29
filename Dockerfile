# syntax=docker/dockerfile:1

# From the base image
ARG APP_UID=1654
ARG UID=$APP_UID

ARG VERSION=EDGE
ARG RELEASE=0
ARG BUILD_CONFIGURATION=Release

########################################
# Base stage
########################################
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0-slim AS base

# RUN mount cache for multi-arch: https://github.com/docker/buildx/issues/549#issuecomment-1788297892
ARG TARGETARCH
ARG TARGETVARIANT

WORKDIR /app

ARG UID
# ffmpeg
COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffmpeg /usr/bin/
COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffprobe /usr/bin/

# BgUtil POT provider
COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /bgutil-pot /usr/bin/

# BgUtil POT client
COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /client /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider

# yt-dlp
ADD --link --chown=$UID:0 --chmod=775 https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux /usr/bin/yt-dlp

########################################
# Build stage
########################################
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /source

ARG TARGETARCH
RUN --mount=source=SoundButtons/SoundButtons.csproj,target=SoundButtons.csproj \
    dotnet restore -a $TARGETARCH "SoundButtons.csproj"

########################################
# Publish stage
########################################
FROM build AS publish

ARG BUILD_CONFIGURATION

ARG TARGETARCH
RUN --mount=source=SoundButtons/.,target=.,rw \
    dotnet publish "SoundButtons.csproj" -a $TARGETARCH -c $BUILD_CONFIGURATION -o /app

########################################
# Final stage
########################################
FROM base AS final

ARG UID
# Support arbitrary user ids (OpenShift best practice)
# https://docs.openshift.com/container-platform/4.14/openshift_images/create-images.html#use-uid_create-images
RUN chown -R $UID:0 /azure-functions-host && \
    chmod -R g=u /azure-functions-host

# Create directories with correct permissions
RUN install -d -m 775 -o $UID -g 0 /home/site/wwwroot && \
    install -d -m 775 -o $UID -g 0 /home/.cache && \
    install -d -m 775 -o $UID -g 0 /licenses && \
    install -d -m 775 -o $UID -g 0 /tmp

# dumb-init
COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /dumb-init /usr/bin/

COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/tarampampam/curl:8.8.0 /bin/curl /bin/curl
HEALTHCHECK --interval=10s --timeout=2s --retries=3 --start-period=20s CMD [ \
    "curl", "--fail", "http://127.0.0.1:8080/api/healthz" \
    ]

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
