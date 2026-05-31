# audio-processing-workflow Specification

## Purpose

Defines the durable orchestration that turns a validated submission into a
published sound button. The `main-sound-buttons` orchestrator
(`SoundButtons/Functions/SoundButtons.cs`, `RunOrchestrator`) coordinates the
activity functions — audio acquisition, blob upload, optional speech-to-text, and
JSON catalog update — in a fixed order, guards against a missing audio file,
cleans up temporary files, and correlates all steps by orchestration instance id.
The Functions host enforces a per-invocation timeout.

## Requirements

### Requirement: Orchestrator coordinates the activity chain in order

The `main-sound-buttons` orchestrator SHALL execute the processing steps in this
order: (1) acquire audio via the `ProcessAudioAsync` activity **only when** the
submission did not already provide a pre-processed uploaded-audio temp path;
(2) `UploadAudioToStorageAsync`; (3) `SpeechToTextAsync`; (4) `ProcessJsonFile`.
Each activity SHALL receive and (where applicable) return the evolving `Request`
state.

#### Scenario: Source-based submission acquires audio first

- **GIVEN** an orchestration input whose `TempPath` is empty
- **WHEN** the orchestrator runs
- **THEN** it calls `ProcessAudioAsync` to produce the audio temp path before proceeding

#### Scenario: Pre-uploaded audio skips acquisition

- **GIVEN** an orchestration input whose `TempPath` is already set (file upload path)
- **WHEN** the orchestrator runs
- **THEN** it does not call `ProcessAudioAsync` and proceeds directly to upload

#### Scenario: Successful end-to-end run

- **GIVEN** a valid audio temp file exists after acquisition
- **WHEN** the orchestrator runs to completion
- **THEN** it calls `UploadAudioToStorageAsync`, then `SpeechToTextAsync`, then `ProcessJsonFile`
- **AND** it deletes the temp file and returns `true`

### Requirement: Missing audio file aborts the workflow

The orchestrator SHALL abort the workflow when, after the acquisition step, the
audio temp file does not exist on disk — without uploading or publishing — and
SHALL perform temp-file cleanup and return `false`.

#### Scenario: Download produced no file

- **GIVEN** the acquisition step did not produce a file at `TempPath`
- **WHEN** the orchestrator checks for the file
- **THEN** it logs an error, runs cleanup, and returns `false`
- **AND** no blob upload or JSON catalog update occurs

### Requirement: Temporary file cleanup

The orchestrator SHALL delete the working audio temp file at the end of both the
success path and the missing-file abort path, so transient audio data is not
retained after processing.

#### Scenario: Temp file removed on completion

- **GIVEN** the workflow has finished (success or missing-file abort)
- **WHEN** the orchestrator returns
- **THEN** the temp audio file at `TempPath` has been deleted

### Requirement: Instance-id correlation

Every orchestrator and activity invocation SHALL push the orchestration
`InstanceId` into the logging context so all log entries for one submission are
correlatable by instance id.

#### Scenario: Logs carry the instance id

- **GIVEN** an orchestration with a given `InstanceId`
- **WHEN** the orchestrator and its activities log
- **THEN** each log entry is enriched with that `InstanceId` property

### Requirement: Bounded execution time

The Functions host SHALL enforce a function timeout of 10 minutes
(`functionTimeout` in `SoundButtons/host.json`) so a stuck submission cannot run
unbounded.

#### Scenario: Configured timeout

- **WHEN** `host.json` is inspected
- **THEN** `functionTimeout` equals `00:10:00`
