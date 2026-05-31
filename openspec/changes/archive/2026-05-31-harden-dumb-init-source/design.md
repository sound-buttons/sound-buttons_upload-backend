## Context

The backend container is built from a multi-stage `Dockerfile`. The runtime base
is `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0`,
whose default `CMD` is `/opt/startup/start_nonappservice.sh`. That script does:

```sh
source /opt/startup/install_ca_certificates.sh
/azure-functions-host/Microsoft.Azure.WebJobs.Script.WebHost
```

It launches the host **without `exec`**, so without an init the host would run as
a child of the shell (PID 1), and child processes the app spawns (`yt-dlp`,
`ffmpeg`, `bgutil-pot`) would not be reliably reaped or signalled. The Dockerfile
therefore overrides the entrypoint:

```
ENTRYPOINT [ "dumb-init", "--", "/opt/startup/start_nonappservice.sh" ]
STOPSIGNAL SIGINT
```

This part is correct and stays. The problem is **where the dumb-init binary comes
from**:

```
COPY --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /dumb-init /usr/bin/
```

- It is pulled out of an unrelated, UPX-compressed multi-tool image.
- There is no integrity (checksum) verification.
- Its version is implicit and tied to an FFmpeg image tag. FFmpeg was bumped to
  `:8.1` (ffmpeg/ffprobe COPYs) but the dumb-init COPY still says `:8.0` — a
  silent provenance drift for a PID-1 binary.

The `containerfile-creator` skill prescribes a "Secure dumb-init Usage" pattern:
download the official static binary in a dedicated stage and verify its SHA256.

## Goals / Non-Goals

**Goals:**
- Source dumb-init from the official `Yelp/dumb-init` release, pinned to an
  explicit version, with SHA256 integrity verification.
- Decouple the init binary from the FFmpeg tool image (fixing the version-drift
  bug and removing the UPX-compressed init).
- Preserve the existing PID-1 behavior (`dumb-init -- start_nonappservice.sh`,
  `STOPSIGNAL SIGINT`) exactly.

**Non-Goals:**
- Removing or replacing dumb-init (it is the right tool and stays).
- Changing the FFmpeg/yt-dlp/BgUtil/curl sources or versions, the `ENTRYPOINT`
  target, ports, env, or any application behavior.
- Reconciling other pre-existing archived-spec drift (the removed `HEALTHCHECK`/
  `curl`, and the FFmpeg `8.0`→`8.1` bump) — those belong to their own changes.
- Enabling multi-arch CI (the arm64 path is authored for correctness/future use,
  but CI continues to build `linux/amd64` only).

## Decisions

### Decision 1: Keep dumb-init as PID 1 (do not remove it)

Rationale: the base startup script does not `exec` the host, and the workload
forks short-lived children (`yt-dlp`, `ffmpeg`, `bgutil-pot`). A real init that
reaps zombies and forwards signals is valuable. Removing it would regress signal
handling and risk zombie accumulation in a long-lived Functions container.

Alternatives considered:
- *Rely on the runtime's `--init` (tini).* Rejected — Kubernetes (the deployment
  target via the Helm chart) does not inject a docker `--init`; the init must be
  baked into the image.

### Decision 2: Download the official binary in a dedicated stage with SHA256 verification

Add a small `download` stage that reuses the **same base image** as the runtime
stage rather than pulling an unrelated distro image just to obtain `curl`. The
runtime base (`mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0`,
Ubuntu 24.04) already ships `/usr/bin/curl` and `ca-certificates` (verified), so
no package installation is needed. To keep a single source of truth and avoid the
image reference drifting between stages, the base image is pinned once via a
global `ARG BASE_IMAGE` and referenced by both the `base` and `download` stages.

The `download` stage is declared `FROM --platform=$BUILDPLATFORM ${BASE_IMAGE}`
so the download/checksum `RUN` executes natively on the builder rather than under
emulation. It references the **raw** base image (not the `base` *stage*), so it
does not depend on the FFmpeg/yt-dlp/BgUtil layers and the dumb-init download
stays decoupled from that tooling. It declares `ARG TARGETARCH`, selects the
dumb-init asset by `TARGETARCH` (the runtime architecture, **not** `BUILDARCH`),
downloads it from the pinned official release URL with the already-present `curl`,
and verifies its SHA256 with `sha256sum -c`. The final stage then copies the
verified binary.

