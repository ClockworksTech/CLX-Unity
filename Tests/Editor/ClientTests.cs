using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;

namespace Clockworks.CLX.Tests
{
    [TestFixture]
    public class ClientTests
    {
        const string Source = "10.0.0.5";

        static void Feed(CLXClient client, string hex, string source = Source)
        {
            byte[] bytes = TestBytes.FromHex(hex);
            client.ProcessDatagram(bytes, bytes.Length, source);
        }

        static void Feed(CLXClient client, IEnumerable<string> fixture, string source = Source)
        {
            foreach (string hex in fixture) Feed(client, hex, source);
        }

        static string NewTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "clx-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        [Test]
        public void EventsWaitForPumpAndStateDoesNot()
        {
            var client = new CLXClient();
            int decks = 0;
            client.DeckReceived += packet => decks++;

            Feed(client, WireFixtures.GoDeck);
            Assert.AreEqual(0, decks);
            Assert.IsTrue(client.TryGetDeck(2, out DeckPacket deck));
            Assert.AreEqual(12.5, deck.Position);

            Assert.AreEqual(1, client.Pump());
            Assert.AreEqual(1, decks);
            Assert.AreEqual(0, client.Pump());
        }

        [Test]
        public void UnknownDeckReadsAsAbsent()
        {
            var client = new CLXClient();
            Assert.IsFalse(client.TryGetDeck(1, out _));
            Assert.IsFalse(client.TryGetDeck(-1, out _));
            Assert.IsFalse(client.TryGetDeck(9999, out _));
            Assert.IsFalse(client.TryGetMeta(1, out _));
            Assert.IsFalse(client.TryGetControl(out _));
            Assert.IsFalse(client.TryGetLeader(out _));
            Assert.IsNull(client.GetWaveform(1));
        }

        [Test]
        public void ControlEventAndLeaderReachListeners()
        {
            var client = new CLXClient();
            ControlPacket control = default;
            EventPacket received = default;
            LeaderPacket leader = default;
            client.ControlReceived += packet => control = packet;
            client.EventReceived += packet => received = packet;
            client.LeaderReceived += packet => leader = packet;

            Feed(client, WireFixtures.GoControl);
            Feed(client, WireFixtures.GoLoopEnter);
            Feed(client, WireFixtures.ViewerLeader);
            client.Pump();

            Assert.AreEqual(2, control.Active);
            Assert.AreEqual("loop_enter", received.EventName);
            Assert.AreEqual(2, leader.LiveDeck);
            Assert.IsTrue(client.TryGetLeader(out LeaderPacket latest));
            Assert.AreEqual(123.5, latest.Position);
        }

        [Test]
        public void TrackChangedFiresOnceAcrossRepeatedMeta()
        {
            var client = new CLXClient();
            int meta = 0, changed = 0;
            client.MetaReceived += packet => meta++;
            client.TrackChanged += packet => changed++;

            Feed(client, WireFixtures.GoMeta);
            Feed(client, WireFixtures.GoMeta);
            Feed(client, WireFixtures.GoMeta);
            client.Pump();

            Assert.AreEqual(3, meta);
            Assert.AreEqual(1, changed);
        }

        [Test]
        public void WaveformBeatgridAndCuesAttachToTheLoadedTrack()
        {
            var client = new CLXClient();
            var seen = new List<string>();
            client.WaveformComplete += waveform => seen.Add("waveform");
            client.BeatgridComplete += grid => seen.Add("beatgrid");
            client.CuesComplete += cues => seen.Add("cues");

            Feed(client, WireFixtures.GoMeta);
            Feed(client, WireFixtures.GoWaveform);
            Feed(client, WireFixtures.GoBeatgrid);
            Feed(client, WireFixtures.GoCues);
            client.Pump();

            Assert.AreEqual(new List<string> { "waveform", "beatgrid", "cues" }, seen);
            Assert.AreEqual(2000, client.GetWaveform(2).Length);
            Assert.AreEqual(3, client.GetBeatgrid(2).Markers.Length);
            Assert.AreEqual(2, client.GetCues(2).Cues.Length);
            Assert.IsNull(client.GetWaveform(1));
            Assert.AreEqual(0L, client.PacketsMalformed);
        }

        [Test]
        public void DataThatArrivesBeforeMetaStillAttaches()
        {
            var client = new CLXClient();
            Feed(client, WireFixtures.GoWaveform);
            Feed(client, WireFixtures.GoMeta);
            Assert.IsNotNull(client.GetWaveform(2));
        }

