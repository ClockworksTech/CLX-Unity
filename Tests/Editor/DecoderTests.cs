using NUnit.Framework;

namespace Clockworks.CLX.Tests
{
    /// <summary>Decodes what the real encoders produced for each sender.</summary>
    [TestFixture]
    public class DecoderTests
    {
        const double Tolerance = 1e-6;

        static readonly string TrackHash = CLXHash.ForTitle(WireFixtures.Title);

        static DeckPacket Deck(string hex)
        {
            byte[] bytes = TestBytes.FromHex(hex);
            Assert.AreEqual(CLXPacketType.Deck, bytes[0]);
            Assert.IsTrue(CLXDecoder.TryDecodeDeck(bytes, 1, bytes.Length - 1, out DeckPacket packet));
            return packet;
        }

        static ControlPacket Control(string hex)
        {
            byte[] bytes = TestBytes.FromHex(hex);
            Assert.AreEqual(CLXPacketType.Control, bytes[0]);
            Assert.IsTrue(CLXDecoder.TryDecodeControl(bytes, 1, bytes.Length - 1, out ControlPacket packet));
            return packet;
        }

        static MetaPacket Meta(string hex)
        {
            byte[] bytes = TestBytes.FromHex(hex);
            Assert.AreEqual(CLXPacketType.Meta, bytes[0]);
            Assert.IsTrue(CLXDecoder.TryDecodeMeta(bytes, 1, bytes.Length - 1, out MetaPacket packet));
            return packet;
        }

        static EventPacket Event(string hex)
        {
            byte[] bytes = TestBytes.FromHex(hex);
            Assert.AreEqual(CLXPacketType.Event, bytes[0]);
            Assert.IsTrue(CLXDecoder.TryDecodeEvent(bytes, 1, bytes.Length - 1, out EventPacket packet));
            return packet;
        }

        static void AssertDeckValues(in DeckPacket deck)
        {
            Assert.AreEqual(1.02, deck.Pitch, Tolerance);
            Assert.AreEqual(12.5, deck.Position, Tolerance);
            Assert.AreEqual(33.25, deck.Position2, Tolerance);
            Assert.AreEqual(0.1, deck.NormalizedPosition, Tolerance);
            Assert.AreEqual(128.0, deck.BPM, Tolerance);
            Assert.AreEqual(240.0, deck.Length, Tolerance);
            Assert.AreEqual(0.5f, deck.EQLow, Tolerance);
            Assert.AreEqual(0.625f, deck.EQMid, Tolerance);
            Assert.AreEqual(0.75f, deck.EQHigh, Tolerance);
            Assert.AreEqual(2, deck.Deck);
            Assert.AreEqual(3, deck.Beat);
        }

        [Test]
        public void ServerDeck()
        {
            DeckPacket deck = Deck(WireFixtures.GoDeck);
            AssertDeckValues(deck);
            Assert.IsFalse(deck.HasOrd); // The Server sends an always-zero Sequence, not Ord.
        }

        [Test]
        public void BridgeDeck()
        {
            DeckPacket deck = Deck(WireFixtures.RustDeck);
            AssertDeckValues(deck);
            Assert.IsTrue(deck.HasOrd);
            Assert.AreEqual(200, deck.Ord);
            Assert.AreEqual(3, deck.State);
        }

        [Test]
        public void ServerControlSendsActiveAsSeratoActive()
        {
            ControlPacket control = Control(WireFixtures.GoControl);
            Assert.AreEqual(1.0, control.UpfaderA, Tolerance);
            Assert.AreEqual(0.5, control.UpfaderB, Tolerance);
            Assert.AreEqual(0.25, control.UpfaderC, Tolerance);
            Assert.AreEqual(0.0, control.UpfaderE, Tolerance); // Not sent.
            Assert.AreEqual(0.75, control.Crossfader, Tolerance);
            Assert.AreEqual(2, control.Active);
            Assert.AreEqual("Production", control.AppState);
        }

