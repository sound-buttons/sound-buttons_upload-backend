## 1. Download stage

- [x] 1.1 Pin the base image once via a global `ARG BASE_IMAGE=mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0` and reference it from the existing `base` stage. Add a dedicated `download` stage declared as `FROM --platform=$BUILDPLATFORM ${BASE_IMAGE} AS download` (reusing the same base image — which already provides `curl` and `ca-certificates`, so no package install is needed — and running the checksum `RUN` natively on the builder). Reference the raw image, not the `base` stage, so the download stays decoupled from the FFmpeg/yt-dlp/BgUtil layers.
- [x] 1.2 In the download stage, declare `ARG TARGETARCH`, then `case "${TARGETARCH}"` to select the dumb-init asset: `amd64`→`x86_64` (SHA256 `e874b55f3279ca41415d290c512a7ba9d08f98041b28ae7c2acb19a545f1c4df`), `arm64`→`aarch64` (SHA256 `b7d648f97154a99c539b63c55979cd29f005f88430fb383007fe3458340b795e`), and `exit 1` for unsupported architectures.
- [x] 1.3 Download the pinned binary from `https://github.com/Yelp/dumb-init/releases/download/v1.2.5/dumb-init_1.2.5_${ARCH}` to `/dumb-init` and verify it with `echo "${SHA256}  /dumb-init" | sha256sum -c -` (build fails on mismatch). The final-stage `COPY --chmod=775` makes it executable.

## 2. Final stage wiring

- [x] 2.1 Replace the final-stage `COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /dumb-init /usr/bin/` with `COPY --link --chown=$UID:0 --chmod=775 --from=download /dumb-init /usr/bin/`.
- [x] 2.2 Confirm `ENTRYPOINT [ "dumb-init", "--", "/opt/startup/start_nonappservice.sh" ]` and `STOPSIGNAL SIGINT` are unchanged, and that no other `COPY --from=ghcr.io/jim60105/static-ffmpeg-upx` line (ffmpeg/ffprobe/dumb-init for that image) was disturbed.

## 3. Validation

- [x] 3.1 Run `hadolint Dockerfile` and confirm it passes (respecting `.hadolint.yml`).
- [x] 3.2 Build the image for `linux/amd64` and confirm the build succeeds, including the SHA256 verification step.
- [x] 3.3 Run the built image and confirm PID 1 is `dumb-init` (`cat /proc/1/comm`) and that `dumb-init --version` reports the pinned `v1.2.5`. (Full Functions-host `/api/healthz` startup requires Azure storage configuration and is unaffected by this sourcing-only change.)
- [x] 3.4 Negative test: temporarily corrupt the expected SHA256 and confirm the build fails at the checksum step; then restore it.

## 4. Commit

- [x] 4.1 Commit the Dockerfile and OpenSpec change with a conventional message and the Co-authored-by trailer.
