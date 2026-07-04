## ADDED Requirements

### Requirement: Deno runtime binary is present in the container image

The container image SHALL include the Deno runtime binary at `/usr/bin/deno`, installed from the official `denoland/deno:bin` multi-arch Docker image. The binary SHALL be owned by `$UID:0` with mode `775` (matching the permission model used for all other static tool binaries), and SHALL be executable by the non-root runtime user.

#### Scenario: Deno binary exists at the expected path

- **WHEN** the container image's filesystem is inspected
- **THEN** `/usr/bin/deno` exists and is a regular executable file
- **AND** its ownership is `$UID:0` with mode `775`

#### Scenario: Deno binary executes on the runtime base distro

- **WHEN** `deno --version` is run inside the built container image
- **THEN** the command exits with code 0
- **AND** the output includes the Deno version string

#### Scenario: Deno is available on PATH for the runtime user

- **WHEN** the container runs as the non-root user (`$UID:0`)
- **AND** a process invokes `deno` without a full path
- **THEN** the Deno binary at `/usr/bin/deno` is found and executed

### Requirement: Deno is sourced from the official multi-arch image

The Deno binary SHALL be copied from the `denoland/deno:bin` Docker image using a `COPY --from` instruction (not downloaded via `curl` or an install script). This ensures multi-architecture support (amd64/arm64) is resolved automatically by the container runtime, and follows the same sourcing pattern used for FFmpeg and bgutil-pot.

#### Scenario: Dockerfile uses COPY --from for Deno

- **WHEN** the `Dockerfile` base stage is inspected
- **THEN** it contains a `COPY --from=docker.io/denoland/deno:bin` (or `denoland/deno:bin`) instruction that copies `/deno` to `/usr/bin/deno`
- **AND** the `COPY` uses `--link`, `--chown=$UID:0`, and `--chmod=775` flags consistent with other tool binary layers
