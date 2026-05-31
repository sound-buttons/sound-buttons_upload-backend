## RENAMED Requirements

- FROM: `### Requirement: Transcode uploaded audio to WebM without video`
- TO: `### Requirement: Transcode media to audio-only WebM without video`

## MODIFIED Requirements

### Requirement: Download audio from a clip URL

The service SHALL, when a submission carries a clip URL instead of a video id (in
practice a non-YouTube clip such as a Twitch clip), download from that URL with
`yt-dlp` (certificate checking disabled) to the temp path, and SHALL then
transform the downloaded media into an **audio-only `.webm`** before the workflow
continues, so that the stored button audio never retains a video stream. An empty
URL SHALL raise an argument error.

The transform SHALL reuse the shared media-to-WebM audio-only transform so that
the clip path is consistent with the file-upload and video-id paths and with the
`audio/webm` storage contract.

#### Scenario: Clip URL download

- **GIVEN** a non-empty clip URL and an empty `Source.VideoId`
- **WHEN** the acquisition activity runs
- **THEN** `DownloadAudioAsync(tempPath, url)` downloads the clip to the temp path

#### Scenario: Downloaded clip is reduced to audio-only WebM

- **GIVEN** a clip download that produced a file containing both video and audio (for example a Twitch clip)
- **WHEN** the acquisition activity continues after the download
- **THEN** the media is transformed to an audio-only `.webm` (the video stream is dropped)
- **AND** the path returned by the activity is the audio-only `.webm` file

### Requirement: Guard against failed downloads

After each download attempt, the acquisition activity SHALL verify the audio file
exists before performing any FFmpeg step (cutting for a video-id source,
transforming for a clip-URL source). For a video-id source, if the file is absent
it SHALL skip cutting and return the (missing) temp path. For a clip-URL source,
if the file is absent it SHALL skip the audio-only transform and return the
(missing) temp path. In both cases the missing file is subsequently caught by the
orchestrator's file-existence guard, which aborts the workflow.

#### Scenario: Video-id download failure is detected before cutting

- **GIVEN** a video-id download that produced no file at the temp path
- **WHEN** `ProcessAudioAsync` checks for the file
- **THEN** it logs the failure and returns without attempting to cut the audio

#### Scenario: Clip download failure is detected before transforming

- **GIVEN** a clip-URL download that produced no file at the temp path
- **WHEN** `ProcessAudioAsync` checks for the file
- **THEN** it logs the failure and returns without attempting the audio-only transform

#### Scenario: Missing file is caught by the orchestrator

- **GIVEN** an acquisition activity that returned a temp path with no file present
- **WHEN** the orchestrator resumes after acquisition
- **THEN** its file-existence guard detects the missing file and aborts the workflow

### Requirement: Transcode media to audio-only WebM without video

The service SHALL provide a shared transform that converts an arbitrary input
media file into an audio-only `.webm` by mapping **only the audio stream(s)** of
the source (so video, subtitle, data, and attachment streams are all excluded)
and explicitly re-encoding the audio to a WebM-compatible codec (Opus) rather
than copying the source stream, and SHALL return the new `.webm` path.
Re-encoding (instead of stream copy) ensures the output is valid for any source
container/codec, including AAC in MP4/TS (as delivered by Twitch clips). This
transform SHALL be used by both the direct file-upload path and the clip-URL
acquisition path.

#### Scenario: Media normalized to audio-only WebM

- **GIVEN** an input media file at a temp path (an uploaded file or a downloaded clip)
- **WHEN** the shared transform runs
- **THEN** FFmpeg maps only the source audio stream(s) and excludes any video (and other non-audio) streams
- **AND** the audio is encoded with a WebM-compatible codec (Opus)
- **AND** the returned path has the `.webm` extension

#### Scenario: Non-WebM source codec is re-encoded, not copied

- **GIVEN** an input whose audio is AAC in an MP4/TS container (for example a Twitch clip)
- **WHEN** the shared transform runs
- **THEN** the audio is re-encoded to Opus so the `.webm` output is valid and playable
- **AND** the transform does not rely on a stream copy that would fail for that container/codec