        [Test]
        public void BridgeControlHasSixFaders()
        {
            ControlPacket control = Control(WireFixtures.RustControl);
            Assert.AreEqual(2, control.Active);
            Assert.AreEqual(0.125, control.GetUpfader(5), Tolerance);
            Assert.AreEqual(0.0625, control.GetUpfader(6), Tolerance);
            Assert.AreEqual(0.0, control.GetUpfader(7), Tolerance);
        }

        [Test]
        public void MetaKeepsUnicodeAndHashesTheTitle()
        {
            foreach (string hex in new[] { WireFixtures.GoMeta, WireFixtures.RustMeta })
            {
                MetaPacket meta = Meta(hex);
                Assert.AreEqual(2, meta.Deck);
                Assert.AreEqual(WireFixtures.Title, meta.Title);
                Assert.AreEqual("Ünicode Artist", meta.Artist);
                Assert.AreEqual("Album", meta.Album);
                Assert.AreEqual("/music/track.flac", meta.FilePath);
                Assert.AreEqual(TrackHash, meta.TrackHash);
            }
        }

        [Test]
        public void LoopEnterWithPositions()
        {
            foreach (string hex in new[] { WireFixtures.GoLoopEnter, WireFixtures.RustLoopEnter })
            {
                EventPacket packet = Event(hex);
                Assert.AreEqual(CLXEventNames.LoopEnter, packet.EventName);
                Assert.AreEqual(2, packet.Deck);
                Assert.AreEqual(2, packet.Values.Length);
                Assert.AreEqual(32.5, packet.GetDouble(0), Tolerance);
                Assert.AreEqual(40.0, packet.GetDouble(1), Tolerance);
            }
        }

        [Test]
        public void LoopEnterFromTraktorHasOneValue()
        {
            EventPacket packet = Event(WireFixtures.GoLoopEnterTraktor);
            Assert.AreEqual(1, packet.Values.Length);
            Assert.AreEqual(4.0, packet.GetDouble(0), Tolerance);
        }

        [Test]
        public void LoopExitValuesAreEmptyEvenWhenGoSendsNil()
        {
            foreach (string hex in new[] { WireFixtures.GoLoopExit, WireFixtures.RustLoopExit })
            {
                EventPacket packet = Event(hex);
                Assert.AreEqual(CLXEventNames.LoopExit, packet.EventName);
                Assert.IsNotNull(packet.Values);
                Assert.AreEqual(0, packet.Values.Length);
                Assert.AreEqual((byte)0, packet.Value);
            }
        }

        [Test]
        public void CommandEventCarriesAnIntegerValue()
        {
            EventPacket packet = Event(WireFixtures.GoCommand);
            Assert.AreEqual("hotcue_1", packet.EventName);
            Assert.AreEqual(0, packet.Deck);
            Assert.AreEqual(7L, packet.Values[0]);
            Assert.AreEqual((byte)7, packet.Value);
            Assert.AreEqual(7.0, packet.GetDouble(0), Tolerance);
        }

        [Test]
        public void OldEventFormUsesASingleValue()
        {
            // {"Event": "x", "Value": 9}, as the CDJ firmware reads and CLX-UE expects.
            byte[] bytes = { 0x82, 0xa5, 0x45, 0x76, 0x65, 0x6e, 0x74, 0xa1, 0x78, 0xa5, 0x56, 0x61, 0x6c, 0x75, 0x65, 0x09 };
            Assert.IsTrue(CLXDecoder.TryDecodeEvent(bytes, 0, bytes.Length, out EventPacket packet));
            Assert.AreEqual((byte)9, packet.Value);
            Assert.AreEqual(0, packet.Values.Length);
        }

