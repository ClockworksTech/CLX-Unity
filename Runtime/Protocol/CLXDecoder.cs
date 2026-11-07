using System;

namespace Clockworks.CLX
{
    /// <summary>
    /// Decodes CLX MessagePack bodies. Each method takes the bytes after the
    /// packet type byte and returns false on malformed input without throwing.
    ///
    /// Keys are matched the way the senders really write them, which isn't always
    /// what the spec table says. Unknown keys are skipped and missing keys read as
    /// their default, so new fields don't break older receivers.
    /// </summary>
    public static class CLXDecoder
    {
        public static bool TryDecodeDeck(byte[] data, int offset, int count, out DeckPacket packet)
        {
            packet = default;
            var r = new MsgPackReader(data, offset, count);
            if (!r.TryReadMapHeader(out int pairs)) return false;
            for (int i = 0; i < pairs; i++)
            {
                MsgPackKey key = r.ReadKey();
                if (key.Is("Pitch")) packet.Pitch = r.ReadDouble();
                else if (key.Is("Position")) packet.Position = r.ReadDouble();
                else if (key.Is("Position2")) packet.Position2 = r.ReadDouble();
                else if (key.Is("NormalizedPosition")) packet.NormalizedPosition = r.ReadDouble();
                else if (key.Is("BPM")) packet.BPM = r.ReadDouble();
                else if (key.Is("Length")) packet.Length = r.ReadDouble();
                else if (key.Is("EQLow")) packet.EQLow = r.ReadSingle();
                else if (key.Is("EQMid")) packet.EQMid = r.ReadSingle();
                else if (key.Is("EQHigh")) packet.EQHigh = r.ReadSingle();
                else if (key.Is("Deck")) packet.Deck = r.ReadInt32();
                else if (key.Is("Beat")) packet.Beat = r.ReadInt32();
                else if (key.Is("State")) packet.State = r.ReadInt32();
                else if (key.Is("Ord"))
                {
                    packet.Ord = r.ReadInt32();
                    packet.HasOrd = true;
                }
                else r.Skip();
                if (r.Failed) return false;
            }
            return true;
        }

        public static bool TryDecodeMeta(byte[] data, int offset, int count, out MetaPacket packet)
        {
            packet = new MetaPacket
            {
                Title = string.Empty,
                Artist = string.Empty,
                Album = string.Empty,
                FilePath = string.Empty,
            };
            var r = new MsgPackReader(data, offset, count);
            if (!r.TryReadMapHeader(out int pairs)) return false;
            for (int i = 0; i < pairs; i++)
            {
                MsgPackKey key = r.ReadKey();
                if (key.Is("Deck")) packet.Deck = r.ReadInt32();
                else if (key.Is("Title")) packet.Title = r.ReadString();
                else if (key.Is("Artist")) packet.Artist = r.ReadString();
                else if (key.Is("Album")) packet.Album = r.ReadString();
                else if (key.Is("FilePath")) packet.FilePath = r.ReadString();
                else r.Skip();
                if (r.Failed) return false;
            }
            return true;
        }

        public static bool TryDecodeControl(byte[] data, int offset, int count, out ControlPacket packet)
        {
            packet = new ControlPacket { AppState = string.Empty };
            var r = new MsgPackReader(data, offset, count);
            if (!r.TryReadMapHeader(out int pairs)) return false;
            for (int i = 0; i < pairs; i++)
            {
                MsgPackKey key = r.ReadKey();
                if (key.Is("UpfaderA")) packet.UpfaderA = r.ReadDouble();
                else if (key.Is("UpfaderB")) packet.UpfaderB = r.ReadDouble();
                else if (key.Is("UpfaderC")) packet.UpfaderC = r.ReadDouble();
                else if (key.Is("UpfaderD")) packet.UpfaderD = r.ReadDouble();
                else if (key.Is("UpfaderE")) packet.UpfaderE = r.ReadDouble();
                else if (key.Is("UpfaderF")) packet.UpfaderF = r.ReadDouble();
                else if (key.Is("Crossfader")) packet.Crossfader = r.ReadDouble();
                // The spec calls this Active, and the Bridge and Player send that.
                // The Server's Go struct sends the field as SeratoActive.
                else if (key.Is("Active") || key.Is("SeratoActive")) packet.Active = r.ReadInt32();
                else if (key.Is("AppState")) packet.AppState = r.ReadString();
                else r.Skip();
                if (r.Failed) return false;
            }
            return true;
        }

