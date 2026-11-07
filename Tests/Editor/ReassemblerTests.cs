using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Clockworks.CLX.Tests
{
    [TestFixture]
    public class ReassemblerTests
    {
        static BinaryFragment Fragment(uint order, uint totalPackets, ulong total, params byte[] data) =>
            new BinaryFragment
            {
                Type = "waveform",
                Hash = new byte[] { 1, 2, 3 },
                Order = order,
                TotalPackets = totalPackets,
                Total = total,
                Data = data,
            };

        static List<BinaryFragment> Decode(string[] fixture)
        {
            var fragments = new List<BinaryFragment>();
            foreach (string hex in fixture)
            {
                byte[] bytes = TestBytes.FromHex(hex);
                Assert.IsTrue(CLXDecoder.TryDecodeBinary(bytes, 1, bytes.Length - 1, out BinaryFragment fragment));
                fragments.Add(fragment);
            }
            return fragments;
        }

        [Test]
        public void JoinsInAnyOrder()
        {
            var reassembler = new CLXReassembler();
            Assert.IsFalse(reassembler.Add("a", Fragment(2, 3, 5, 5), 0, out _));
            Assert.IsFalse(reassembler.Add("a", Fragment(0, 3, 5, 1, 2), 0, out _));
            Assert.IsTrue(reassembler.Add("a", Fragment(1, 3, 5, 3, 4), 0, out byte[] payload));
            Assert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, payload);
            Assert.AreEqual(0, reassembler.PendingCount);
        }

        [Test]
        public void RepeatedFragmentsCountOnce()
        {
            var reassembler = new CLXReassembler();
            Assert.IsFalse(reassembler.Add("a", Fragment(0, 2, 4, 1, 2), 0, out _));
            Assert.IsFalse(reassembler.Add("a", Fragment(0, 2, 4, 1, 2), 0, out _));
            Assert.IsTrue(reassembler.Add("a", Fragment(1, 2, 4, 3, 4), 0, out byte[] payload));
            Assert.AreEqual(new byte[] { 1, 2, 3, 4 }, payload);
        }

        [Test]
        public void WithoutTotalPacketsItCompletesOnTheByteTotal()
        {
            var reassembler = new CLXReassembler();
            Assert.IsFalse(reassembler.Add("a", Fragment(1, 0, 4, 3, 4), 0, out _));
            Assert.IsTrue(reassembler.Add("a", Fragment(0, 0, 4, 1, 2), 0, out byte[] payload));
            Assert.AreEqual(new byte[] { 1, 2, 3, 4 }, payload);
        }

        [Test]
        public void WithoutTotalPacketsAGapBlocksCompletion()
        {
            var reassembler = new CLXReassembler();
            // The bytes add up, but order 1 is missing: an oversize fragment can't fill it.
            Assert.IsFalse(reassembler.Add("a", Fragment(0, 0, 4, 1, 2), 0, out _));
            Assert.IsFalse(reassembler.Add("a", Fragment(2, 0, 4, 5, 6), 0, out _));
        }

        [Test]
        public void ARetryAfterLossDoesNotSpliceIntoTheOldAttempt()
        {
            var reassembler = new CLXReassembler();
            // First attempt loses order 1. The retry has a different Total.
            Assert.IsFalse(reassembler.Add("a", Fragment(0, 2, 4, 1, 2), 0, out _));
            Assert.IsFalse(reassembler.Add("a", Fragment(0, 2, 6, 7, 7, 7), 10, out _));
            Assert.IsTrue(reassembler.Add("a", Fragment(1, 2, 6, 8, 8, 8), 20, out byte[] payload));
            Assert.AreEqual(new byte[] { 7, 7, 7, 8, 8, 8 }, payload);
        }

        [Test]
        public void ADifferentPayloadOfTheSameShapeRestarts()
        {
            var reassembler = new CLXReassembler();
            Assert.IsFalse(reassembler.Add("a", Fragment(0, 2, 4, 1, 2), 0, out _));
            Assert.IsFalse(reassembler.Add("a", Fragment(0, 2, 4, 9, 9), 10, out _));
            Assert.IsTrue(reassembler.Add("a", Fragment(1, 2, 4, 8, 8), 20, out byte[] payload));
            Assert.AreEqual(new byte[] { 9, 9, 8, 8 }, payload);
        }

        [Test]
        public void TwoSendersDoNotMix()
        {
            var reassembler = new CLXReassembler();
            Assert.IsFalse(reassembler.Add("a", Fragment(0, 2, 2, 1), 0, out _));
            Assert.IsFalse(reassembler.Add("b", Fragment(0, 2, 2, 9), 0, out _));
            Assert.IsTrue(reassembler.Add("a", Fragment(1, 2, 2, 2), 0, out byte[] payload));
            Assert.AreEqual(new byte[] { 1, 2 }, payload);
        }

        [Test]
        public void StaleTransfersExpire()
        {
            var reassembler = new CLXReassembler();
            reassembler.Add("a", Fragment(0, 2, 4, 1, 2), 0, out _);
            Assert.AreEqual(1, reassembler.PendingCount);
            reassembler.Add("b", Fragment(0, 2, 4, 1, 2), CLXReassembler.TimeoutMilliseconds, out _);
            Assert.AreEqual(1, reassembler.PendingCount);
        }

        [Test]
        public void HostileSizesAreRefused()
        {
            var reassembler = new CLXReassembler();
            Assert.IsFalse(reassembler.Add("a", Fragment(0, 1, CLXReassembler.MaxPayloadBytes + 1, 1), 0, out _));
            Assert.IsFalse(reassembler.Add("a", Fragment(5, 2, 4, 1), 0, out _));
            Assert.AreEqual(0, reassembler.PendingCount);
        }

        [Test]
        public void RealFragmentsRebuildTheSamePayloadInAnyOrder()
        {
            foreach (string[] fixture in new[]
            {
                WireFixtures.GoWaveform,
                WireFixtures.ProlinkWaveform,
                WireFixtures.RustWaveform,
                WireFixtures.ViewerWaveform,
            })
            {
                List<BinaryFragment> fragments = Decode(fixture);
                Assert.IsTrue(fragments.Count > 1);

                byte[] expected = Reassemble(fragments);
                fragments.Reverse();
                Assert.AreEqual(expected, Reassemble(fragments));

                Assert.IsTrue(CLXDecoder.TryDecodeWaveform(expected, out WaveformPayload waveform));
                Assert.AreEqual(CLXHash.ForTitle(WireFixtures.Title), waveform.Hash);
            }
        }

        static byte[] Reassemble(List<BinaryFragment> fragments)
        {
            var reassembler = new CLXReassembler();
            byte[] result = null;
            foreach (BinaryFragment fragment in fragments)
            {
                if (reassembler.Add("a", fragment, 0, out byte[] payload)) result = payload;
            }
            Assert.IsNotNull(result);
            return result;
        }
    }

    [TestFixture]
    public class PacketFilterTests
    {
        static readonly byte[] BytesA = { 1, 2, 3 };
        static readonly byte[] BytesB = { 4, 5, 6 };

        static DeckPacket WithOrd(int ord) => new DeckPacket { Deck = 1, Ord = ord, HasOrd = true };

        static DeckPacket WithoutOrd() => new DeckPacket { Deck = 1 };

        [Test]
        public void MirroredPacketWithOrdIsADuplicate()
        {
            var filter = new CLXPacketFilter();
            DeckPacket packet = WithOrd(10);
            Assert.AreEqual(CLXPacketVerdict.Accept, filter.CheckDeck("a", packet, BytesA, 0, 3, 0));
            Assert.AreEqual(CLXPacketVerdict.Duplicate, filter.CheckDeck("a", packet, BytesA, 0, 3, 1));
        }

        [Test]
        public void ASenderStuckOnOneOrdStillGetsThrough()
        {
            var filter = new CLXPacketFilter();
            filter.CheckDeck("a", WithOrd(0), BytesA, 0, 3, 0);
            Assert.AreEqual(CLXPacketVerdict.Accept, filter.CheckDeck("a", WithOrd(0), BytesB, 0, 3, 16));
        }

        [Test]
        public void OlderOrdIsLateAndWrappingIsNot()
        {
            var filter = new CLXPacketFilter();
            filter.CheckDeck("a", WithOrd(254), BytesA, 0, 3, 0);
            Assert.AreEqual(CLXPacketVerdict.Accept, filter.CheckDeck("a", WithOrd(2), BytesB, 0, 3, 16));
            Assert.AreEqual(CLXPacketVerdict.Late, filter.CheckDeck("a", WithOrd(255), BytesA, 0, 3, 17));
        }

        [Test]
        public void AfterAGapOrderingStartsOver()
        {
            var filter = new CLXPacketFilter();
            filter.CheckDeck("a", WithOrd(100), BytesA, 0, 3, 0);
            Assert.AreEqual(CLXPacketVerdict.Accept, filter.CheckDeck("a", WithOrd(50), BytesB, 0, 3, 5000));
        }

        [Test]
        public void WithoutOrdOnlyAnImmediateCopyIsDropped()
        {
            var filter = new CLXPacketFilter();
            DeckPacket packet = WithoutOrd();
            Assert.AreEqual(CLXPacketVerdict.Accept, filter.CheckDeck("a", packet, BytesA, 0, 3, 0));
            Assert.AreEqual(CLXPacketVerdict.Duplicate, filter.CheckDeck("a", packet, BytesA, 0, 3, 1));
            // A paused deck repeats itself at 60 Hz, and those packets are real.
            Assert.AreEqual(CLXPacketVerdict.Accept, filter.CheckDeck("a", packet, BytesA, 0, 3, 16));
        }

        [Test]
        public void SendersAndDecksAreJudgedSeparately()
        {
            var filter = new CLXPacketFilter();
            filter.CheckDeck("a", WithOrd(100), BytesA, 0, 3, 0);
            Assert.AreEqual(CLXPacketVerdict.Accept, filter.CheckDeck("b", WithOrd(50), BytesA, 0, 3, 1));
            var otherDeck = new DeckPacket { Deck = 2, Ord = 50, HasOrd = true };
            Assert.AreEqual(CLXPacketVerdict.Accept, filter.CheckDeck("a", otherDeck, BytesA, 0, 3, 1));
        }

        [Test]
        public void AMirroredEventFiresOnceButARepeatLaterFiresAgain()
        {
            var filter = new CLXPacketFilter();
            Assert.IsFalse(filter.IsDuplicateEvent("a", BytesA, 0, 3, 0));
            Assert.IsTrue(filter.IsDuplicateEvent("a", BytesA, 0, 3, 2));
            Assert.IsFalse(filter.IsDuplicateEvent("a", BytesA, 0, 3, 500));
            Assert.IsFalse(filter.IsDuplicateEvent("a", BytesB, 0, 3, 501));
        }
    }
}
