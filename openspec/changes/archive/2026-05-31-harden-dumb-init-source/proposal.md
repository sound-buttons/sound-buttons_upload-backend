## Why

The container runs `dumb-init` as PID 1 (correct — the base image's
`start_nonappservice.sh` launches the Functions host **without `exec`**, and the
app spawns child processes like `yt-dlp`, `ffmpeg`, and `bgutil-pot` that need
proper signal forwarding and zombie reaping). However, the dumb-init binary is
copied out of an unrelated, UPX-compressed third-party tool image
(`ghcr.io/jim60105/static-ffmpeg-upx`) with **no integrity verification and no
independent version pin**. This already caused a latent bug: ffmpeg/ffprobe were
bumped to `static-ffmpeg-upx:8.1` while dumb-init is still pulled from `:8.0`, so
the init binary's provenance now silently lags the rest of the image. Sourcing a
security-critical PID-1 binary this way is fragile and contrary to supply-chain
best practice.

## What Changes

- Add a dedicated, minimal **download stage** that fetches the official
  `Yelp/dumb-init` **v1.2.5** static binary, selected per target architecture
  (`amd64`/`arm64`), and **verifies its SHA256** against the published checksum;
  the build SHALL fail on mismatch.
- Replace the final-stage `COPY --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0
  /dumb-init` with a `COPY --from=<download stage>` of the verified binary,
  **decoupling dumb-init from the FFmpeg tool image** and removing the reliance
  on a UPX-compressed init.
- Keep dumb-init as PID 1 (`ENTRYPOINT [ "dumb-init", "--", ... ]`) and
  `STOPSIGNAL SIGINT` unchanged — this is a sourcing/hardening change, not a
  behavior change.

## Capabilities

### New Capabilities
- `container-init-process`: Defines the container's PID-1 init contract — that a
  pinned, integrity-verified `dumb-init` runs as PID 1 to forward signals and
  reap zombie child processes, sourced independently of other tool images.

### Modified Capabilities
- None. The existing `dotnet-runtime-platform` spec's "Non-.NET image and tool
  versions are unchanged" requirement mentions the dumb-init source, but that
  requirement is explicitly scoped to the archived **.NET 10 upgrade** ("SHALL
  NOT be altered **by this upgrade**") and remains a true historical record of
  that change. Editing it here would mean either carrying its now-stale
  FFmpeg/curl wording (which drifted via other, unrelated changes) or absorbing
  that drift into this change — both undesirable. Forward-looking governance of
  the dumb-init binary now lives in the new `container-init-process` capability,
  so no delta against `dotnet-runtime-platform` is needed.

## Impact

- **Dockerfile**: new `download` stage; one changed `COPY` in the final stage.
  No change to `ENTRYPOINT`, `STOPSIGNAL`, ports, env, or any other tooling.
- **Image behavior**: functionally identical at runtime — dumb-init still runs as
  PID 1. The binary's origin and integrity change, not its role.
- **Build**: adds a small GitHub download + checksum step (consistent with the
  existing `ADD` of `yt-dlp` from GitHub releases). Current CI builds
  `linux/amd64` only; the arm64 branch future-proofs multi-arch.
- **No application code, API, Helm, or CI-workflow changes.**
