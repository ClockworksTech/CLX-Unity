# Changelog

## [0.1.0] - Unreleased

First version, written without access to Unity. See "What hasn't been tested" in
the README.

### Added

- `CLXReceiver` component: joins a CLX stream and exposes deck time, metadata,
  mixer state, events, waveforms, beat grids, cues and the clock leader through C#
  events, Inspector events and properties.
- `CLXClient`, the same thing as a Unity-free class.
- `CLXWaveform`, for parsing waveform files, and `CLXWaveformTexture` for drawing them.
- `BeatgridPayload.ExpandBeats`, which turns sparse tempo markers into one entry
  per beat.
- Live status in the receiver's Inspector, and a GameObject menu item.
- Deck Monitor sample.
