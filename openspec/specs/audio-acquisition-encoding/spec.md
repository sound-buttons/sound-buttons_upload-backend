# audio-acquisition-encoding Specification

## Purpose

Defines how the service obtains and shapes the raw audio for a sound button:
downloading source audio with `yt-dlp` (YoutubeDLSharp), trimming a time range and
transcoding to WebM with FFmpeg (Xabe.FFmpeg), and locating the bundled `yt-dlp`
and `ffmpeg` executables at runtime. Implemented in
`SoundButtons/Services/ProcessAudioService.cs`, `SoundButtons/Functions/ProcessAudio.cs`,
and `SoundButtons/Helper/YoutubeDLHelper.cs`. All produced audio is normalized to
the `.webm` container.

## Requirements

### Requirement: Download audio from a YouTube video id

When a submission carries a `Source.VideoId`, the service SHALL download the audio
from `https://youtu.be/<videoId>` using `yt-dlp` with audio-only format
selection `251/140`, certificate checking disabled, the YouTube `skip=dash`
extractor argument, and a download-sections argument restricting the download to
the `Start`–`End` time range. A missing/empty `VideoId` SHALL raise an argument
error.

#### Scenario: Section download for a video id

- **GIVEN** a `Source` with a non-empty `VideoId` and a `Start`/`End` range
- **WHEN** `DownloadAudioAsync` runs
- **THEN** `yt-dlp` is invoked for `https://youtu.be/<videoId>` with format `251/140` and a `*Start-End` download section
- **AND** the audio is written to the provided temp path

### Requirement: Download audio from a clip URL

When a submission carries a clip URL instead of a video id, the service SHALL
download from that URL with `yt-dlp` (certificate checking disabled) to the temp
path. An empty URL SHALL raise an argument error.

#### Scenario: Clip URL download

- **GIVEN** a non-empty clip URL and an empty `Source.VideoId`
- **WHEN** the acquisition activity runs
- **THEN** `DownloadAudioAsync(tempPath, url)` downloads the clip audio to the temp path

### Requirement: Guard against failed downloads

For a video-id source, the acquisition activity SHALL verify the audio file exists
immediately after the download attempt and before cutting; if the file is absent
(download failed), it SHALL skip cutting and return the (missing) temp path. For a
clip-URL source the activity does not perform this immediate check; the missing
file is instead caught by the orchestrator's file-existence guard, which aborts
the workflow.

#### Scenario: Video-id download failure is detected before cutting

- **GIVEN** a video-id download that produced no file at the temp path
- **WHEN** `ProcessAudioAsync` checks for the file
- **THEN** it logs the failure and returns without attempting to cut the audio

#### Scenario: Clip download failure is caught by the orchestrator

- **GIVEN** a clip-URL download that produced no file
- **WHEN** the orchestrator resumes after acquisition
- **THEN** its file-existence guard detects the missing file and aborts the workflow

### Requirement: Trim a downloaded clip to its duration

For a video-id source, after downloading the section the service SHALL cut the
audio to the requested duration (`End - Start`) using FFmpeg with an
`-sseof -<duration>` pre-input seek, writing a `.webm` output that replaces the
working temp file.

#### Scenario: Cut to requested length

- **GIVEN** a downloaded audio file and a `Source` duration
- **WHEN** `CutAudioAsync` runs
- **THEN** FFmpeg trims the tail `duration` seconds via `-sseof -<duration>` and overwrites the temp file with the `.webm` result

### Requirement: Transcode uploaded audio to WebM without video

For a direct file upload, the service SHALL transcode the uploaded media to a
`.webm` audio file, explicitly dropping any video stream (`-map -0:v`), and return
the new `.webm` path.

#### Scenario: Uploaded file normalized to webm audio

- **GIVEN** an uploaded media file at a temp path
- **WHEN** `TranscodeAudioAsync` runs
- **THEN** FFmpeg produces a `.webm` output that excludes the video stream
- **AND** the returned path has the `.webm` extension

### Requirement: Runtime tool discovery

The service SHALL locate the `yt-dlp` and `ffmpeg` executables at startup by
searching the current directory, the temp directory, and each `PATH` entry (using
`PATHEXT` extensions where applicable), and SHALL configure FFmpeg with the
discovered executables directory.

#### Scenario: Executables resolved from PATH

- **GIVEN** `yt-dlp` and `ffmpeg` are present on `PATH` (or the current/temp directory)
- **WHEN** the service initializes
- **THEN** their full paths are resolved and FFmpeg's executables path is set to the `ffmpeg` directory
