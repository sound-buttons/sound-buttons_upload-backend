## 1. Add Deno binary to the Dockerfile base stage

- [x] 1.1 Add a `COPY --link --chown=$UID:0 --chmod=775 --from=docker.io/denoland/deno:bin /deno /usr/bin/deno` instruction in the `base` stage of `Dockerfile`, immediately after the yt-dlp `ADD` instruction (line 37), with a `# Deno (runtime dependency for yt-dlp)` comment.
- [x] 1.2 Verify that the new `COPY` instruction uses the `--link` flag for layer independence, consistent with the FFmpeg, bgutil-pot, and dumb-init `COPY` instructions.

## 2. Build and verify the image

- [x] 2.1 Build the `base` stage with `podman build --target base -t sound-buttons-deno-test -f Dockerfile .` and confirm the build succeeds.
- [x] 2.2 Run `podman run --rm sound-buttons-deno-test deno --version` and confirm the command exits with code 0 and prints the Deno version string.
- [x] 2.3 Verify the existing tool binaries are not affected: run `podman run --rm sound-buttons-deno-test sh -c "ffmpeg -version && yt-dlp --version && bgutil-pot --version"` and confirm all succeed (or at least don't report missing-library/loader errors).

## 3. Validate CI conformance

- [x] 3.1 Run `hadolint Dockerfile` and confirm no new warnings or errors are introduced by the Deno `COPY` instruction.
- [x] 3.2 Build the full `final` stage with `podman build --target final -t sound-buttons-final-test -f Dockerfile .` and confirm it succeeds end-to-end.

## 4. Update documentation

- [x] 4.1 Update the `AGENTS.md` infrastructure constraints section to mention Deno alongside the existing tool binaries (FFmpeg, yt-dlp, bgutil-pot, dumb-init) so that future changes are aware of the Deno dependency.

