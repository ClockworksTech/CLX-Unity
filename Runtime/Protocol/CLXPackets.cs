using System;

namespace Clockworks.CLX
{
    /// <summary>The first byte of every CLX datagram.</summary>
    public static class CLXPacketType
    {
        public const byte Control = 0x00;
        public const byte Deck = 0x01;
        public const byte Meta = 0x02;

        /// <summary>A two-byte waveform request: <c>[0x03][deck]</c>, sent to a sender.</summary>
        public const byte WaveformRequest = 0x03;

        public const byte Event = 0x04;

        /// <summary>A fragment of a waveform, beat grid or cue list, or a leader packet.</summary>
        public const byte Binary = 0x05;

        /// <summary>The one-byte join packet that asks senders to resend their state.</summary>
        public const byte Join = 0x09;
    }

    /// <summary>Real-time state of one deck (<c>0x01</c>), sent at about 60 Hz.</summary>
    [Serializable]
    public struct DeckPacket
    {
        /// <summary>Tempo multiplier: 1.0 is unmodified, 1.08 is +8%.</summary>
        public double Pitch;

        /// <summary>Playhead position in seconds. Zero from LITE senders.</summary>
        public double Position;

        /// <summary>Beat grid position, as beat number and fraction. Zero from LITE senders.</summary>
        public double Position2;

        /// <summary>Playhead position from 0 to 1.</summary>
        public double NormalizedPosition;

        public double BPM;

        /// <summary>Track length in seconds. Zero from LITE senders.</summary>
        public double Length;

        public float EQLow;
        public float EQMid;
        public float EQHigh;

        /// <summary>Deck number as sent, starting at 1.</summary>
        public int Deck;

        /// <summary>Current beat, 1 to 4. Ignore other values.</summary>
        public int Beat;

        /// <summary>
        /// Play state as sent by the source. Its values depend on the sender, and
        /// senders that don't report it leave it at 0.
        /// </summary>
        public int State;

        /// <summary>Sequence number, wrapping at 255. Only valid when <see cref="HasOrd"/> is true.</summary>
        public int Ord;

        /// <summary>False when the sender doesn't send <see cref="Ord"/>.</summary>
        public bool HasOrd;
    }

    /// <summary>Track metadata (<c>0x02</c>), sent on load and repeated every few seconds.</summary>
    [Serializable]
    public struct MetaPacket
    {
        public int Deck;
        public string Title;
        public string Artist;
        public string Album;
        public string FilePath;

        /// <summary>The track hash that keys this track's waveform, beat grid and cues.</summary>
        public string TrackHash => CLXHash.ForTitle(Title);
    }

    /// <summary>Mixer and app state (<c>0x00</c>).</summary>
    [Serializable]
    public struct ControlPacket
    {
        public double UpfaderA;
        public double UpfaderB;
        public double UpfaderC;
        public double UpfaderD;
        public double UpfaderE;
        public double UpfaderF;
        public double Crossfader;

        /// <summary>Active deck or focus state.</summary>
        public int Active;

        public string AppState;

        /// <summary>The upfader of deck 1 to 6, or 0 for any other deck.</summary>
        public double GetUpfader(int deck)
        {
            switch (deck)
            {
                case 1: return UpfaderA;
                case 2: return UpfaderB;
                case 3: return UpfaderC;
                case 4: return UpfaderD;
                case 5: return UpfaderE;
                case 6: return UpfaderF;
                default: return 0;
            }
        }
    }

    /// <summary>
    /// A command or deck state change (<c>0x04</c>). Match on the
    /// <see cref="EventName"/> values you know and ignore the rest.
    /// </summary>
    public struct EventPacket
    {
        public string EventName;

        /// <summary>Deck number, or 0 when the event isn't deck-specific.</summary>
        public int Deck;

        /// <summary>
        /// Arguments. Their meaning depends on the event, so check the length
        /// before indexing. Never null. Numbers come through as
        /// <see cref="long"/> or <see cref="double"/>.
        /// </summary>
        public object[] Values;

        /// <summary>
        /// The first value as a byte, for the older single-value form of an
        /// event and for commands. 0 when there is none.
        /// </summary>
        public byte Value;

        /// <summary>A value as a double, or the fallback if it's absent or not a number.</summary>
        public double GetDouble(int index, double fallback = 0)
        {
            if (Values == null || index < 0 || index >= Values.Length) return fallback;
            switch (Values[index])
            {
                case double d: return d;
                case long l: return l;
                case ulong u: return u;
                default: return fallback;
            }
        }
    }

    /// <summary>The event names defined by the CLX spec.</summary>
    public static class CLXEventNames
    {
        /// <summary>
        /// A deck entered or resized a loop. Values are <c>[in, out]</c> in
        /// seconds, or <c>[beats]</c> when the source can't report positions
        /// (Traktor), so check the length first.
        /// </summary>
        public const string LoopEnter = "loop_enter";

        /// <summary>A deck left a loop. Values are empty.</summary>
        public const string LoopExit = "loop_exit";
    }

    /// <summary>
    /// The clock leader's live deck and position (<c>0x05</c>, Type "leader"), an
    /// optional part of CLX. Treat each packet as the current truth.
    /// </summary>
    [Serializable]
    public struct LeaderPacket
    {
        /// <summary>The live deck, 1 to 6, or 0 when nothing is live.</summary>
        public int LiveDeck;

        /// <summary>The leader's clock position in seconds.</summary>
        public double Position;

        /// <summary>The project loaded on the leader. Empty when unnamed.</summary>
        public string Project;

