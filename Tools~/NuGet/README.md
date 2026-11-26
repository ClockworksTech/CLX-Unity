# Clockworks.CLX.Protocol

Receive a live [CLX](https://github.com/medcelerate/CLX-Spec) stream from Clockworks
senders in any .NET app: deck time, track metadata, mixer state, events, beat grids,
cues, waveforms and the leader clock. It's the protocol layer of
[CLX for Unity](https://github.com/ClockworksTech/CLX-Unity), without Unity.

Targets .NET Standard 2.1 and has no dependencies.

## Quick start

```csharp
using Clockworks.CLX;

using var client = new CLXClient();
client.DeckReceived += deck => Console.WriteLine($"Deck {deck.Deck}: {deck.Position:0.00}s at {deck.BPM} BPM");
client.TrackChanged += meta => Console.WriteLine($"Deck {meta.Deck}: {meta.Artist} - {meta.Title}");
client.Start();

while (true)
{
    client.Pump();          // raises the events above on this thread
    Thread.Sleep(10);
}
```

Packets are received on a background thread. Without `Pump()`, read the latest state
from any thread instead: `TryGetDeck`, `TryGetMeta`, `TryGetControl`, `TryGetLeader`,
`GetWaveform`, `GetBeatgrid` and `GetCues`.

Decks are numbered from 1, as on the wire. LITE senders send zero for `Position`,
`Position2` and `Length`, so use `NormalizedPosition`, which they do send.

## Network

CLX is UDP on port 3650. The client listens to unicast and to the multicast group
`239.0.0.1`, and every 5 seconds sends a join packet to port 7000, which makes senders
resend metadata, beat grids and cues. Set `CLXClientOptions` before `Start()` to change
the address, ports, multicast, waveform requests and the cache folder.

On macOS the app may need Local Network permission; on Windows, a firewall exception.

## License

Apache-2.0.
