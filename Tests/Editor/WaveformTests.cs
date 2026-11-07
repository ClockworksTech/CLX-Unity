using System;
using NUnit.Framework;

namespace Clockworks.CLX.Tests
{
    [TestFixture]
    public class WaveformTests
    {
        const double Tolerance = 1e-5;

        static byte[] Header(int version, int channels, int rate, int spp, int bits, int length)
        {
            var bytes = new byte[24];
            int[] values = { version, channels, rate, spp, bits, length };
            for (int i = 0; i < 6; i++) BitConverter.GetBytes(values[i]).CopyTo(bytes, i * 4);
            return bytes;
        }

        static byte[] Reassembled(string[] fixture)
        {
            var reassembler = new CLXReassembler();
            byte[] result = null;
            foreach (string hex in fixture)
            {
                byte[] bytes = TestBytes.FromHex(hex);
                Assert.IsTrue(CLXDecoder.TryDecodeBinary(bytes, 1, bytes.Length - 1, out BinaryFragment fragment));
                if (reassembler.Add("a", fragment, 0, out byte[] payload)) result = payload;
            }
            Assert.IsNotNull(result);
            return result;
        }

        [Test]
        public void ParsesARealWaveformTransfer()
        {
            Assert.IsTrue(CLXDecoder.TryDecodeWaveform(Reassembled(WireFixtures.GoWaveform), out WaveformPayload payload));
            Assert.IsFalse(payload.Replace);
            Assert.IsTrue(CLXWaveform.TryParse(payload.Data, out CLXWaveform waveform));

            Assert.AreEqual(2, waveform.Version);
            Assert.AreEqual(1, waveform.Channels);
            Assert.AreEqual(44100, waveform.SampleRate);
            Assert.AreEqual(124, waveform.SamplesPerPixel);
            Assert.AreEqual(8, waveform.Bits);
            Assert.AreEqual(2000, waveform.Length);
            Assert.AreEqual(2000.0 * 124 / 44100, waveform.Duration, Tolerance);

            waveform.GetPeak(5, 0, out float min, out float max);
            Assert.AreEqual(-5 / 128.0, min, Tolerance);
            Assert.AreEqual(5 / 128.0, max, Tolerance);

            Assert.IsTrue(waveform.HasColors);
            Assert.AreEqual(44, (int)waveform.Colors[300 * 3]);
            Assert.AreEqual(88, (int)waveform.Colors[300 * 3 + 1]);
            Assert.AreEqual(132, (int)waveform.Colors[300 * 3 + 2]);
        }

        [Test]
        public void BridgeWaveformCarriesReplace()
        {
            Assert.IsTrue(CLXDecoder.TryDecodeWaveform(Reassembled(WireFixtures.RustWaveform), out WaveformPayload payload));
            Assert.IsTrue(payload.Replace);
        }

        [Test]
        public void ColorTableIsOptional()
        {
            byte[] data = new byte[24 + 4];
            Header(2, 1, 44100, 124, 8, 2).CopyTo(data, 0);
            Assert.IsTrue(CLXWaveform.TryParse(data, out CLXWaveform waveform));
            Assert.IsFalse(waveform.HasColors);
        }

        [Test]
        public void StereoPeaksAreAddressedPerChannel()
        {
            byte[] data = new byte[24 + 4];
            Header(2, 2, 44100, 124, 8, 1).CopyTo(data, 0);
            data[24] = unchecked((byte)-10);
            data[25] = 10;
            data[26] = unchecked((byte)-20);
            data[27] = 20;
            Assert.IsTrue(CLXWaveform.TryParse(data, out CLXWaveform waveform));
            waveform.GetPeak(0, 1, out float min, out float max);
            Assert.AreEqual(-20 / 128.0, min, Tolerance);
            Assert.AreEqual(20 / 128.0, max, Tolerance);
        }

        [Test]
        public void MalformedFilesAreRejected()
        {
            Assert.IsFalse(CLXWaveform.TryParse(null, out _));
            Assert.IsFalse(CLXWaveform.TryParse(new byte[10], out _));
            Assert.IsFalse(CLXWaveform.TryParse(Header(2, 1, 44100, 124, 8, 0), out _));
            Assert.IsFalse(CLXWaveform.TryParse(Header(2, 0, 44100, 124, 8, 2), out _));
            Assert.IsFalse(CLXWaveform.TryParse(Header(2, 1, 44100, 124, 8, 2), out _)); // No peaks.
            Assert.IsFalse(CLXWaveform.TryParse(Header(2, 1, 44100, 124, 16, 2), out _));
            Assert.IsFalse(CLXWaveform.TryParse(Header(2, 64, 44100, 124, 8, int.MaxValue), out _));
        }