        [Test]
        public void DiskCacheServesATrackLoadedLater()
        {
            string directory = NewTempDirectory();
            try
            {
                var first = new CLXClient(new CLXClientOptions { CacheDirectory = directory });
                Feed(first, WireFixtures.GoWaveform);
                Feed(first, WireFixtures.GoBeatgrid);
                Feed(first, WireFixtures.GoCues);

                string hash = CLXHash.ForTitle(WireFixtures.Title);
                Assert.IsTrue(File.Exists(Path.Combine(directory, hash + ".rwf")));
                Assert.IsTrue(File.Exists(Path.Combine(directory, hash + ".bg")));
                Assert.IsTrue(File.Exists(Path.Combine(directory, hash + ".cues")));

                var second = new CLXClient(new CLXClientOptions { CacheDirectory = directory });
                CLXWaveform loaded = null;
                second.WaveformComplete += waveform => loaded = waveform;
                Feed(second, WireFixtures.GoMeta);
                second.Pump();

                Assert.IsNotNull(loaded);
                Assert.AreEqual(Path.Combine(directory, hash + ".rwf"), loaded.FilePath);
                Assert.IsNotNull(second.GetBeatgrid(2));
                Assert.IsNotNull(second.GetCues(2));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void AWaveformIsKeptUnlessTheNewOneSaysReplace()
        {
            string directory = NewTempDirectory();
            try
            {
                var client = new CLXClient(new CLXClientOptions { CacheDirectory = directory });
                string path = Path.Combine(directory, CLXHash.ForTitle(WireFixtures.Title) + ".rwf");

                Feed(client, WireFixtures.GoWaveform);
                File.WriteAllText(path, "marker: the cached file");

                Feed(client, WireFixtures.ProlinkWaveform); // No Replace.
                Assert.AreEqual("marker: the cached file", File.ReadAllText(path));

                Feed(client, WireFixtures.RustWaveform); // Replace = true.
                Assert.AreNotEqual("marker: the cached file", File.ReadAllText(path));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void SourcesAreDiscovered()
        {
            var client = new CLXClient();
            Assert.IsFalse(client.SourcesFlag);

            Feed(client, WireFixtures.GoDeck, "10.0.0.1");
            Feed(client, WireFixtures.GoControl, "10.0.0.2");
            Assert.IsTrue(client.SourcesFlag);
            Assert.AreEqual(2, client.DiscoveredSources.Length);

            client.ResetSourcesFlag();
            Feed(client, WireFixtures.GoControl, "10.0.0.2");
            Assert.IsFalse(client.SourcesFlag);
        }

        [Test]
        public void SourceSelectionFiltersByPacketKind()
        {
            var client = new CLXClient();
            client.Options.SourceSelection.Deck = "10.0.0.1";

            Feed(client, WireFixtures.GoDeck, "10.0.0.2");
            Feed(client, WireFixtures.GoControl, "10.0.0.2");
            Assert.IsFalse(client.TryGetDeck(2, out _));
            Assert.IsTrue(client.TryGetControl(out _));
            Assert.AreEqual(1, client.DiscoveredSources.Length); // Still discovered.

            Feed(client, WireFixtures.GoDeck, "10.0.0.1");
            Assert.IsTrue(client.TryGetDeck(2, out _));
        }

        [Test]
        public void AThrowingHandlerDoesNotLoseTheQueue()
        {
            var client = new CLXClient();
            var errors = new List<Exception>();
            client.HandlerError += errors.Add;
            int controls = 0;
            client.DeckReceived += packet => throw new InvalidOperationException("boom");
            client.ControlReceived += packet => controls++;

            Feed(client, WireFixtures.GoDeck);
            Feed(client, WireFixtures.GoControl);
            client.Pump();

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(1, controls);
        }

        [Test]
        public void AnUnpumpedQueueStaysBounded()
        {
            var client = new CLXClient();
            for (int i = 0; i < CLXClient.MaxQueuedEvents + 100; i++)
            {
                Feed(client, WireFixtures.GoControl);
            }
            Assert.AreEqual(CLXClient.MaxQueuedEvents, client.Pump());
        }

        [Test]
        public void IgnoresPacketsThatAreNotForAReceiver()
        {
            var client = new CLXClient();
            client.ProcessDatagram(new byte[] { CLXPacketType.WaveformRequest, 1 }, 2, Source);
            client.ProcessDatagram(new byte[] { CLXPacketType.Join }, 1, Source);
            client.ProcessDatagram(new byte[] { 0x7f, 1, 2 }, 3, Source);
            client.ProcessDatagram(new byte[0], 0, Source);
            Assert.AreEqual(0L, client.PacketsReceived);
            Assert.AreEqual(0, client.DiscoveredSources.Length);
        }

        [Test]
        public void GarbageNeverThrows()
        {
            var fixtures = new List<string>
            {
                WireFixtures.GoDeck, WireFixtures.RustDeck, WireFixtures.GoMeta, WireFixtures.GoControl,
                WireFixtures.GoLoopEnter, WireFixtures.ViewerLeader, WireFixtures.GoCues[0],
                WireFixtures.GoBeatgrid[0], WireFixtures.GoWaveform[0], WireFixtures.ViewerWaveform[0],
            };
            var random = new Random(12345);
            var client = new CLXClient();

            foreach (string hex in fixtures)
            {
                byte[] clean = TestBytes.FromHex(hex);
                for (int length = 0; length < clean.Length; length += 7)
                {
                    client.ProcessDatagram(clean, length, Source); // Truncated.
                }
                for (int round = 0; round < 500; round++)
                {
                    var bytes = (byte[])clean.Clone();
                    for (int i = 0; i < 8; i++) bytes[1 + random.Next(bytes.Length - 1)] = (byte)random.Next(256);
                    client.ProcessDatagram(bytes, bytes.Length, Source); // Corrupted.
                }
            }
            client.Pump();
        }

        // The rest run over real UDP sockets on loopback.

        static int FreeUdpPort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        static bool WaitFor(Func<bool> condition, int timeoutMilliseconds = 3000)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMilliseconds)
            {
                if (condition()) return true;
                Thread.Sleep(5);
            }
            return condition();
        }

        static void Send(UdpClient socket, string hex, int port, string address = "127.0.0.1")
        {
            byte[] bytes = TestBytes.FromHex(hex);
            socket.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse(address), port));
        }

