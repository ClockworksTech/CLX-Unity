using System;
using System.Collections.Generic;

namespace Clockworks.CLX
{
    /// <summary>What to do with a Deck packet.</summary>
    public enum CLXPacketVerdict
    {
        Accept,

        /// <summary>Older than one already used.</summary>
        Late,

        /// <summary>The same packet again.</summary>
        Duplicate,
    }

    /// <summary>
    /// Drops repeated and out-of-order packets. Senders mirror every packet to
    /// the multicast group as well as to their unicast target, so a machine that
    /// is the target and also listens to multicast hears everything twice. Left
    /// alone, a command event would fire twice and time could step backwards.
    ///
    /// Not thread-safe. Time comes in as milliseconds so tests can control it.
    /// </summary>
    public sealed class CLXPacketFilter
    {
        // Ordering is only trusted between packets this close together, so a
        // sender that restarts or pauses isn't taken for a late one. Same as the
        // Viewer's.
        const long OrdWindowMilliseconds = 100;

        // Without Ord the only clue is identical bytes, and a paused deck really
        // does repeat itself every 16 ms, so this stays under one packet interval.
        const long IdenticalDeckWindowMilliseconds = 5;

        const long IdenticalEventWindowMilliseconds = 20;

        const int MaxEntries = 256;
        const int EventKey = -1;

        sealed class Entry
        {
            public long At;
            public int Ord;
            public bool HasOrd;
            public int Length;
            public byte[] Bytes = new byte[256];

            public bool SameBytes(byte[] data, int offset, int count)
            {
                if (count != Length) return false;
                for (int i = 0; i < count; i++)
                {
                    if (Bytes[i] != data[offset + i]) return false;
                }
                return true;
            }

            public void Store(byte[] data, int offset, int count, long now)
            {
                if (Bytes.Length < count) Bytes = new byte[count];
                Buffer.BlockCopy(data, offset, Bytes, 0, count);
                Length = count;
                At = now;
            }
        }

        readonly Dictionary<(string source, int deck), Entry> _entries =
            new Dictionary<(string source, int deck), Entry>();

        public void Clear() => _entries.Clear();

        /// <summary>Judges a Deck packet. <paramref name="data"/> is its body.</summary>
        public CLXPacketVerdict CheckDeck(
            string source, in DeckPacket packet, byte[] data, int offset, int count, long nowMilliseconds)
        {
            bool known = _entries.TryGetValue((source, packet.Deck), out Entry entry);
            CLXPacketVerdict verdict = CLXPacketVerdict.Accept;
            if (known)
            {
                long age = nowMilliseconds - entry.At;
                if (packet.HasOrd && entry.HasOrd)
                {
                    if (age < OrdWindowMilliseconds)
                    {
                        int step = (packet.Ord - entry.Ord) & 0xff;
                        if (step == 0 && entry.SameBytes(data, offset, count)) verdict = CLXPacketVerdict.Duplicate;
                        else if (step > 127) verdict = CLXPacketVerdict.Late;
                    }
                }
                else if (age < IdenticalDeckWindowMilliseconds && entry.SameBytes(data, offset, count))
                {
                    verdict = CLXPacketVerdict.Duplicate;
                }
            }
            else
            {
                entry = Add(source, packet.Deck);
            }

            if (verdict == CLXPacketVerdict.Accept)
            {
                entry.Ord = packet.Ord;
                entry.HasOrd = packet.HasOrd;
                entry.Store(data, offset, count, nowMilliseconds);
            }
            return verdict;
        }

        /// <summary>True if this Event packet repeats the last one from its source.</summary>
        public bool IsDuplicateEvent(string source, byte[] data, int offset, int count, long nowMilliseconds)
        {
            if (!_entries.TryGetValue((source, EventKey), out Entry entry))
            {
                entry = Add(source, EventKey);
            }
            else if (nowMilliseconds - entry.At < IdenticalEventWindowMilliseconds
                && entry.SameBytes(data, offset, count))
            {
                return true;
            }
            entry.Store(data, offset, count, nowMilliseconds);
            return false;
        }

        Entry Add(string source, int deck)
        {
            if (_entries.Count >= MaxEntries) _entries.Clear();
            var entry = new Entry();
            _entries[(source, deck)] = entry;
            return entry;
        }
    }
}