        [Test]
        public void TruncatedColorTableIsIgnored()
        {
            byte[] data = new byte[24 + 2 + 8 + 1];
            Header(2, 1, 44100, 124, 8, 1).CopyTo(data, 0);
            data[26] = (byte)'C';
            data[27] = (byte)'L';
            data[28] = (byte)'R';
            data[29] = (byte)'S';
            BitConverter.GetBytes(1).CopyTo(data, 30);
            Assert.IsTrue(CLXWaveform.TryParse(data, out CLXWaveform waveform));
            Assert.IsFalse(waveform.HasColors);
        }

        [Test]
        public void BeatgridExpandsSparseMarkers()
        {
            foreach (string[] fixture in new[] { WireFixtures.GoBeatgrid, WireFixtures.RustBeatgrid })
            {
                Assert.IsTrue(CLXDecoder.TryDecodeBeatgrid(Reassembled(fixture), out BeatgridPayload grid));
                Assert.AreEqual(CLXHash.ForTitle(WireFixtures.Title), grid.Hash);
                Assert.AreEqual(512u, grid.Total);
                Assert.AreEqual(3, grid.Markers.Length);
                Assert.AreEqual(130f, grid.Markers[2].Bpm, Tolerance);
                Assert.IsTrue(grid.Markers[2].Terminal);
                Assert.AreEqual(64u, grid.Markers[0].BeatsToNext);

                CLXBeat[] beats = grid.ExpandBeats(60f);
                Assert.AreEqual(129, beats.Length);
                Assert.AreEqual(0.25f, beats[0].Time, Tolerance);
                Assert.AreEqual(0.25f + 30f / 64, beats[1].Time, Tolerance);
                Assert.AreEqual(30.25f, beats[64].Time, Tolerance);
                Assert.AreEqual(64, beats[64].Index);
                Assert.AreEqual(45f, beats[96].Time, Tolerance);
                Assert.IsTrue(beats[96].Terminal);
                Assert.AreEqual(45f + 32 * 60f / 130, beats[128].Time, 1e-3);
                Assert.AreEqual(128, beats[128].Index);
            }
        }

        [Test]
        public void ConstantTempoTrackHasOneMarker()
        {
            var grid = new BeatgridPayload
            {
                Markers = new[] { new BeatgridMarker { Bpm = 120, Position = 1, Terminal = true } },
            };
            CLXBeat[] beats = grid.ExpandBeats(3f);
            Assert.AreEqual(5, beats.Length);
            Assert.AreEqual(3f, beats[4].Time, Tolerance);
        }

        [Test]
        public void PathologicalGridIsBounded()
        {
            var grid = new BeatgridPayload
            {
                Markers = new[]
                {
                    new BeatgridMarker { Bpm = 0, Position = 5 },
                    new BeatgridMarker { Bpm = 1000000, Position = 5, Terminal = true },
                },
            };
            Assert.IsTrue(grid.ExpandBeats(100000f).Length <= 20000);
        }

        [Test]
        public void CuesDecode()
        {
            foreach (string[] fixture in new[] { WireFixtures.GoCues, WireFixtures.RustCues })
            {
                Assert.IsTrue(CLXDecoder.TryDecodeCues(Reassembled(fixture), out CuesPayload payload));
                Assert.AreEqual(2, payload.Cues.Length);
                Assert.AreEqual(1.5f, payload.Cues[0].Time, Tolerance);
                Assert.AreEqual("Intro", payload.Cues[0].Name);
                Assert.IsTrue(payload.Cues[0].Hotcue);
                Assert.AreEqual(string.Empty, payload.Cues[1].Name);
                Assert.IsFalse(payload.Cues[1].Hotcue);
            }
        }

        [Test]
        public void GoNilCueListIsEmpty()
        {
            Assert.IsTrue(CLXDecoder.TryDecodeCues(Reassembled(WireFixtures.GoCuesNil), out CuesPayload payload));
            Assert.AreEqual(0, payload.Cues.Length);
        }
    }
}