        [Test]
        public void ReceivesOverUdp()
        {
            int port = FreeUdpPort();
            using (var client = new CLXClient(new CLXClientOptions
            {
                ListenPort = port,
                MulticastListen = false,
                ListenIPAddress = "127.0.0.1",
                SenderCommandPort = FreeUdpPort(),
                AutoRequestWaveforms = false,
            }))
            using (var sender = new UdpClient())
            {
                Assert.IsTrue(client.Start(), client.LastError);
                Assert.IsTrue(client.IsListening);

                Send(sender, WireFixtures.GoDeck, port);
                Assert.IsTrue(WaitFor(() => client.TryGetDeck(2, out _)));
                Assert.AreEqual("127.0.0.1", client.DiscoveredSources[0]);

                client.Stop();
                Assert.IsFalse(client.IsListening);
            }
        }

        [Test]
        public void JoinsThenRequestsAndReceivesTheWaveform()
        {
            int port = FreeUdpPort();
            using (var commandSocket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (var client = new CLXClient(new CLXClientOptions
            {
                ListenPort = port,
                MulticastListen = false,
                ListenIPAddress = "127.0.0.1",
                SenderCommandPort = ((IPEndPoint)commandSocket.Client.LocalEndPoint).Port,
                AnnounceIntervalSeconds = 0,
            }))
            {
                commandSocket.Client.ReceiveTimeout = 3000;
                Assert.IsTrue(client.Start(), client.LastError);

                var from = new IPEndPoint(IPAddress.Any, 0);
                byte[] join = commandSocket.Receive(ref from);
                Assert.AreEqual(new[] { CLXPacketType.Join }, join);

                // Loading a track is what makes the receiver ask for its waveform.
                Send(commandSocket, WireFixtures.GoMeta, port);
                byte[] request = commandSocket.Receive(ref from);
                Assert.AreEqual(new byte[] { CLXPacketType.WaveformRequest, 2 }, request);

                foreach (string fragment in WireFixtures.GoWaveform) Send(commandSocket, fragment, port);
                Assert.IsTrue(WaitFor(() => client.GetWaveform(2) != null));
                Assert.AreEqual(2000, client.GetWaveform(2).Length);
            }
        }

        [Test]
        public void ReceivesMulticastAlongsideAnotherListener()
        {
            int port = FreeUdpPort();
            var options = new CLXClientOptions
            {
                ListenPort = port,
                SenderCommandPort = FreeUdpPort(),
                AutoRequestWaveforms = false,
            };
            var otherOptions = new CLXClientOptions
            {
                ListenPort = port,
                SenderCommandPort = options.SenderCommandPort,
                AutoRequestWaveforms = false,
            };
            using (var client = new CLXClient(options))
            using (var other = new CLXClient(otherOptions)) // A Viewer on the same machine, say.
            using (var sender = new UdpClient(AddressFamily.InterNetwork))
            {
                Assert.IsTrue(client.Start(), client.LastError);
                Assert.IsTrue(other.Start(), other.LastError);

                sender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                sender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
                // Loopback, so this works with no network and doesn't reach the LAN.
                sender.Client.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.MulticastInterface,
                    IPAddress.Loopback.GetAddressBytes());

                // A loaded test machine can lose a datagram, so keep sending.
                bool received = WaitFor(() =>
                {
                    Send(sender, WireFixtures.GoDeck, port, CLXClient.MulticastGroup);
                    return client.TryGetDeck(2, out _) && other.TryGetDeck(2, out _);
                });
                Assert.IsTrue(received, "Multicast didn't reach both listeners.");
            }
        }
    }
}
