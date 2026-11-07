# CLX for Unity

Receive a live [CLX](https://github.com/medcelerate/CLX-Spec) stream from Clockworks
senders in Unity: deck time, track metadata, mixer state, events, waveforms, beat
grids and cues. It's the Unity counterpart of
[CLX-UE](https://github.com/medcelerate/CLX-UE), the Unreal Engine plugin.

> **Not yet run in Unity.** This version was written without Unity installed. The
> protocol layer is well tested outside it, but the component, Editor code and
> sample scene have never been opened in the Editor. See
> [What hasn't been tested](#what-hasnt-been-tested).

Requires Unity 2021.3 or later. It has no package dependencies.

## Install

**From a git URL:** in **Window → Package Manager**, choose **+ → Add package from
git URL** and enter:

```text
https://github.com/ClockworksTech/CLX-Unity.git
```

Add `#v0.1.0` to the URL to pin a version.

**From a local copy:** choose **+ → Add package from disk** and pick `package.json`
in this folder.

To see the sample, select **CLX** in the Package Manager and import **Deck Monitor**
from its **Samples** tab. Open the `DeckMonitor` scene and press Play with a CLX
sender running on your network. If the scene won't open, add `CLXDeckMonitor` to an
empty GameObject instead: it brings its own receiver.

## Quick start

Add a **CLX Receiver** to a GameObject (**GameObject → Clockworks → CLX Receiver**).
It starts listening on port 3650 when enabled. Then read its state:

```csharp
using Clockworks.CLX;
using UnityEngine;

public class SpinWithTheMusic : MonoBehaviour
{
    public CLXReceiver receiver;
    public int deck = 1;

    void Update()
    {
        if (receiver.TryGetDeck(deck, out DeckPacket packet))
        {
            transform.localRotation = Quaternion.Euler(0, (float)packet.NormalizedPosition * 360, 0);
        }
    }
}
```

Or bind its events:

```csharp
void OnEnable()
{
    receiver.OnTrackChanged += HandleTrack;
    receiver.OnEventMessageReceived += HandleEvent;
    receiver.OnWaveformComplete += HandleWaveform;
}

void OnDisable()
{
    receiver.OnTrackChanged -= HandleTrack;
    receiver.OnEventMessageReceived -= HandleEvent;
    receiver.OnWaveformComplete -= HandleWaveform;
}

void HandleTrack(MetaPacket meta) => Debug.Log($"Deck {meta.Deck}: {meta.Artist} - {meta.Title}");

void HandleEvent(EventPacket packet)
{
    if (packet.EventName == CLXEventNames.LoopEnter && packet.Values.Length == 2)
    {
        Debug.Log($"Loop {packet.GetDouble(0)} to {packet.GetDouble(1)}");
    }
}

void HandleWaveform(CLXWaveform waveform)
{
    material.mainTexture = CLXWaveformTexture.Render(waveform, 2048, 128, Color.green);
}
```

To do this without a script, use the receiver's **Events** section in the Inspector.
`CLXReceiver.Instance` finds the receiver from anywhere when a scene has only one.

Events fire on the main thread, during the receiver's `Update`. That runs before
other scripts' `Update`, so reading state there gets the latest packet.

Decks are numbered from 1, as on the wire. A packet's `Position` is `double`
seconds. LITE senders send zero for `Position`, `Position2` and `Length`, so use
`NormalizedPosition`, which they do send.

Unity pauses when it loses focus unless **Run In Background** is on in **Player
Settings**. For a show, turn it on.

## From CLX-UE

The names match, apart from Unreal's prefixes.

| CLX-UE | CLX for Unity |
| --- | --- |
| `UCLXSubsystem` / **Get CLXSubsystem** | `CLXReceiver` / `CLXReceiver.Instance` |
| `ListenIPAddress`, `ListenPort` | The same |
| `SourceSelection` | The same, with a named field per packet kind |
| `DiscoveredSources`, `bSourcesFlag`, `ResetSourcesFlag()` | `DiscoveredSources`, `SourcesFlag`, `ResetSourcesFlag()` |
| `StartListening()`, `StopListening()`, `Announce()` | The same |
| `OnControlMessageReceived`, `OnDeckMessageReceived`, `OnMetaMessageReceived`, `OnEventMessageReceived` | The same |
| `OnWaveformComplete(FilePath)` | `OnWaveformComplete(CLXWaveform)`, with the path in `.FilePath` |
| `FDeckPacket`, `FMetaPacket`, `FControlPacket`, `FEventPacket` | `DeckPacket`, `MetaPacket`, `ControlPacket`, `EventPacket` |
| `FDeckPacket.DeckIndex` | `DeckPacket.Deck` |

Unity also gets these, which CLX-UE doesn't have:

- `OnBeatgridComplete`, `OnCuesComplete`, `OnLeaderMessageReceived` and `OnTrackChanged`.
- `TryGetDeck`, `TryGetMeta`, `GetWaveform`, `GetBeatgrid` and `GetCues`, for reading
  the latest state instead of waiting for an event.
- `RequestWaveform(deck)`.
- Multicast listening, the extra Control and Deck fields, and `FilePath` on Meta.

It behaves differently from CLX-UE in these ways:

- **Waveforms arrive.** CLX-UE reads waveforms from `0x03` packets, a format no
  current sender transmits. This package reads `0x05`, and asks for a track's
  waveform when it loads, since senders otherwise send one only when it's first
  generated.
- **It doesn't skip waveforms when `AppState` is `Production`,** which is what
  release builds of the Server report.
- **`SourceSelection` filters.** In CLX-UE it's declared but unused.
- **Events run on the main thread,** so handlers can touch the scene.
- **It reads the Server's `SeratoActive`** as `Active`, which CLX-UE reads as 0.

## Network

CLX is UDP on port 3650. The receiver listens to both unicast and the multicast
group `239.0.0.1`, unless you turn **Multicast Listen** off. Every 5 seconds it also
sends a join packet to port 7000, which makes senders resend metadata, beat grids
and cues.

If nothing arrives:

- **Check the sender's target.** A sender streams to one unicast address, plus the
  multicast group if that's turned on. In Clockworks Server, turn on **Enable
  Multicast**, or set the target to this machine.
- **Sharing a machine with a Viewer.** Both can bind port 3650, but only one of them
  gets each unicast packet. Multicast reaches both, so use multicast.
- **macOS** can ask for Local Network access for the Unity Editor and for your built
  app. Sends can fail with "No route to host" without it.
- **Windows** asks to allow the Editor through the firewall on first use.
- **The Inspector** shows packet counts and the last network error in Play mode.

Received files go in `Application.persistentDataPath/CLX/cache`, named by track
hash. Turn off **Cache To Disk** to keep them in memory only. A track hash is the
hex MD5 of its title, which is why a Meta packet is enough to find a track's data.

Waveform files use the Viewer's `.rwf` layout. That differs from the BBC format the
spec cites: the header order is `version, channels, sampleRate, samplesPerPixel,
bits, length`. See `CLXWaveform` for the details.

### Platforms

| Platform | Notes |
| --- | --- |
| Windows, macOS, Linux | The main targets. Mono and IL2CPP should both work. |
| iOS | Needs `NSLocalNetworkUsageDescription`. Multicast needs Apple's `com.apple.developer.networking.multicast` entitlement. |
| Android | Multicast may need `CHANGE_WIFI_MULTICAST_STATE` and a `MulticastLock`, which this package doesn't take. Listing network interfaces can fail on some versions, and it falls back to the default interface. |
| WebGL | Not supported: browsers have no UDP. `StartListening` fails and logs why. |

## Using it without Unity

`Runtime/Protocol` is plain C# with no Unity references: `CLXClient` does everything
except the component. Call `Pump()` to raise its events on your own thread.

```csharp
using var client = new CLXClient();
client.DeckReceived += deck => Console.WriteLine($"{deck.Deck}: {deck.Position}");
client.Start();
while (true)
{
    client.Pump();
    Thread.Sleep(10);
}
```

## Development

`Tools~` is ignored by Unity. To run every check that doesn't need Unity:

```bash
Tools~/run-tests.sh
```

It needs the dotnet SDK, and uses Mono as well if it's installed. Pass a Viewer's
cache folder as an argument to also decode real sender data.

To run the tests in Unity, add this to your project's `Packages/manifest.json`, then
use **Window → General → Test Runner**:

```json
"testables": ["live.clockworks.clx"]
```

After adding or renaming a file, run `Tools~/generate-meta.py`. Unity skips any
file without a `.meta` when a package comes from a git URL.

`Tests/Editor/WireFixtures.cs` holds the test datagrams, which
`Tools~/Fixtures/generate.py` regenerates from the real Go and Rust msgpack encoders.

## What hasn't been tested

Nothing in this package has run inside Unity, because Unity wasn't available. This
is what was and wasn't checked.

**Checked outside Unity:**

- The protocol layer compiles for .NET Standard 2.1 at C# 9, the profile of Unity
  2021.3, with warnings as errors.
- All tests pass on .NET and on Mono, the runtime family Unity's Editor uses,
  including real UDP sockets, multicast and two listeners sharing a port.
- Packets from the real Go encoder (`vmihailenco/msgpack`) with the Server's and
  prolink-clx's struct shapes decode correctly.
- Packets with the Bridge's and Viewer's struct shapes decode correctly. **These
  fixtures aren't from the real Rust encoder yet:** `rust.txt` applies `rmp_serde`'s
  encoding rules by hand, because Rust couldn't build when this was written. To
  replace it, run `Tools~/Fixtures/generate.py` and then the tests.
- Every waveform, beat grid and cue file in a real Viewer's cache decodes.
- `package.json`, `.meta` files, assembly references and the scene's links pass
  `Tools~/check-package.py`.

**Only type-checked:** `CLXReceiver`, `CLXWaveformTexture`, the Inspector and the
sample compile against hand-written stubs of Unity's API. If a stub is wrong, the
real compile will fail. Their behaviour hasn't been run at all.

**Not tested:**

- That Unity imports the package, from a git URL or from disk.
- That the hand-written `DeckMonitor.unity` scene opens. It's a deliberately minimal
  file. The script route in [Install](#install) doesn't depend on it.
- IL2CPP builds, and any platform but macOS.
- The tests under Unity's real NUnit. Outside Unity they run on a small stand-in.
- A live session against a running sender. The fixtures are real encoders' bytes,
  not a capture from a real network.
- The notes under [Network](#network) and [Platforms](#platforms) about permissions,
  which come from platform documentation, not from running there.

## License

Apache-2.0. See [LICENSE.md](LICENSE.md).
