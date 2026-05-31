## Why

Sound-button audio is supposed to be **audio-only WebM** end-to-end: the blob is
uploaded with `ContentType = "audio/webm"` and the working file always carries a
`.webm` extension. Two of the three acquisition paths already honor this:

- **YouTube** sources (a `videoId`, including scraped YouTube clips) download with
  `yt-dlp` audio-only format `251/140`, then cut — audio only.
- **File uploads** are run through `TranscodeAudioAsync`, which drops the video
  stream (`-map -0:v`) — audio only.

But the **clip URL path** for non-YouTube clips (in practice **Twitch clips**)
calls the generic `DownloadAudioAsync(tempPath, url)` overload, which sets **no
format selection and performs no transcode**. `yt-dlp` therefore downloads the
clip's best combined stream (video **and** audio) and writes it to the `.webm`
temp path verbatim. The result is a **video-bearing file** that is then uploaded
and served as `audio/webm` — larger than necessary, inconsistent with the
audio-only contract, and exactly the bug reported: "Twitch clips are saved with
video, not audio only."

A second, easy-to-miss detail: Twitch clips are delivered as **AAC audio inside
an MP4/TS container**. AAC cannot be stream-copied into a WebM container, so
simply reusing today's `TranscodeAudioAsync` (which relies on FFmpeg's default
stream **copy**) would fail for Twitch input. The transform must perform a real
audio **re-encode** to a WebM-compatible codec (Opus). The existing upload path
gets away with copy only because browser `MediaRecorder` uploads are already
WebM/Opus.

## What Changes

- **Transform clip downloads to audio-only WebM.** After a successful clip
  download, the acquisition activity SHALL run the downloaded media through the
  audio-only transform (drop video, produce `.webm`) before the pipeline
  continues — making the Twitch/clip path consistent with the upload and
  video-id paths.
- **Make the audio-only transform codec-robust.** The shared transform SHALL
  explicitly re-encode audio to a WebM-compatible codec (**Opus**) instead of
  relying on a stream copy, so AAC/MP4 Twitch clips (and any other source codec)
  produce a valid, playable audio-only `.webm`. This also hardens the existing
  file-upload path against non-WebM uploads.
- **Guard the clip path against failed downloads.** The clip branch SHALL verify
  the downloaded file exists before invoking the transform (mirroring the
  video-id branch), so a failed download skips the transform and returns the
  missing temp path rather than throwing inside FFmpeg.

## Capabilities

### Modified Capabilities

- `audio-acquisition-encoding`: The clip-URL download now produces audio-only
  WebM (previously it stored whatever `yt-dlp` downloaded, including video); the
  shared media-to-WebM transform now explicitly re-encodes audio to Opus so any
  source codec yields a valid audio-only `.webm`; and the clip path now performs
  the same post-download file-existence guard as the video-id path.

## Impact

- **`SoundButtons/Functions/ProcessAudio.cs`**: the `else if (request.Clip)`
  branch gains a file-existence guard and a call to the audio-only transform,
  returning the transformed `.webm` path.
- **`SoundButtons/Services/ProcessAudioService.cs`**: `TranscodeAudioAsync` is
  hardened to force an explicit Opus audio re-encode (and drop video), so it is
  safe to reuse for AAC/MP4 clip input as well as uploads.
- **Behavior**: Twitch (and any future non-YouTube) clip buttons are now stored
  audio-only, matching the `audio/webm` contract; the served files shrink and no
  longer carry a hidden video stream. One extra FFmpeg pass runs for clips — the
  same kind of pass the upload and video-id paths already perform.
- **No changes** to the HTTP API, request/response shape, Helm chart, Dockerfile,
  or CI workflows. No new dependencies (FFmpeg/`libopus` already ship in the
  image).
