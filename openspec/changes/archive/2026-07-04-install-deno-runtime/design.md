## Context

The Sound Buttons upload backend container image bundles several static tool binaries in its `base` stage: FFmpeg/ffprobe (audio transcoding), yt-dlp (audio downloading), bgutil-pot (YouTube POT token provider + yt-dlp plugin client), and dumb-init (PID 1 init). The bgutil-pot provider requires a Deno runtime to execute its JavaScript-based token-generation logic. Without Deno installed in the container, yt-dlp cannot obtain POT tokens from the bgutil-pot provider, causing YouTube downloads to fail with "Sign in to confirm you're not a bot" errors.

The Dockerfile currently follows a consistent pattern: static tool binaries are added to the `base` stage via `COPY --from=<image>` or `ADD --link` instructions, all with `--chown=$UID:0 --chmod=775` for OpenShift arbitrary-UID compatibility. The image runs as a non-root user (`USER $UID:0`).

## Goals / Non-Goals

**Goals:**
- Install the Deno runtime binary in the container image so that bgutil-pot can generate POT tokens at runtime.
- Follow the existing Dockerfile pattern for adding static tool binaries.
- Maintain multi-architecture support (amd64/arm64).
- Verify the binary executes on the runtime base distro.

**Non-Goals:**
- Upgrading or modifying bgutil-pot, yt-dlp, FFmpeg, or dumb-init.
- Adding Deno to the `test` stage (bgutil-pot is not exercised in tests).
- Pinning a specific Deno version (the `bin` image tag tracks the latest stable release; pinning can be addressed in a follow-up if version drift causes issues).
- Using Deno for any purpose other than the bgutil-pot provider (the .NET application does not call Deno directly).

## Decisions

### Decision 1: Use `COPY --from=denoland/deno:bin` instead of `curl` + install script

**Choice:** Copy the Deno binary from the official `denoland/deno:bin` Docker image using a multi-stage `COPY --from`.

**Alternatives considered:**
- **`curl | sh` install script**: Requires `unzip` (not guaranteed in the base image), runs shell code at build time, and introduces a network dependency mid-build that is harder to cache. The `COPY --from` approach is deterministic, cacheable, and consistent with how FFmpeg, bgutil-pot, and other binaries are already sourced.
- **`ADD` from a GitHub release URL**: Would require architecture-conditional logic (similar to dumb-init's `download` stage). The `denoland/deno:bin` image is already multi-arch, so Docker/Podman resolves the correct architecture automatically.

**Rationale:** The `denoland/deno:bin` variant is a minimal image containing only `/deno`, published as a multi-arch manifest. This is the officially recommended approach for copying Deno into custom images and aligns with the project's existing `COPY --from` pattern.

### Decision 2: Install to `/usr/bin/deno` (not `/usr/local/bin/deno`)

**Choice:** Place the binary at `/usr/bin/deno`.

**Rationale:** All other static tool binaries in the `base` stage (ffmpeg, ffprobe, yt-dlp, bgutil-pot) are installed to `/usr/bin/`. Consistency keeps the Dockerfile predictable and ensures the binary is on `PATH` without additional `ENV` configuration.

### Decision 3: Place the `COPY` in the `base` stage, after yt-dlp

**Choice:** Add the Deno `COPY` immediately after the yt-dlp `ADD` instruction in the `base` stage.

**Rationale:** Deno is a runtime dependency of the bgutil-pot provider, which is itself a plugin for yt-dlp. Grouping the Deno install near yt-dlp and bgutil-pot maintains logical ordering. The `--link` flag ensures this layer is cached independently and can be reordered by BuildKit if beneficial.

## Risks / Trade-offs

- **[Image size increase]** → The Deno binary adds ~40–130 MB to the image. The base image is already ~1 GB+ (Azure Functions runtime), so the relative increase is modest. Mitigation: if size becomes a concern, a future change can pin a specific Deno version and explore UPX compression.
- **[Unpinned Deno version]** → Using `denoland/deno:bin` (latest) means builds at different times may produce images with different Deno versions. Mitigation: bgutil-pot's Deno usage is minimal (POT token generation) and is unlikely to break across Deno minor versions. A follow-up change can pin to a specific tag (e.g., `denoland/deno:bin-2.3.6`) if stability is required.
- **[No integrity verification]** → Unlike dumb-init (which has SHA256 verification), the Deno binary is trusted transitively through the Docker image signature. Mitigation: the official `denoland/deno` images are published by the Deno team and pulled over TLS from Docker Hub. This is the same trust model used for the FFmpeg and bgutil-pot images.