        public static bool TryDecodeEvent(byte[] data, int offset, int count, out EventPacket packet)
        {
            packet = new EventPacket { EventName = string.Empty, Values = Array.Empty<object>() };
            var r = new MsgPackReader(data, offset, count);
            if (!r.TryReadMapHeader(out int pairs)) return false;
            bool hasValue = false;
            for (int i = 0; i < pairs; i++)
            {
                MsgPackKey key = r.ReadKey();
                if (key.Is("Event")) packet.EventName = r.ReadString();
                else if (key.Is("Deck")) packet.Deck = r.ReadInt32();
                else if (key.Is("Values"))
                {
                    // Go writes an empty list as nil.
                    if (r.ReadValue() is object[] values) packet.Values = values;
                }
                else if (key.Is("Value"))
                {
                    // The form CLX-UE and the CDJ firmware use.
                    packet.Value = unchecked((byte)r.ReadInt64());
                    hasValue = true;
                }
                else r.Skip();
                if (r.Failed) return false;
            }
            if (!hasValue && packet.Values.Length > 0)
            {
                packet.Value = unchecked((byte)(long)packet.GetDouble(0));
            }
            return true;
        }

        public static bool TryDecodeBinary(byte[] data, int offset, int count, out BinaryFragment fragment)
        {
            fragment = new BinaryFragment
            {
                Type = string.Empty,
                Hash = Array.Empty<byte>(),
                Data = Array.Empty<byte>(),
            };
            var r = new MsgPackReader(data, offset, count);
            if (!r.TryReadMapHeader(out int pairs)) return false;
            for (int i = 0; i < pairs; i++)
            {
                MsgPackKey key = r.ReadKey();
                if (key.Is("Type")) fragment.Type = r.ReadString();
                else if (key.Is("Hash")) fragment.Hash = r.ReadBytes();
                else if (key.Is("Total")) fragment.Total = r.ReadUInt64();
                else if (key.Is("Order")) fragment.Order = (uint)r.ReadUInt64();
                else if (key.Is("TotalPackets")) fragment.TotalPackets = (uint)r.ReadUInt64();
                else if (key.Is("Data")) fragment.Data = r.ReadBytes();
                else r.Skip();
                if (r.Failed) return false;
            }
            return true;
        }

        public static bool TryDecodeLeader(byte[] data, int offset, int count, out LeaderPacket packet)
        {
            packet = new LeaderPacket { Project = string.Empty };
            var r = new MsgPackReader(data, offset, count);
            if (!r.TryReadMapHeader(out int pairs)) return false;
            for (int i = 0; i < pairs; i++)
            {
                MsgPackKey key = r.ReadKey();
                if (key.Is("LiveDeck")) packet.LiveDeck = r.ReadInt32();
                else if (key.Is("Position")) packet.Position = r.ReadDouble();
                else if (key.Is("Project")) packet.Project = r.ReadString();
                else if (key.Is("Tx"))
                {
                    packet.Tx = r.ReadUInt64();
                    packet.HasTx = true;
                }
                else r.Skip();
                if (r.Failed) return false;
            }
            return true;
        }

        public static bool TryDecodeWaveform(byte[] data, out WaveformPayload payload)
        {
            payload = null;
            var result = new WaveformPayload();
            var r = new MsgPackReader(data, 0, data.Length);
            if (!r.TryReadMapHeader(out int pairs)) return false;
            byte[] hash = Array.Empty<byte>();
            for (int i = 0; i < pairs; i++)
            {
                MsgPackKey key = r.ReadKey();
                if (key.Is("Hash")) hash = r.ReadBytes();
                else if (key.Is("Data")) result.Data = r.ReadBytes();
                else if (key.Is("Replace")) result.Replace = r.ReadBool();
                else r.Skip();
                if (r.Failed) return false;
            }
            result.Hash = CLXHash.Normalize(hash);
            if (result.Hash == null) return false;
            payload = result;
            return true;
        }