Pinned version: **v1.2.5** (the latest `Yelp/dumb-init` release; the project has
had no newer release since 2021). The release also publishes a `sha256sums`
asset; the verified upstream SHA256 values are:
- `amd64` (`x86_64`): `e874b55f3279ca41415d290c512a7ba9d08f98041b28ae7c2acb19a545f1c4df`
- `arm64` (`aarch64`): `b7d648f97154a99c539b63c55979cd29f005f88430fb383007fe3458340b795e`

(Both confirmed by downloading the assets and hashing them during proposal
research; the binary is a statically-linked, stripped ELF.)

Rationale: integrity verification + explicit version pin + independence from the
FFmpeg image. A static binary needs no runtime libraries on the .NET base. The
default dumb-init mode (process-group signal forwarding, **no** `--single-child`)
is required because the base startup script does not `exec` the host.

Alternatives considered:
- *Just bump the existing COPY to `static-ffmpeg-upx:8.1`.* Fixes the drift but
  keeps the opaque source, no checksum, and a UPX-compressed init. Rejected — it
  does not address the root supply-chain concern.
- *Pull a separate minimal distro image (e.g. `debian:bookworm-slim`) for the
  download stage and `apt-get install curl ca-certificates`.* Rejected — it pulls
  an unrelated image and adds an apt layer purely to obtain `curl`, which the
  existing base image already provides. Reusing the pinned base image is leaner
  and keeps the build's image set minimal.
- *Install via `apt-get install dumb-init` in the final stage.* Adds package
  layers and apt metadata to the runtime image, pulls a dynamically-linked build,
  and depends on the distro repo's version. The verified static binary is leaner
  and version-explicit. Rejected.
- *Use BuildKit `ADD --checksum=sha256:<hash> <url> <dest>`.* This can verify a
  single fixed remote URL without curl, and would be cleaner for an
  amd64-only build. But it is awkward for **per-architecture** URL/checksum
  selection in one stage (the URL and expected hash both vary by arch, and there
  is no built-in unsupported-arch failure). The `RUN case "$TARGETARCH"` approach
  keeps the arch mapping, URL, checksum, and unsupported-arch failure all
  explicit, so it is preferred here.

### Decision 3: Architecture-aware selection via `TARGETARCH`

Use a `case "${TARGETARCH}"` to map `amd64`→`x86_64` and `arm64`→`aarch64`, each
with its expected SHA256, failing on unsupported architectures. This keeps the
single Dockerfile correct for any future multi-arch build while today's amd64-only
CI is unaffected.

## Risks / Trade-offs

- **Build-time network dependency on GitHub releases.** → Mitigation: identical to
  the existing `ADD … yt-dlp_linux` step; pinned URL + checksum make it
  deterministic and tamper-evident. A registry/network outage fails the build
  loudly rather than shipping a bad binary.
- **Pinned v1.2.5 could miss a future security fix.** → Mitigation: it is the
  current latest; the pin is explicit and trivially bumpable (URL + two SHA256s).
- **SHA256 values become stale if upstream re-tags or replaces an asset.** →
  Mitigation: GitHub allows maintainers to replace release assets, but the build
  pins the expected SHA256, so any asset change fails verification and the build
  rather than silently shipping a different binary — which is the desired safety
  behavior.
- **Extra `download` stage.** → Negligible: it reuses the already-pinned base
  image (no additional unrelated image is pulled; cross-arch builds may pull the
  builder-platform variant of the same base image) and produces only a
  ~tens-of-KB binary copied via `--link`; it does not bloat the final image.

## Migration Plan

1. Add the `download` stage and switch the final-stage dumb-init `COPY`.
2. Build the image (amd64) and verify: the build runs the checksum step; PID 1 is
   `dumb-init`; the container starts and `/api/healthz` responds.
3. Negative check: tampering with the expected hash makes the build fail.

Rollback: revert the Dockerfile change (restore the `COPY --from=static-ffmpeg-upx`
line). No data or API impact.

## Open Questions

None.
