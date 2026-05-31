## ADDED Requirements

### Requirement: dumb-init runs as PID 1

The container SHALL run `dumb-init` as process ID 1, wrapping the Azure Functions
host startup, so that termination signals are forwarded to the host process and
zombie child processes (for example `yt-dlp`, `ffmpeg`, and `bgutil-pot`) are
reaped. This is required because the base image's startup script launches the
Functions host without `exec`, leaving the host as a non-PID-1 child.

The container's `STOPSIGNAL` SHALL remain `SIGINT` so that runtimes which honor
the image stop signal deliver `SIGINT` to dumb-init, which forwards it to the
application.

dumb-init SHALL use its default process-group signal-forwarding mode. It SHALL
NOT be invoked with `--single-child` unless the base startup script is changed to
`exec` the Functions host, because the script currently launches the host
without `exec` and `--single-child` would forward signals only to the immediate
shell child rather than the host process.

#### Scenario: ENTRYPOINT invokes dumb-init as the init process

- **WHEN** the `Dockerfile` final-stage `ENTRYPOINT` is inspected
- **THEN** the first element is `dumb-init`
- **AND** dumb-init invokes the base image startup path (`/opt/startup/start_nonappservice.sh`)
- **AND** dumb-init is not invoked with `--single-child`

#### Scenario: dumb-init is installed and executable

- **WHEN** the final image filesystem is inspected
- **THEN** `/usr/bin/dumb-init` exists, is executable by the runtime user, and is resolvable on `PATH`

#### Scenario: dumb-init is PID 1 in the running container

- **WHEN** the built image is started and the process at PID 1 is inspected (for example via `/proc/1/comm`)
- **THEN** PID 1 is `dumb-init`

#### Scenario: Stop signal is SIGINT

- **WHEN** the `Dockerfile` `STOPSIGNAL` instruction is inspected
- **THEN** it is `SIGINT`

### Requirement: dumb-init binary is pinned and integrity-verified

The `dumb-init` binary SHALL be obtained from the official `Yelp/dumb-init`
GitHub release at a pinned version, and the build SHALL verify the downloaded
binary against the upstream-published SHA256 checksum for the matching target
architecture. The build SHALL fail if the checksum does not match. The download
and verification SHALL occur in a dedicated build stage, separate from the
runtime/final stage.

The binary selection SHALL be architecture-aware, supporting at least `amd64`
(upstream asset suffix `x86_64`) and `arm64` (upstream asset suffix `aarch64`).

#### Scenario: dumb-init is sourced from a pinned official release

- **WHEN** the `Dockerfile` download stage is inspected
- **THEN** it fetches `dumb-init` from a `https://github.com/Yelp/dumb-init/releases/download/<version>/...` URL
- **AND** the `<version>` is an explicit pinned tag (for example `v1.2.5`), not a moving reference such as `latest`

#### Scenario: Downloaded binary is checksum-verified

- **WHEN** the download stage runs
- **THEN** it computes the SHA256 of the downloaded binary and compares it to the expected per-architecture value
- **AND** a mismatch causes the build to fail (non-zero exit)

#### Scenario: Architecture-aware selection

- **WHEN** the image is built for `linux/amd64`
- **THEN** the `x86_64` dumb-init asset is downloaded and verified
- **WHEN** the image is built for `linux/arm64`
- **THEN** the `aarch64` dumb-init asset is downloaded and verified

### Requirement: dumb-init is decoupled from the FFmpeg tool image

The `dumb-init` binary in the final image SHALL be copied from the dedicated
download stage and SHALL NOT be copied from the FFmpeg static-binary image
(`ghcr.io/jim60105/static-ffmpeg-upx`) or any other unrelated multi-tool image.
This ensures the init binary's version and integrity are independent of FFmpeg
tooling updates.

#### Scenario: No dumb-init copy from the FFmpeg image

- **WHEN** the `Dockerfile` `COPY` instructions are inspected
- **THEN** no instruction copies `/dumb-init` from `ghcr.io/jim60105/static-ffmpeg-upx`
- **AND** the final-stage `dumb-init` is copied `--from` the dedicated download stage
