# speech-to-text-transcription Specification

## Purpose

Defines the optional speech-to-text step that auto-fills a button's Japanese name
from the clipped audio using OpenAI Whisper. Implemented in
`SoundButtons/Functions/SpeechToText.cs` (the `SpeechToTextAsync` activity) and
`SoundButtons/Services/OpenAIService.cs`. The step is opt-in, degrades gracefully
on failure, and requires an API key supplied via the `OpenAI_ApiKey` environment
variable.

## Requirements

### Requirement: Opt-in transcription via sentinel name

The `SpeechToTextAsync` activity SHALL invoke transcription only when the
submission's Japanese name field equals the sentinel `[useSTT]`. In that case it
SHALL transcribe the processed audio file with language `ja` and replace the
Japanese name with the transcription text (empty string when no text is returned).
When the field is any other value, the activity SHALL leave it unchanged.

#### Scenario: Sentinel triggers transcription

- **GIVEN** a request whose `NameJP` equals `[useSTT]`
- **WHEN** `SpeechToTextAsync` runs
- **THEN** the audio temp file is transcribed with language `ja`
- **AND** `NameJP` is set to the returned transcription text (or empty string if none)

#### Scenario: Non-sentinel name is preserved

- **GIVEN** a request whose `NameJP` is a normal name (not `[useSTT]`)
- **WHEN** `SpeechToTextAsync` runs
- **THEN** transcription is not invoked and `NameJP` is unchanged

### Requirement: Whisper transcription request shape

When transcribing, the service SHALL POST a multipart request to the OpenAI
`audio/transcriptions` endpoint (base `https://api.openai.com/v1/`) with model
`whisper-1`, `response_format` `verbose_json`, `temperature` `0.1`, a
de-duplication prompt, the audio file content, and (when specified) the target
`language`, authenticated with a bearer token from `OpenAI_ApiKey`. A non-success
HTTP status SHALL raise an error.

#### Scenario: Transcription request parameters

- **GIVEN** a configured API key and an audio file
- **WHEN** `SpeechToTextAsync` (service) posts the request
- **THEN** the request targets `audio/transcriptions` with `model=whisper-1`, `response_format=verbose_json`, `temperature=0.1`, and a `Bearer` authorization header
- **AND** the `language` part is included when a language is specified

### Requirement: API key guard

When the `OpenAI_ApiKey` environment variable is unset or empty, the service SHALL
log a critical message at construction and SHALL short-circuit transcription
calls by returning an empty transcription response instead of calling OpenAI.

#### Scenario: Missing API key short-circuits

- **GIVEN** no `OpenAI_ApiKey` is configured
- **WHEN** transcription is requested
- **THEN** no OpenAI HTTP call is made and an empty transcription response is returned

### Requirement: Graceful degradation on failure

A transcription HTTP failure SHALL NOT fail the overall workflow. The
`SpeechToTextAsync` activity SHALL catch `HttpRequestException`, log the error, and
return the request unchanged so the button is still published (without an
auto-filled Japanese name).

#### Scenario: Transcription error is non-fatal

- **GIVEN** the OpenAI request throws `HttpRequestException`
- **WHEN** `SpeechToTextAsync` runs
- **THEN** the error is logged and the activity returns the request
- **AND** the workflow continues to JSON catalog publishing
