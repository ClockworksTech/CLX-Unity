using System;
using System.Collections.Generic;

namespace Clockworks.CLX
{
    /// <summary>
    /// Rebuilds payloads split over <c>0x05</c> fragments, which can arrive out
    /// of order, repeated or not at all. Follows the rules the Viewer settled on.
    ///
    /// Not thread-safe. Time comes in as milliseconds so tests can control it.
    /// </summary>
    public sealed class CLXReassembler
    {
        /// <summary>An unfinished transfer is dropped this long after its first fragment.</summary>
        public const long TimeoutMilliseconds = 30000;

        /// <summary>Larger transfers are refused. Real waveforms are well under this.</summary>
        public const ulong MaxPayloadBytes = 64 * 1024 * 1024;

        const int MaxTransfers = 32;

        sealed class Transfer
        {
            public ulong Total;
            public uint ExpectedPackets;
            public ulong Received;
            public uint MaxOrder;
            public long StartedAt;
            public readonly Dictionary<uint, byte[]> Fragments = new Dictionary<uint, byte[]>();

            public void Restart(ulong total, uint expectedPackets, long now)
            {
                Total = total;
                ExpectedPackets = expectedPackets;
                Received = 0;
                MaxOrder = 0;
                StartedAt = now;
                Fragments.Clear();
            }
        }

        readonly Dictionary<string, Transfer> _transfers = new Dictionary<string, Transfer>();

        /// <summary>Transfers in progress.</summary>
        public int PendingCount => _transfers.Count;

        public void Clear() => _transfers.Clear();

        /// <summary>
        /// Adds a fragment. Returns true with the whole payload once the last
        /// missing fragment arrives.
        /// </summary>
        /// <param name="source">The sender's address, which keeps two senders' transfers apart.</param>
        /// <param name="fragment">The decoded fragment.</param>
        /// <param name="nowMilliseconds">A monotonic clock.</param>
        /// <param name="payload">The reassembled bytes.</param>
        public bool Add(string source, in BinaryFragment fragment, long nowMilliseconds, out byte[] payload)
        {
            payload = null;
            DropExpired(nowMilliseconds);

            if (fragment.Total > MaxPayloadBytes) return false;
            if (fragment.TotalPackets != 0 && fragment.Order >= fragment.TotalPackets) return false;

            // Keyed on the hex form because senders disagree on what the envelope
            // Hash holds, and some of those values aren't valid text.
            string key = source + "/" + fragment.Type + "/" + CLXHash.ToHex(fragment.Hash);
            if (!_transfers.TryGetValue(key, out Transfer transfer))
            {
                if (_transfers.Count >= MaxTransfers) DropOldest();
                transfer = new Transfer();
                transfer.Restart(fragment.Total, fragment.TotalPackets, nowMilliseconds);
                _transfers[key] = transfer;
            }

            // A retry after packet loss, or a Replace waveform for the same hash,
            // can land on what an earlier attempt left behind. Splicing the two
            // would corrupt the payload, so anything that doesn't match restarts.
            bool sameShape = fragment.Total == transfer.Total
                && (fragment.TotalPackets == 0
                    || transfer.ExpectedPackets == 0
                    || fragment.TotalPackets == transfer.ExpectedPackets);
            if (!sameShape || DiffersFromStored(transfer, fragment))
            {
                transfer.Restart(fragment.Total, fragment.TotalPackets, nowMilliseconds);
            }

            // A later fragment may be the first to carry the count.
            if (transfer.ExpectedPackets == 0) transfer.ExpectedPackets = fragment.TotalPackets;

            if (!transfer.Fragments.ContainsKey(fragment.Order))
            {
                transfer.Fragments[fragment.Order] = fragment.Data;
                transfer.Received += (ulong)fragment.Data.Length;
                transfer.MaxOrder = Math.Max(transfer.MaxOrder, fragment.Order);
                if (transfer.Received > MaxPayloadBytes)
                {
                    _transfers.Remove(key);
                    return false;
                }
            }

            int count = transfer.Fragments.Count;
            bool complete = transfer.ExpectedPackets != 0
                ? count == transfer.ExpectedPackets
                : transfer.Received == transfer.Total && count == transfer.MaxOrder + 1L;
            if (!complete) return false;

            payload = Join(transfer);
            _transfers.Remove(key);
            return true;
        }

        static bool DiffersFromStored(Transfer transfer, in BinaryFragment fragment)
        {
            if (!transfer.Fragments.TryGetValue(fragment.Order, out byte[] stored)) return false;
            if (stored.Length != fragment.Data.Length) return true;
            for (int i = 0; i < stored.Length; i++)
            {
                if (stored[i] != fragment.Data[i]) return true;
            }
            return false;
        }

        static byte[] Join(Transfer transfer)
        {
            var orders = new List<uint>(transfer.Fragments.Keys);
            orders.Sort();
            var result = new byte[transfer.Received];
            int offset = 0;
            foreach (uint order in orders)
            {
                byte[] data = transfer.Fragments[order];
                Buffer.BlockCopy(data, 0, result, offset, data.Length);
                offset += data.Length;
            }
            return result;
        }

        void DropExpired(long now)
        {
            List<string> expired = null;
            foreach (KeyValuePair<string, Transfer> pair in _transfers)
            {
                if (now - pair.Value.StartedAt < TimeoutMilliseconds) continue;
                if (expired == null) expired = new List<string>();
                expired.Add(pair.Key);
            }
            if (expired == null) return;
            foreach (string key in expired) _transfers.Remove(key);
        }

        void DropOldest()
        {
            string oldest = null;
            long startedAt = long.MaxValue;
            foreach (KeyValuePair<string, Transfer> pair in _transfers)
            {
                if (pair.Value.StartedAt >= startedAt) continue;
                startedAt = pair.Value.StartedAt;
                oldest = pair.Key;
            }
            if (oldest != null) _transfers.Remove(oldest);
        }
    }
}
