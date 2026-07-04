## MODIFIED Requirements

### Requirement: Non-.NET image and tool versions are unchanged

Components whose version tags are unrelated to the .NET runtime SHALL NOT be altered by this upgrade. In particular, the FFmpeg static-binary image tag (`ghcr.io/jim60105/static-ffmpeg-upx:8.1`, where `8.1` denotes the FFmpeg version) SHALL remain unchanged, as SHALL the yt-dlp, BgUtil POT, curl, and dumb-init sources. The Deno runtime binary (`denoland/deno:bin`) is an additional static tool binary added to the base stage; it SHALL NOT alter or replace any of the pre-existing tool sources.

#### Scenario: FFmpeg image tag is preserved

- **WHEN** the `Dockerfile` `COPY --from` instructions are inspected after the upgrade
- **THEN** the `ghcr.io/jim60105/static-ffmpeg-upx:8.1` reference is unchanged
- **AND** no FFmpeg-related tag was modified under the mistaken assumption that `8.1` referred to .NET 8

#### Scenario: Deno addition does not displace existing tool binaries

- **WHEN** the `Dockerfile` base stage is inspected after the change
- **THEN** the FFmpeg, ffprobe, bgutil-pot, bgutil-pot client, yt-dlp, and dumb-init instructions remain present and unmodified
- **AND** the Deno `COPY` is an addition, not a replacement

### Requirement: Copied static tooling runs on the new base image

When an end-to-end upload is processed on the built image (per the OpenAPI/upload smoke test), the copied static binaries (FFmpeg/ffprobe, yt-dlp, BgUtil POT, Deno, dumb-init) SHALL execute successfully on the .NET 10 base distro, and audio clipping SHALL complete without a missing-library or loader error.

#### Scenario: Copied static tooling runs on the new base image

- **WHEN** an end-to-end upload is processed on the built image (per the OpenAPI/upload smoke test)
- **THEN** the copied static binaries (FFmpeg/ffprobe, yt-dlp, BgUtil POT, Deno, dumb-init) execute successfully on the .NET 10 base distro
- **AND** audio clipping completes without a missing-library or loader error
