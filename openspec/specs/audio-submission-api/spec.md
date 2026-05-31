# audio-submission-api Specification

## Purpose

Defines the public HTTP surface of the `sound-buttons_upload-backend` service:
the anonymous `sound-buttons` submission endpoint that accepts an audio-button
submission (a YouTube/Twitch source reference or an uploaded audio file), resolves
and validates the source, starts the durable processing workflow, and returns a
status-polling response; plus the `healthz` liveness endpoint. Implemented in
`SoundButtons/Functions/SoundButtons.cs` (HTTP trigger `HttpStart`) and
`SoundButtons/Functions/Utility.cs` (`Healthz`).

## Requirements

### Requirement: Submission endpoint accepts multipart form-data

The service SHALL expose an HTTP-triggered function named `sound-buttons` at
`AuthorizationLevel.Anonymous` accepting `POST`. The request body SHALL be
`multipart/form-data`; the function SHALL parse both simple key/value form fields
and an optional uploaded file section. Requests whose `Content-Type` header does
not contain `multipart/form-data` SHALL be rejected with HTTP 400.

#### Scenario: Non-multipart request is rejected

- **GIVEN** a `POST` to `sound-buttons` with a `Content-Type` other than `multipart/form-data`
- **WHEN** the request is received
- **THEN** the function returns HTTP 400 with body `Invalid content type`
- **AND** no orchestration is started

#### Scenario: Multipart form is parsed into fields and file

- **GIVEN** a `multipart/form-data` request containing form fields and file part(s)
- **WHEN** the function parses the body
- **THEN** each non-file `form-data` section is read as a UTF-8 string keyed by its field name
- **AND** each file part is read into a byte array keyed by its field name
- **AND** when multiple file parts are present only the first is size-checked and processed (the code does not reject extra files)

### Requirement: Source resolution from submission inputs

The function SHALL resolve the audio source from the form inputs in the following
forms: a YouTube `videoId` (raw 11-character id or a full YouTube URL from which
the id is extracted), a YouTube clip URL (whose target `videoId` and start/end
times are scraped from the clip page HTML), or a Twitch clip URL (passed through
without a `videoId`). A YouTube URL that yields no extractable id SHALL be
discarded (treated as no source).

#### Scenario: Raw YouTube video id with start/end

- **GIVEN** form fields `videoId` (11-char id), `start`, and `end` parse as numbers
- **WHEN** the source is built
- **THEN** the `Source.VideoId`, `Source.Start`, and `Source.End` are populated from those fields

#### Scenario: Full YouTube URL is reduced to its video id

- **GIVEN** a `videoId` field that begins with `http` and contains a valid YouTube URL
- **WHEN** the source is built
- **THEN** the 11-character video id is extracted from the URL
- **AND** an unrecognized URL results in an empty `VideoId` with `Start`/`End` reset to 0

#### Scenario: YouTube clip URL is scraped for target video and times

- **GIVEN** a `clip` field matching a YouTube clip URL
- **WHEN** the clip page is fetched
- **THEN** `startTimeMs`/`endTimeMs` from the page `clipConfig` set `Source.Start`/`Source.End` (converted from milliseconds to seconds)
- **AND** the page `videoId` sets `Source.VideoId`

#### Scenario: Twitch clip URL is passed through

- **GIVEN** a `clip` field matching a Twitch clip URL
- **WHEN** the clip is processed
- **THEN** the clip string is retained for download
- **AND** `Source.VideoId` is empty with `Start`/`End` set to 0

### Requirement: Submission input validation

The function SHALL reject submissions that provide no source value — that is, an
empty resolved `Source.VideoId`, no `clip` field value, and no uploaded file —
with HTTP 400. (A non-empty `clip` value is accepted at this stage even if it
matches neither a YouTube nor a Twitch clip; such an unusable clip is not rejected
here and instead fails later in the workflow.) An uploaded file larger than 30 MB
SHALL be rejected with HTTP 400. When a `Source.VideoId` resolved from the
`videoId`/YouTube-URL inputs is present, the clip duration (`End - Start`) SHALL be
greater than 0 and at most 180 seconds; an out-of-range duration SHALL fail the
request. (This duration check runs before YouTube-clip scraping, so durations
derived from a scraped YouTube clip are not subject to it.)

#### Scenario: No source provided

- **GIVEN** a submission with empty `videoId`, no `clip`, and no file
- **WHEN** the request is validated
- **THEN** the function returns HTTP 400 with body `No source found`

#### Scenario: Uploaded file exceeds size limit

- **GIVEN** an uploaded file larger than 30 MB (30 * 1024 * 1024 bytes)
- **WHEN** the request is validated
- **THEN** the function returns HTTP 400 with body `File size over 30MB`

#### Scenario: Clip duration out of range

- **GIVEN** a `Source.VideoId` resolved from the `videoId`/YouTube-URL input with `End - Start <= 0` or `End - Start > 180`
- **WHEN** the source is checked (before any YouTube-clip scraping)
- **THEN** the request fails (an exception is raised) and no button is published

### Requirement: Output filename derivation

The function SHALL derive the stored filename from the submitted `nameZH`,
stripping characters that are not alphanumeric or letters of any language
(multi-byte CJK characters are preserved). When the sanitized result is empty,
a GUID SHALL be used as the filename.

#### Scenario: Sanitized name used as filename

- **GIVEN** a `nameZH` containing letters/CJK plus punctuation
- **WHEN** the filename is derived
- **THEN** disallowed characters are removed and the remaining name is used

#### Scenario: Empty sanitized name falls back to GUID

- **GIVEN** a `nameZH` that sanitizes to an empty string (or is absent)
- **WHEN** the filename is derived
- **THEN** a newly generated GUID (`N` format) is used as the filename

### Requirement: Workflow kickoff and status response

After validation, the function SHALL start a new `main-sound-buttons`
orchestration with a unique instance id, passing the resolved submission
(directory defaulting to `test`, group defaulting to `未分類`, volume defaulting to
1, plus names, IP from `X-Forwarded-For`, source/clip, and any pre-processed
uploaded-audio temp path). It SHALL return the Durable Functions
check-status response so the client can poll instance status.

#### Scenario: Orchestration started and status returned

- **GIVEN** a valid submission
- **WHEN** the function schedules the orchestration
- **THEN** a `main-sound-buttons` instance is created with a unique `InstanceId`
- **AND** the HTTP response is the durable check-status response containing the instance status URLs

### Requirement: Health check endpoint

The service SHALL expose an anonymous `GET healthz` endpoint that returns HTTP
200 without side effects, for container/orchestrator liveness probing.

#### Scenario: Health probe succeeds

- **GIVEN** a running Functions host
- **WHEN** `GET /api/healthz` is called
- **THEN** the response status is HTTP 200 (OK)
