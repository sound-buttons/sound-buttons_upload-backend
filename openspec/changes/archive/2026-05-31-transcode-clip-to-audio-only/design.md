## Context

Audio acquisition runs inside the `ProcessAudioAsync` activity
(`SoundButtons/Functions/ProcessAudio.cs`), which delegates to
`ProcessAudioService` (`SoundButtons/Services/ProcessAudioService.cs`). There are
three acquisition shapes, decided earlier in the HTTP trigger
(`SoundButtons/Functions/SoundButtons.cs`):

1. **File upload** — handled before the orchestrator starts, in
   `ProcessAudioFromFileUpload`, which writes the uploaded bytes to a temp file
   and calls `TranscodeAudioAsync(tempPath)`. That method drops the video stream
   (`-map -0:v`) and writes a `.webm`. Result: **audio only.**

2. **Video-id source** (a YouTube `videoId`, including YouTube clips, which
   `ProcessYoutubeClip` scrapes into a `videoId` + `Start`/`End`). In
   `ProcessAudioAsync` this takes the `Source.VideoId` branch:
   `DownloadAudioAsync(tempPath, source)` downloads with `yt-dlp` audio-only
   format `251/140`, then `CutAudioAsync` trims. Result: **audio only.**

3. **Clip URL** for non-YouTube clips — in practice **Twitch clips**.
   `ProcessTwitchClip` clears `Source.VideoId` and leaves the clip URL on
   `request.Clip`. In `ProcessAudioAsync` this takes the
   `else if (!string.IsNullOrEmpty(request.Clip))` branch:
   `DownloadAudioAsync(tempPath, request.Clip)`. That overload sets only
   `NoCheckCertificates` and `Output`; it has **no `Format` and no transcode**,
   so `yt-dlp` downloads the clip's best combined (video+audio) stream and writes
   it as-is. Result: **video + audio**, mislabeled `.webm`.

Downstream, `UploadAudioToStorageAsync` always uploads with
`ContentType = "audio/webm"`, and both it and `ProcessJsonFile` derive the blob
extension from `Path.GetExtension(request.TempPath)`. The whole pipeline assumes
the working file is an audio-only `.webm`. The clip path violates that assumption.

A subtle codec constraint: Twitch clips deliver **AAC audio in an MP4/TS
container**. AAC is not a valid WebM audio codec, so an FFmpeg stream **copy**
into a `.webm` container fails. Today's `TranscodeAudioAsync` adds the source
streams via Xabe.FFmpeg's `AddStream(...)` without `SetCodec`, which emits a
codec **copy**. That works for the upload path only because browser
`MediaRecorder` already produces WebM/Opus. Reusing it unchanged for Twitch input
would error out.

## Goals / Non-Goals

**Goals:**

- The clip-URL (Twitch) acquisition path produces an **audio-only `.webm`**,
  consistent with the upload and video-id paths and the `audio/webm` contract.
- The audio-only transform works for **any source container/codec** (notably
  AAC/MP4), not just already-WebM input.
- A failed clip download is handled gracefully (no FFmpeg crash on a missing
  file), matching the video-id path's guard.

**Non-Goals:**

- No change to time-range trimming for clips. Twitch clips arrive with
  `Start = End = 0` and the whole clip is the intended sound; this change keeps
  using the full clip.
- No change to the HTTP API, request/response models, orchestration shape, Helm
  chart, Dockerfile, or CI.
- No change to YouTube (video-id / YouTube-clip) or file-upload acquisition
  behavior, beyond the upload path benefiting from the now codec-robust transform.
  In particular, `CutAudioAsync` (the video-id trim step) is **left as-is**: it
  still adds the downloaded streams without an explicit audio codec. This is safe
  because that path downloads audio-only format `251/140`; if `yt-dlp` ever fell
  back to `140` (AAC/M4A) and tried to write `.webm`, that is a separate,
  pre-existing concern out of scope for this change.