        /// <summary>Egress time in nanoseconds since the Unix epoch. Only valid when <see cref="HasTx"/> is true.</summary>
        public ulong Tx;

        /// <summary>False when the packet's age is unknown, which isn't the same as a Tx of 0.</summary>
        public bool HasTx;
    }

    /// <summary>One <c>0x05</c> datagram's envelope. Large payloads span many fragments.</summary>
    public struct BinaryFragment
    {
        /// <summary><c>waveform</c>, <c>beatgrid</c>, <c>cues</c>, <c>leader</c>, or a type to ignore.</summary>
        public string Type;

        /// <summary>
        /// Identifies this transfer only. Senders disagree on what it holds, so
        /// use the Hash inside the reassembled payload to identify the track.
        /// </summary>
        public byte[] Hash;

        /// <summary>Size of the reassembled payload in bytes.</summary>
        public ulong Total;

        public uint Order;

        /// <summary>The number of fragments, or 0 from older senders.</summary>
        public uint TotalPackets;

        public byte[] Data;
    }

    /// <summary>One marker in a track's sparse tempo map.</summary>
    [Serializable]
    public struct BeatgridMarker
    {
        public float Bpm;

        /// <summary>Seconds from the start of the track.</summary>
        public float Position;

        /// <summary>True for the markers that bracket the grid.</summary>
        public bool Terminal;

        public uint BeatsToNext;
    }

    /// <summary>One beat of a grid expanded by <see cref="BeatgridPayload.ExpandBeats"/>.</summary>
    [Serializable]
    public struct CLXBeat
    {
        /// <summary>Seconds from the start of the track.</summary>
        public float Time;

        /// <summary>True when this beat is a terminal marker.</summary>
        public bool Terminal;

        /// <summary>Beats since the first marker. Every 4th is a downbeat.</summary>
        public int Index;
    }

    /// <summary>A track's beat grid (<c>0x05</c>, Type "beatgrid").</summary>
    public sealed class BeatgridPayload
    {
        const int MaxBeats = 20000;

        /// <summary>The 32-character track hash.</summary>
        public string Hash = string.Empty;

        /// <summary>
        /// Total number of beats in the track. Some senders leave this at 0, so
        /// use <see cref="ExpandBeats"/> to count them.
        /// </summary>
        public uint Total;

        /// <summary>
        /// Tempo anchors, not beats: a constant-tempo track often has just one.
        /// Use <see cref="ExpandBeats"/> to get one entry per beat.
        /// </summary>
        public BeatgridMarker[] Markers = Array.Empty<BeatgridMarker>();

        /// <summary>
        /// Expands the sparse markers into one entry per beat, from the first
        /// marker to the end of the track. Between two markers the beats are
        /// evenly spaced; after the last they continue at its BPM.
        /// </summary>
        public CLXBeat[] ExpandBeats(float trackSeconds)
        {
            var beats = new System.Collections.Generic.List<CLXBeat>();
            int beatIndex = 0;
            for (int i = 0; i < Markers.Length && beats.Count < MaxBeats; i++)
            {
                BeatgridMarker m = Markers[i];
                beats.Add(new CLXBeat { Time = m.Position, Terminal = m.Terminal, Index = beatIndex });

                float spacing = 0;
                long count = 0;
                if (i + 1 < Markers.Length)
                {
                    long n = Math.Max(1u, m.BeatsToNext);
                    float next = Markers[i + 1].Position;
                    if (next > m.Position)
                    {
                        spacing = (next - m.Position) / n;
                        count = n;
                    }
                }
                else if (m.Bpm > 0)
                {
                    spacing = 60f / m.Bpm;
                    count = Math.Max(0L, (long)Math.Floor((trackSeconds - m.Position) / spacing));
                    // The marker takes beat 0, and in this branch nothing follows
                    // to take beat `count`, so that beat is emitted here.
                    count += 1;
                }

                if (spacing > 0)
                {
                    for (long b = 1; b < count && beats.Count < MaxBeats; b++)
                    {
                        float time = m.Position + b * spacing;
                        if (time > trackSeconds) break;
                        beats.Add(new CLXBeat { Time = time, Index = (int)(beatIndex + b) });
                    }
                }
                beatIndex += (int)Math.Min(count, int.MaxValue / 2);
            }
            return beats.ToArray();
        }
    }

    /// <summary>One hot cue or memory cue.</summary>
    [Serializable]
    public struct CLXCue
    {
        /// <summary>Seconds from the start of the track.</summary>
        public float Time;

        /// <summary>The cue's label. Empty when unnamed.</summary>
        public string Name;

        /// <summary>True for a hot cue (a pad), false for a memory cue.</summary>
        public bool Hotcue;
    }

    /// <summary>
    /// The DJ software's cues for a track (<c>0x05</c>, Type "cues"). A new list
    /// replaces the old one for that hash. Empty means the track has no cues.
    /// </summary>
    public sealed class CuesPayload
    {
        /// <summary>The 32-character track hash.</summary>
        public string Hash = string.Empty;

        public CLXCue[] Cues = Array.Empty<CLXCue>();
    }

    /// <summary>A reassembled waveform (<c>0x05</c>, Type "waveform"), before its file is parsed.</summary>
    public sealed class WaveformPayload
    {
        /// <summary>The 32-character track hash.</summary>
        public string Hash = string.Empty;

        /// <summary>The waveform file's bytes.</summary>
        public byte[] Data = Array.Empty<byte>();

        /// <summary>True if this should overwrite a cached waveform for the same hash.</summary>
        public bool Replace;
    }
}