        [Test]
        public void ViewerLeaderHasAnArrayHashAndNoTx()
        {
            byte[] bytes = TestBytes.FromHex(WireFixtures.ViewerLeader);
            Assert.IsTrue(CLXDecoder.TryDecodeBinary(bytes, 1, bytes.Length - 1, out BinaryFragment fragment));
            Assert.AreEqual("leader", fragment.Type);
            Assert.AreEqual(32, fragment.Hash.Length);
            Assert.AreEqual(1u, fragment.TotalPackets);

            Assert.IsTrue(CLXDecoder.TryDecodeLeader(fragment.Data, 0, fragment.Data.Length, out LeaderPacket leader));
            Assert.AreEqual(2, leader.LiveDeck);
            Assert.AreEqual(123.5, leader.Position, Tolerance);
            Assert.AreEqual("Main Stage", leader.Project);
            Assert.IsFalse(leader.HasTx);
        }

        [Test]
        public void LeaderTxIsKeptAsAFullUInt64()
        {
            byte[] bytes = TestBytes.FromHex(WireFixtures.ViewerLeaderTx);
            Assert.IsTrue(CLXDecoder.TryDecodeBinary(bytes, 1, bytes.Length - 1, out BinaryFragment fragment));
            Assert.IsTrue(CLXDecoder.TryDecodeLeader(fragment.Data, 0, fragment.Data.Length, out LeaderPacket leader));
            Assert.IsTrue(leader.HasTx);
            Assert.AreEqual(1790000000123456789UL, leader.Tx);
        }

        [Test]
        public void UnknownKeysAreSkipped()
        {
            // {"Future": {"a": [1, 2]}, "Deck": 4}
            byte[] bytes =
            {
                0x82, 0xa6, 0x46, 0x75, 0x74, 0x75, 0x72, 0x65, 0x81, 0xa1, 0x61, 0x92, 0x01, 0x02,
                0xa4, 0x44, 0x65, 0x63, 0x6b, 0x04,
            };
            Assert.IsTrue(CLXDecoder.TryDecodeDeck(bytes, 0, bytes.Length, out DeckPacket deck));
            Assert.AreEqual(4, deck.Deck);
        }

        [Test]
        public void NonMapBodiesAreRejected()
        {
            byte[] bytes = { 0x91, 0x01 };
            Assert.IsFalse(CLXDecoder.TryDecodeDeck(bytes, 0, bytes.Length, out _));
            Assert.IsFalse(CLXDecoder.TryDecodeMeta(bytes, 0, bytes.Length, out _));
            Assert.IsFalse(CLXDecoder.TryDecodeControl(bytes, 0, bytes.Length, out _));
            Assert.IsFalse(CLXDecoder.TryDecodeEvent(bytes, 0, bytes.Length, out _));
        }

        [Test]
        public void HashNormalizing()
        {
            byte[] Ascii(string s) => System.Text.Encoding.ASCII.GetBytes(s);
            const string hash = "08c3e0ce1d50d6f0759bc5a870ede817";
            Assert.AreEqual(hash, CLXHash.Normalize(Ascii(hash)));
            Assert.AreEqual(hash, CLXHash.Normalize(Ascii(hash.ToUpperInvariant())));
            Assert.AreEqual(hash, CLXHash.Normalize(Ascii(hash + "\0\0")));
            Assert.AreEqual(hash, CLXHash.Normalize(Ascii(hash + "  ")));
            Assert.IsNull(CLXHash.Normalize(Ascii("../../etc/passwd")));
            Assert.IsNull(CLXHash.Normalize(Ascii("g8c3e0ce1d50d6f0759bc5a870ede817")));
            Assert.IsNull(CLXHash.Normalize(new byte[32]));
            Assert.IsNull(CLXHash.Normalize(null));
        }

        [Test]
        public void TitleHashMatchesMd5OfUtf8()
        {
            // From `printf %s 'Déjà Vu (Extended Mix)' | md5`.
            Assert.AreEqual("ea6c883362d20d1b9aa412967735934c", CLXHash.ForTitle(WireFixtures.Title));
            Assert.AreEqual(string.Empty, CLXHash.ForTitle(string.Empty));
        }
    }
}
