## 1. Harden the shared audio-only transform

- [x] 1.1 In `SoundButtons/Services/ProcessAudioService.cs`, update
  `TranscodeAudioAsync` so the produced `.webm` is guaranteed audio-only **and**
  codec-valid for any source: select **only the audio stream(s)** of the source
  (e.g. `mediaInfo.AudioStreams` / FFmpeg `-map 0:a`, so video, subtitle, data,
  and attachment streams are all excluded — do not rely on `-map -0:v` alone) and
  **explicitly encode the audio to Opus** (e.g. set the audio stream codec to
  `libopus` / `AudioCodec.libopus`) instead of relying on FFmpeg's default stream
  copy. Confirm the built FFmpeg argument string maps only audio and contains an
  Opus audio encode (not `-c:a copy`).
- [x] 1.2 Keep `TranscodeAudioAsync`'s existing contract: input temp path in,
  `.webm` output path returned (via `Path.ChangeExtension(tempPath, ".webm")`),
  overwrite enabled. The file-upload path keeps calling it unchanged.

## 2. Transform clip downloads to audio-only WebM

- [x] 2.1 In `SoundButtons/Functions/ProcessAudio.cs`, in the
  `else if (!string.IsNullOrEmpty(request.Clip))` branch, after
  `DownloadAudioAsync(tempPath, request.Clip)` returns, add a file-existence
  guard mirroring the video-id branch: if `!File.Exists(tempPath)`, log the
  failure and return `tempPath` (the missing path) without transcoding.
- [x] 2.2 When the downloaded clip file exists, call
  `processAudioService.TranscodeAudioAsync(tempPath)` and return its result, so
  the clip path yields an audio-only `.webm` consistent with the upload and
  video-id paths.

## 3. Spec & validation

- [x] 3.1 Run `openspec validate transcode-clip-to-audio-only --strict` and
  confirm it passes.
- [x] 3.2 Build the project in the .NET 10 SDK container
  (`podman run --rm -v "$PWD":/src:Z -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash -lc 'dotnet build SoundButtons/SoundButtons.csproj'`)
  and confirm it compiles.
- [x] 3.3 Confirm the **runtime image** can encode Opus (not just the dev host):
  build/inspect the final image and verify `ffmpeg -hide_banner -encoders` lists
  `libopus`. The bundled FFmpeg is copied from `ghcr.io/jim60105/static-ffmpeg-upx`,
  so this must be checked there rather than only on the developer machine.
- [x] 3.4 Functional check: submit a **Twitch clip** through the pipeline (or run
  the clip branch directly), then probe the produced temp file with `ffprobe` and
  confirm it has **an audio stream and no video stream**, and that the audio
  codec is Opus in a WebM container. Regression checks (each must still yield an
  audio-only `.webm`): a **YouTube clip**, an **already-WebM/Opus upload** (catch
  re-encode regressions), and an **MP4/AAC upload** (prove the shared transform
  is genuinely codec-robust).

## 4. Commit

- [x] 4.1 Commit the code and OpenSpec change with a conventional message and the
  `Co-authored-by` trailer.