## Decisions

### Decision 1: Transform after download, reusing a single audio-only primitive

After the clip download succeeds, the activity runs the downloaded file through
the **same** "produce audio-only WebM" transform used by the upload path
(`TranscodeAudioAsync`), and returns the transformed `.webm` path. Keeping one
shared primitive avoids divergent behavior between the upload and clip paths and
keeps the `.webm` extension/`audio/webm` contract centralized.

The transform is applied **after** `yt-dlp` finishes, rather than asking `yt-dlp`
to extract audio itself, because a post-download FFmpeg pass keeps the output
filename/extension under our control (`.webm`), which the blob-upload and
JSON-update activities depend on.

### Decision 2: Force an explicit Opus re-encode in the shared transform

`TranscodeAudioAsync` is hardened to **map only the source audio stream(s)**
(e.g. `mediaInfo.AudioStreams` / `-map 0:a`, excluding video, subtitle, data, and
attachment streams — not merely `-map -0:v`) and to **explicitly set the audio
codec to Opus** (`libopus`) rather than relying on FFmpeg's default stream copy. This guarantees
a valid, playable audio-only `.webm` regardless of the source codec — including
AAC/MP4 Twitch clips — and additionally hardens the upload path against any
non-WebM upload a client might send. WebM's standard audio codec is Opus, so this
is the natural target. The cost is one real audio encode (already the norm for
the video-id and upload paths); for a low-traffic, pre-release service this is
acceptable and is strictly more correct than a copy that can fail.

### Decision 3: Guard the clip path before transforming

The clip branch verifies the downloaded file exists immediately after the
download (as the video-id branch already does) and, if absent, logs and returns
the missing temp path **without** invoking FFmpeg. This prevents
`FFmpeg.GetMediaInfo` from throwing on a non-existent file and preserves the
existing "orchestrator aborts on a missing file" failure mode.

## Alternatives Considered

- **Let `yt-dlp` extract audio (`-x --audio-format opus`, i.e.
  `ExtractAudio` + `AudioConversionFormat.Opus`).** This removes the explicit
  second FFmpeg call, but `yt-dlp`'s post-processor renames the output to
  `.opus`, breaking the `.webm` extension that `UploadAudioToStorageAsync` and
  `ProcessJsonFile` derive via `Path.GetExtension`. Working around that (forced
  remux/rename) adds more moving parts than the post-download transcode. Rejected.

- **Add audio-only `Format` selection (`bestaudio/best`) to the generic
  `DownloadAudioAsync(url)` overload, without a transcode.** For Twitch the
  available formats are typically a single combined (audio+video) stream, so
  `bestaudio/best` falls back to the combined stream and still yields video.
  Even when a separate audio stream exists, it may be AAC/MP4 rather than WebM,
  still violating the `.webm`/Opus contract. Rejected in favor of an explicit
  transcode that guarantees the output shape.

- **A dedicated clip-only transcode method, leaving `TranscodeAudioAsync`
  copy-based.** This duplicates the transform and leaves the upload path silently
  fragile to non-WebM uploads. Hardening the single shared method is simpler and
  strictly safer. Rejected.

## Risks / Trade-offs

- **Extra CPU per clip.** Each Twitch clip now incurs one FFmpeg audio encode.
  This matches what the upload and video-id paths already do and is negligible at
  current volume.
- **Re-encode of already-Opus uploads.** Forcing an Opus encode on the upload
  path means already-Opus uploads are re-encoded (slightly lossy / more CPU)
  rather than copied. Acceptable for correctness and uniformity; WebM/Opus in →
  Opus out is a near-transparent re-encode. If this ever matters, a follow-up
  could conditionally copy when the source is already Opus.
- **`libopus` availability.** The transform depends on `libopus` in the bundled
  FFmpeg. The image's FFmpeg is a full static build that includes Opus, so this
  holds; the validation tasks confirm a real Twitch clip transcodes successfully.
