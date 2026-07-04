## Why

The `bgutil-pot` provider — which generates YouTube Proof-of-Origin (POT) tokens that yt-dlp needs to bypass "Sign in to confirm you're not a bot" blocks — requires a Deno runtime to execute its JavaScript-based token-generation scripts at runtime. Currently, the container image bundles the `bgutil-pot` binary and its yt-dlp plugin client but does **not** include the Deno runtime, causing POT token generation to fail silently when yt-dlp invokes the provider during YouTube audio downloads.

## What Changes

- **Add the Deno runtime binary to the `base` stage** of the Dockerfile by copying it from the official `denoland/deno:bin` multi-arch image, following the same `COPY --from` pattern already used for FFmpeg, bgutil-pot, and other static tool binaries.
- **Verify the installed Deno binary executes correctly** on the runtime base distro (Debian-based Azure Functions .NET 10 isolated image).

## Capabilities

### New Capabilities
- `deno-runtime`: Installation and availability of the Deno JavaScript/TypeScript runtime in the container image, required as a dependency for the bgutil-pot POT provider used by yt-dlp.

### Modified Capabilities
- `dotnet-runtime-platform`: The Dockerfile's base stage gains a new `COPY --from` layer for the Deno binary. The set of static tool binaries bundled in the runtime image expands to include Deno alongside FFmpeg, yt-dlp, bgutil-pot, and dumb-init.

## Impact

- **Dockerfile (`base` stage)**: One new `COPY --from=denoland/deno:bin` instruction added after the existing yt-dlp `ADD`.
- **Image size**: Increases by the size of the Deno static binary (~40–130 MB depending on architecture and compression).
- **Build time**: Minimal impact — the `denoland/deno:bin` image is small and pulled in parallel with other `COPY --from` sources.
- **No application code changes**: The .NET Functions project, services, and tests are unaffected. Deno is a runtime tool consumed by bgutil-pot, not by the .NET application directly.
- **CI/CD**: The existing `test.yml` and `docker_publish.yml` workflows require no changes; they will automatically pick up the new layer.