        public static bool TryDecodeBeatgrid(byte[] data, out BeatgridPayload payload)
        {
            payload = null;
            var result = new BeatgridPayload();
            var r = new MsgPackReader(data, 0, data.Length);
            if (!r.TryReadMapHeader(out int pairs)) return false;
            byte[] hash = Array.Empty<byte>();
            for (int i = 0; i < pairs; i++)
            {
                MsgPackKey key = r.ReadKey();
                if (key.Is("Hash")) hash = r.ReadBytes();
                else if (key.Is("Total")) result.Total = (uint)r.ReadUInt64();
                else if (key.Is("Markers"))
                {
                    if (r.TryReadArrayHeader(out int markers))
                    {
                        result.Markers = new BeatgridMarker[markers];
                        for (int m = 0; m < markers && !r.Failed; m++)
                        {
                            result.Markers[m] = ReadMarker(ref r);
                        }
                    }
                    else r.Skip();
                }
                else r.Skip();
                if (r.Failed) return false;
            }
            result.Hash = CLXHash.Normalize(hash);
            if (result.Hash == null) return false;
            payload = result;
            return true;
        }

        static BeatgridMarker ReadMarker(ref MsgPackReader r)
        {
            BeatgridMarker marker = default;
            if (!r.TryReadMapHeader(out int pairs))
            {
                r.Skip();
                return marker;
            }
            for (int i = 0; i < pairs && !r.Failed; i++)
            {
                MsgPackKey key = r.ReadKey();
                if (key.Is("Bpm")) marker.Bpm = r.ReadSingle();
                else if (key.Is("Position")) marker.Position = r.ReadSingle();
                else if (key.Is("Terminal")) marker.Terminal = r.ReadBool();
                else if (key.Is("BeatsToNext")) marker.BeatsToNext = (uint)r.ReadUInt64();
                else r.Skip();
            }
            return marker;
        }

        public static bool TryDecodeCues(byte[] data, out CuesPayload payload)
        {
            payload = null;
            var result = new CuesPayload();
            var r = new MsgPackReader(data, 0, data.Length);
            if (!r.TryReadMapHeader(out int pairs)) return false;
            byte[] hash = Array.Empty<byte>();
            for (int i = 0; i < pairs; i++)
            {
                MsgPackKey key = r.ReadKey();
                if (key.Is("Hash")) hash = r.ReadBytes();
                else if (key.Is("Cues"))
                {
                    if (r.TryReadArrayHeader(out int cues))
                    {
                        result.Cues = new CLXCue[cues];
                        for (int c = 0; c < cues && !r.Failed; c++)
                        {
                            result.Cues[c] = ReadCue(ref r);
                        }
                    }
                    else r.Skip(); // Go writes an empty list as nil.
                }
                else r.Skip();
                if (r.Failed) return false;
            }
            result.Hash = CLXHash.Normalize(hash);
            if (result.Hash == null) return false;
            payload = result;
            return true;
        }

        static CLXCue ReadCue(ref MsgPackReader r)
        {
            var cue = new CLXCue { Name = string.Empty };
            if (!r.TryReadMapHeader(out int pairs))
            {
                r.Skip();
                return cue;
            }
            for (int i = 0; i < pairs && !r.Failed; i++)
            {
                MsgPackKey key = r.ReadKey();
                if (key.Is("Time")) cue.Time = r.ReadSingle();
                else if (key.Is("Name")) cue.Name = r.ReadString();
                else if (key.Is("Hotcue")) cue.Hotcue = r.ReadBool();
                else r.Skip();
            }
            return cue;
        }
    }
}
