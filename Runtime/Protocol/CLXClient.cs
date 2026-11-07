using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Clockworks.CLX
{
    /// <summary>
    /// Restricts which sender each kind of packet is accepted from. An empty
    /// value accepts any sender.
    /// </summary>
    [Serializable]
    public class CLXSourceSelection
    {
        public string Control = string.Empty;
        public string Deck = string.Empty;
        public string Meta = string.Empty;

        /// <summary>Waveforms, beat grids, cues and leader packets.</summary>
        public string Binary = string.Empty;

        public string Event = string.Empty;

        internal bool Allows(byte packetType, string source)
        {
            string wanted;
            switch (packetType)
            {
                case CLXPacketType.Control: wanted = Control; break;
                case CLXPacketType.Deck: wanted = Deck; break;
                case CLXPacketType.Meta: wanted = Meta; break;
                case CLXPacketType.Binary: wanted = Binary; break;
                case CLXPacketType.Event: wanted = Event; break;
                default: return true;
            }
            return string.IsNullOrEmpty(wanted) || wanted == source;
        }
    }

    /// <summary>Settings for a <see cref="CLXClient"/>. Read when it starts.</summary>
    public sealed class CLXClientOptions
    {
        /// <summary>
        /// A local address to listen on, or 0.0.0.0 for all of them. With
        /// multicast on, the socket still binds every address (Linux won't deliver
        /// multicast to a socket bound to a unicast address, and a waveform reply
        /// is unicast), and this picks the interface that joins the group.
        /// </summary>
        public string ListenIPAddress = "0.0.0.0";

        public int ListenPort = CLXClient.DefaultPort;

        /// <summary>The port senders listen for joins and waveform requests on.</summary>
        public int SenderCommandPort = CLXClient.CommandPort;

        /// <summary>Also listen to the CLX multicast group. Unicast keeps working.</summary>
        public bool MulticastListen = true;

        /// <summary>
        /// Seconds between join packets, or 0 to send only one. Repeating them
        /// finds a sender that starts after the receiver. The Viewer also sends
        /// one every 5 seconds, and a sender answers by resending its metadata.
        /// </summary>
        public double AnnounceIntervalSeconds = 5;

        /// <summary>
        /// Ask for a track's waveform when it's loaded and not cached. Without
        /// this a waveform arrives only when the sender first generates it.
        /// </summary>
        public bool AutoRequestWaveforms = true;

        /// <summary>
        /// Folder for received <c>.rwf</c>, <c>.bg</c> and <c>.cues</c> files.
        /// Null keeps them in memory only.
        /// </summary>
        public string CacheDirectory;

        /// <summary>Per-packet sender filter. It can be changed while running.</summary>
        public CLXSourceSelection SourceSelection = new CLXSourceSelection();
    }

    /// <summary>
    /// Receives a CLX stream. This is the protocol layer on its own: it has no
    /// Unity dependency, and a <see cref="T:Clockworks.CLX.CLXReceiver"/> wraps it
    /// as a component.
    ///
    /// Packets are received on a background thread. The latest state can be read
    /// from any thread, and events fire on whichever thread calls <see cref="Pump"/>.
    /// </summary>
    public sealed class CLXClient : IDisposable
    {
        /// <summary>The port senders stream to.</summary>
        public const int DefaultPort = 3650;

        /// <summary>The group senders can mirror their stream to.</summary>
        public const string MulticastGroup = "239.0.0.1";

        /// <summary>The port senders listen for joins and waveform requests on.</summary>
        public const int CommandPort = 7000;

        /// <summary>Events waiting for <see cref="Pump"/> are dropped, oldest first, past this.</summary>
        public const int MaxQueuedEvents = 2048;

        const int DeckSlots = 256;
        const int MaxTracksInMemory = 32;
        const int MaxWaveformRequests = 5;
        const long WaveformRequestIntervalMilliseconds = 5000;
        const int PollMicroseconds = 50000;
        const int ReceiveBufferBytes = 2 * 1024 * 1024;

        enum EventKind
        {
            Control,
            Deck,
            Meta,
            Event,
            Leader,
            TrackChanged,
            Waveform,
            Beatgrid,
            Cues,
        }

        struct QueuedEvent
        {
            public EventKind Kind;
            public DeckPacket Deck; // Inline so 60 Hz deck traffic doesn't allocate.
            public object Payload;
        }

        sealed class DeckSlot
        {
            public DeckPacket Deck;
            public bool HasDeck;
            public MetaPacket Meta;
            public bool HasMeta;
            public string TrackHash = string.Empty;
            public string Source = string.Empty;
            public int WaveformRequests;
            public long NextWaveformRequest;
        }

        sealed class Track
        {
            public long LastUsed;
            public CLXWaveform Waveform;
            public BeatgridPayload Beatgrid;
            public CuesPayload Cues;
        }

        // Filled by the receive thread, drained by Pump.
        readonly object _queueLock = new object();
        Queue<QueuedEvent> _incoming = new Queue<QueuedEvent>();
        Queue<QueuedEvent> _dispatching = new Queue<QueuedEvent>();

        // Guards the state below, which any thread can read.
        readonly object _stateLock = new object();
        readonly DeckSlot[] _decks = new DeckSlot[DeckSlots];
        readonly Dictionary<string, Track> _tracks = new Dictionary<string, Track>();
        readonly HashSet<string> _sources = new HashSet<string>();
        ControlPacket _control;
        bool _hasControl;
        LeaderPacket _leader;
        bool _hasLeader;
        string[] _sourceList = Array.Empty<string>();
        bool _sourcesFlag;
        long _trackUseCounter;

        // Only the receive thread touches these after Start.
        readonly CLXReassembler _reassembler = new CLXReassembler();
        readonly CLXPacketFilter _filter = new CLXPacketFilter();
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly HashSet<string> _joinedAddresses = new HashSet<string>();
        long _nextAnnounce;

        Socket _socket;
        Socket _sendSocket;
        Thread _thread;
        volatile bool _running;
        volatile string _lastError = string.Empty;
        long _packetsReceived;
        long _packetsDropped;
        long _packetsMalformed;
        long _lastPacketAt = -1;

        public CLXClient(CLXClientOptions options = null)
        {
            Options = options ?? new CLXClientOptions();
        }

        public CLXClientOptions Options { get; }

        /// <summary>True between a successful <see cref="Start"/> and <see cref="Stop"/>.</summary>
        public bool IsListening => _running;

        /// <summary>The latest socket or cache problem. Empty when there hasn't been one.</summary>
        public string LastError => _lastError;

        public long PacketsReceived => Interlocked.Read(ref _packetsReceived);

        /// <summary>Repeated or out-of-order packets that were dropped.</summary>
        public long PacketsDropped => Interlocked.Read(ref _packetsDropped);

        /// <summary>Packets that couldn't be decoded.</summary>
        public long PacketsMalformed => Interlocked.Read(ref _packetsMalformed);

        /// <summary>Seconds since any CLX packet arrived, or infinity if none has.</summary>
        public double SecondsSinceLastPacket
        {
            get
            {
                long at = Interlocked.Read(ref _lastPacketAt);
                return at < 0 ? double.PositiveInfinity : (_clock.ElapsedMilliseconds - at) / 1000.0;
            }
        }

        public event Action<ControlPacket> ControlReceived;
        public event Action<DeckPacket> DeckReceived;

        /// <summary>Every Meta packet. Senders repeat these, so see <see cref="TrackChanged"/>.</summary>
        public event Action<MetaPacket> MetaReceived;

        public event Action<EventPacket> EventReceived;
        public event Action<LeaderPacket> LeaderReceived;

        /// <summary>Fires once when a deck's title or file changes.</summary>
        public event Action<MetaPacket> TrackChanged;

        public event Action<CLXWaveform> WaveformComplete;
        public event Action<BeatgridPayload> BeatgridComplete;
        public event Action<CuesPayload> CuesComplete;

        /// <summary>
        /// Gets exceptions thrown by event handlers during <see cref="Pump"/>, so one
        /// bad handler doesn't lose the rest of the queue. Without a subscriber
        /// they propagate out of Pump.
        /// </summary>
        public event Action<Exception> HandlerError;

        /// <summary>Opens the sockets and starts receiving. False if it failed; see <see cref="LastError"/>.</summary>
        public bool Start()
        {
            if (_running) return false;

            _lastError = string.Empty;
            _reassembler.Clear();
            _filter.Clear();
            _joinedAddresses.Clear();
            _nextAnnounce = 0;

            try
            {
                string listen = Options.ListenIPAddress;
                IPAddress listenAddress = string.IsNullOrEmpty(listen) ? IPAddress.Any : IPAddress.Parse(listen);
                IPAddress bindAddress = Options.MulticastListen ? IPAddress.Any : listenAddress;

                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                // Another CLX app on this machine may hold the port. The group
                // reaches both of them, but a unicast packet reaches only one.
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _socket.ReceiveBufferSize = ReceiveBufferBytes;
                _socket.Bind(new IPEndPoint(bindAddress, Options.ListenPort));

                _sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _sendSocket.EnableBroadcast = true;
                _sendSocket.Bind(new IPEndPoint(IPAddress.Any, 0));

                _running = true;
                JoinMulticast();
                _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "CLX Receiver" };
                _thread.Start();
                return true;
            }
            catch (Exception e)
            {
                _lastError = e.Message;
                Stop();
                return false;
            }
        }

        /// <summary>Stops receiving and closes the sockets. State and the queue are kept.</summary>
        public void Stop()
        {
            _running = false;
            if (_thread != null && _thread != Thread.CurrentThread)
            {
                _thread.Join(1000);
            }
            _thread = null;
            _socket?.Close();
            _socket = null;
            _sendSocket?.Close();
            _sendSocket = null;
        }

        public void Dispose() => Stop();

        /// <summary>
        /// Sends the join packet, which asks senders to resend their state:
        /// metadata, beat grids and cues for every deck. Waveforms come only on
        /// request.
        /// </summary>
        public void Announce()
        {
            Socket socket = _sendSocket;
            if (socket == null) return;
            var packet = new[] { CLXPacketType.Join };
            foreach (IPAddress target in GetAnnounceTargets())
            {
                SendTo(socket, packet, target, Options.SenderCommandPort);
            }
        }

        /// <summary>
        /// Asks the sender of a deck for its waveform, and false if no sender is
        /// known for that deck yet. The reply raises <see cref="WaveformComplete"/>.
        /// </summary>
        public bool RequestWaveform(int deck)
        {
            string source;
            lock (_stateLock)
            {
                DeckSlot slot = SlotOrNull(deck);
                source = slot?.Source;
            }
            return !string.IsNullOrEmpty(source) && SendWaveformRequest(deck, source);
        }

        bool SendWaveformRequest(int deck, string source)
        {
            Socket socket = _sendSocket;
            if (socket == null || !IPAddress.TryParse(source, out IPAddress address)) return false;
            var packet = new[] { CLXPacketType.WaveformRequest, (byte)deck };
            // A sender listens on the command port, a Viewer relaying the stream on
            // the CLX port. Whichever is listening answers, and the other ignores it.
            SendTo(socket, packet, address, Options.SenderCommandPort);
            SendTo(socket, packet, address, Options.ListenPort);
            return true;
        }

        void SendTo(Socket socket, byte[] packet, IPAddress address, int port)
        {
            try
            {
                socket.SendTo(packet, new IPEndPoint(address, port));
            }
            catch (SocketException e)
            {
                // Expected while a network is down or an interface has no route.
                // The next announce retries.
                _lastError = "Couldn't send to " + address + ": " + e.Message;
            }
            catch (ObjectDisposedException)
            {
                // Stop ran on another thread.
            }
        }

        List<IPAddress> GetAnnounceTargets()
        {
            var targets = new List<IPAddress>();
            string listen = Options.ListenIPAddress;
            bool anyAddress = string.IsNullOrEmpty(listen) || listen == "0.0.0.0";

            foreach (CLXNetwork.LocalAddress local in CLXNetwork.GetLocalAddresses())
            {
                if (local.Broadcast == null) continue;
                if (!anyAddress && local.Address.ToString() != listen) continue;
                if (!targets.Contains(local.Broadcast)) targets.Add(local.Broadcast);
            }

            if (!anyAddress && IPAddress.IsLoopback(IPAddress.Parse(listen)))
            {
                targets.Add(IPAddress.Loopback);
                return targets;
            }
            if (targets.Count == 0 || anyAddress) targets.Add(IPAddress.Broadcast);
            // On macOS a limited broadcast never reaches a sender on the same machine.
            if (anyAddress) targets.Add(IPAddress.Loopback);
            return targets;
        }

        void JoinMulticast()
        {
            if (!Options.MulticastListen) return;

            IPAddress group = IPAddress.Parse(MulticastGroup);
            string listen = Options.ListenIPAddress;
            bool anyAddress = string.IsNullOrEmpty(listen) || listen == "0.0.0.0";
            bool joinedAny = _joinedAddresses.Count > 0;

            if (!anyAddress)
            {
                joinedAny |= TryJoin(group, IPAddress.Parse(listen));
            }
            else
            {
                foreach (CLXNetwork.LocalAddress local in CLXNetwork.GetLocalAddresses())
                {
                    joinedAny |= TryJoin(group, local.Address);
                }
                if (!joinedAny) joinedAny = TryJoin(group, IPAddress.Any);
            }

            if (!joinedAny) _lastError = "Couldn't join multicast group " + MulticastGroup + ".";
        }

        bool TryJoin(IPAddress group, IPAddress local)
        {
            string key = local.ToString();
            if (_joinedAddresses.Contains(key)) return true;
            try
            {
                _socket.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.AddMembership,
                    new MulticastOption(group, local));
                _joinedAddresses.Add(key);
                return true;
            }
            catch (SocketException)
            {
                return false; // Not every interface can do multicast.
            }
        }

        void ReceiveLoop()
        {
            var buffer = new byte[65536];
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            Socket socket = _socket;
            while (_running)
            {
                try
                {
                    if (socket.Poll(PollMicroseconds, SelectMode.SelectRead))
                    {
                        int length = socket.ReceiveFrom(buffer, ref remote);
                        ProcessDatagram(buffer, length, ((IPEndPoint)remote).Address.ToString());
                    }
                    Tick();
                }
                catch (SocketException e)
                {
                    // A UDP socket reports transient errors here (Windows raises
                    // WSAECONNRESET after an ICMP unreachable), so keep going.
                    _lastError = e.Message;
                    Thread.Sleep(1);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Interlocked.Increment(ref _packetsMalformed);
                    _lastError = e.Message;
                }
            }
        }

        void Tick()
        {
            long now = _clock.ElapsedMilliseconds;
            if (now >= _nextAnnounce)
            {
                double interval = Options.AnnounceIntervalSeconds;
                _nextAnnounce = interval > 0 ? now + (long)(interval * 1000) : long.MaxValue;
                JoinMulticast(); // Picks up interfaces that came up since the last time.
                Announce();
            }

            if (!Options.AutoRequestWaveforms) return;

            // A lost fragment loses the whole waveform, so keep asking for a while.
            lock (_stateLock)
            {
                for (int deck = 0; deck < DeckSlots; deck++)
                {
                    DeckSlot slot = _decks[deck];
                    if (slot == null || !NeedsWaveform(slot) || now < slot.NextWaveformRequest) continue;
                    slot.WaveformRequests++;
                    slot.NextWaveformRequest = now + WaveformRequestIntervalMilliseconds;
                    SendWaveformRequest(deck, slot.Source);
                }
            }
        }

        bool NeedsWaveform(DeckSlot slot)
        {
            if (slot.TrackHash.Length == 0 || slot.WaveformRequests >= MaxWaveformRequests) return false;
            if (slot.Source.Length == 0) return false;
            return !_tracks.TryGetValue(slot.TrackHash, out Track track) || track.Waveform == null;
        }

        /// <summary>
        /// Handles one datagram as if it came from the network. The receive thread
        /// calls this, and it's public so a stream can be replayed without sockets.
        /// Not safe to call from two threads at once.
        /// </summary>
        public void ProcessDatagram(byte[] buffer, int length, string source)
        {
            if (length < 1) return;
            byte type = buffer[0];
            bool known = type == CLXPacketType.Control
                || type == CLXPacketType.Deck
                || type == CLXPacketType.Meta
                || type == CLXPacketType.Event
                || type == CLXPacketType.Binary;
            if (!known) return; // Includes waveform requests from other receivers.

            long now = _clock.ElapsedMilliseconds;
            Interlocked.Increment(ref _packetsReceived);
            Interlocked.Exchange(ref _lastPacketAt, now);
            NoteSource(source);

            CLXSourceSelection selection = Options.SourceSelection;
            if (selection != null && !selection.Allows(type, source)) return;

            bool decoded;
            switch (type)
            {
                case CLXPacketType.Deck: decoded = HandleDeck(buffer, length, source, now); break;
                case CLXPacketType.Meta: decoded = HandleMeta(buffer, length, source, now); break;
                case CLXPacketType.Control: decoded = HandleControl(buffer, length); break;
                case CLXPacketType.Event: decoded = HandleEvent(buffer, length, source, now); break;
                default: decoded = HandleBinary(buffer, length, source, now); break;
            }
            if (!decoded) Interlocked.Increment(ref _packetsMalformed);
        }

        void NoteSource(string source)
        {
            lock (_stateLock)
            {
                if (!_sources.Add(source)) return;
                _sourceList = new string[_sources.Count];
                _sources.CopyTo(_sourceList);
                _sourcesFlag = true;
            }
        }

        bool HandleDeck(byte[] buffer, int length, string source, long now)
        {
            if (!CLXDecoder.TryDecodeDeck(buffer, 1, length - 1, out DeckPacket packet)) return false;

            if (_filter.CheckDeck(source, packet, buffer, 1, length - 1, now) != CLXPacketVerdict.Accept)
            {
                Interlocked.Increment(ref _packetsDropped);
                return true;
            }

            lock (_stateLock)
            {
                DeckSlot slot = Slot(packet.Deck);
                if (slot != null)
                {
                    slot.Deck = packet;
                    slot.HasDeck = true;
                    slot.Source = source;
                }
            }
            Enqueue(new QueuedEvent { Kind = EventKind.Deck, Deck = packet });
            return true;
        }

        bool HandleMeta(byte[] buffer, int length, string source, long now)
        {
            if (!CLXDecoder.TryDecodeMeta(buffer, 1, length - 1, out MetaPacket packet)) return false;

            bool changed = false;
            string hash = string.Empty;
            lock (_stateLock)
            {
                DeckSlot slot = Slot(packet.Deck);
                if (slot != null)
                {
                    changed = !slot.HasMeta
                        || slot.Meta.Title != packet.Title
                        || slot.Meta.FilePath != packet.FilePath;
                    slot.Meta = packet;
                    slot.HasMeta = true;
                    slot.Source = source;
                    if (changed)
                    {
                        hash = packet.TrackHash;
                        slot.TrackHash = hash;
                        slot.WaveformRequests = 0;
                        slot.NextWaveformRequest = now;
                    }
                }
            }

            Enqueue(new QueuedEvent { Kind = EventKind.Meta, Payload = packet });
            if (changed)
            {
                Enqueue(new QueuedEvent { Kind = EventKind.TrackChanged, Payload = packet });
                if (hash.Length > 0) LoadTrack(hash);
            }
            return true;
        }

        bool HandleControl(byte[] buffer, int length)
        {
            if (!CLXDecoder.TryDecodeControl(buffer, 1, length - 1, out ControlPacket packet)) return false;
            lock (_stateLock)
            {
                _control = packet;
                _hasControl = true;
            }
            Enqueue(new QueuedEvent { Kind = EventKind.Control, Payload = packet });
            return true;
        }

        bool HandleEvent(byte[] buffer, int length, string source, long now)
        {
            if (!CLXDecoder.TryDecodeEvent(buffer, 1, length - 1, out EventPacket packet)) return false;
            if (_filter.IsDuplicateEvent(source, buffer, 1, length - 1, now))
            {
                Interlocked.Increment(ref _packetsDropped);
                return true;
            }
            Enqueue(new QueuedEvent { Kind = EventKind.Event, Payload = packet });
            return true;
        }

        bool HandleBinary(byte[] buffer, int length, string source, long now)
        {
            if (!CLXDecoder.TryDecodeBinary(buffer, 1, length - 1, out BinaryFragment fragment)) return false;

            switch (fragment.Type)
            {
                case "leader":
                    if (!CLXDecoder.TryDecodeLeader(fragment.Data, 0, fragment.Data.Length, out LeaderPacket leader))
                    {
                        return false;
                    }
                    lock (_stateLock)
                    {
                        _leader = leader;
                        _hasLeader = true;
                    }
                    Enqueue(new QueuedEvent { Kind = EventKind.Leader, Payload = leader });
                    return true;

                case "waveform":
                case "beatgrid":
                case "cues":
                    if (!_reassembler.Add(source, fragment, now, out byte[] payload)) return true;
                    if (fragment.Type == "waveform") return HandleWaveform(payload);
                    return fragment.Type == "beatgrid" ? HandleBeatgrid(payload) : HandleCues(payload);

                default:
                    return true; // chat, midi and the like: not for us.
            }
        }

        bool HandleWaveform(byte[] payload)
        {
            if (!CLXDecoder.TryDecodeWaveform(payload, out WaveformPayload decoded)) return false;

            // A later, lower-resolution or duplicate transmission mustn't clobber a
            // good waveform unless the sender says it replaces it. The listener is
            // told either way, since it may be waiting for this track.
            CLXWaveform waveform = null;
            if (!decoded.Replace)
            {
                lock (_stateLock)
                {
                    if (_tracks.TryGetValue(decoded.Hash, out Track known)) waveform = known.Waveform;
                }
                if (waveform == null) waveform = ReadCachedWaveform(decoded.Hash);
            }
            if (waveform == null)
            {
                if (!CLXWaveform.TryParse(decoded.Data, out waveform)) return false;
                waveform.Hash = decoded.Hash;
                waveform.FilePath = WriteCache(decoded.Hash, ".rwf", decoded.Data);
            }

            lock (_stateLock)
            {
                GetTrack(decoded.Hash).Waveform = waveform;
            }
            Enqueue(new QueuedEvent { Kind = EventKind.Waveform, Payload = waveform });
            return true;
        }

        bool HandleBeatgrid(byte[] payload)
        {
            if (!CLXDecoder.TryDecodeBeatgrid(payload, out BeatgridPayload beatgrid)) return false;
            WriteCache(beatgrid.Hash, ".bg", payload);
            lock (_stateLock)
            {
                GetTrack(beatgrid.Hash).Beatgrid = beatgrid;
            }
            Enqueue(new QueuedEvent { Kind = EventKind.Beatgrid, Payload = beatgrid });
            return true;
        }

        bool HandleCues(byte[] payload)
        {
            if (!CLXDecoder.TryDecodeCues(payload, out CuesPayload cues)) return false;
            WriteCache(cues.Hash, ".cues", payload);
            lock (_stateLock)
            {
                GetTrack(cues.Hash).Cues = cues;
            }
            Enqueue(new QueuedEvent { Kind = EventKind.Cues, Payload = cues });
            return true;
        }

        // A track was loaded: get what's known about it from memory or the disk cache
        // and tell listeners, the same as if it had just come from the network.
        void LoadTrack(string hash)
        {
            CLXWaveform waveform;
            BeatgridPayload beatgrid;
            CuesPayload cues;
            lock (_stateLock)
            {
                Track track = GetTrack(hash);
                waveform = track.Waveform;
                beatgrid = track.Beatgrid;
                cues = track.Cues;
            }

            if (waveform == null) waveform = ReadCachedWaveform(hash);
            if (beatgrid == null
                && ReadCache(hash, ".bg") is byte[] bg
                && CLXDecoder.TryDecodeBeatgrid(bg, out BeatgridPayload cachedGrid))
            {
                beatgrid = cachedGrid;
            }
            if (cues == null
                && ReadCache(hash, ".cues") is byte[] cueBytes
                && CLXDecoder.TryDecodeCues(cueBytes, out CuesPayload cachedCues))
            {
                cues = cachedCues;
            }

            lock (_stateLock)
            {
                Track track = GetTrack(hash);
                if (track.Waveform == null) track.Waveform = waveform;
                if (track.Beatgrid == null) track.Beatgrid = beatgrid;
                if (track.Cues == null) track.Cues = cues;
            }

            if (waveform != null) Enqueue(new QueuedEvent { Kind = EventKind.Waveform, Payload = waveform });
            if (beatgrid != null) Enqueue(new QueuedEvent { Kind = EventKind.Beatgrid, Payload = beatgrid });
            if (cues != null) Enqueue(new QueuedEvent { Kind = EventKind.Cues, Payload = cues });
        }

        CLXWaveform ReadCachedWaveform(string hash)
        {
            byte[] data = ReadCache(hash, ".rwf");
            if (data == null || !CLXWaveform.TryParse(data, out CLXWaveform waveform)) return null;
            waveform.Hash = hash;
            waveform.FilePath = CachePath(hash, ".rwf");
            return waveform;
        }

        string CachePath(string hash, string extension)
        {
            string directory = Options.CacheDirectory;
            return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, hash + extension);
        }

        byte[] ReadCache(string hash, string extension)
        {
            string path = CachePath(hash, extension);
            if (path == null || !File.Exists(path)) return null;
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _lastError = e.Message;
                return null;
            }
        }

        // Writes through a temporary file so a reader never sees half a file.
        // Returns the path, or an empty string if nothing was written.
        string WriteCache(string hash, string extension, byte[] data)
        {
            string path = CachePath(hash, extension);
            if (path == null) return string.Empty;
            try
            {
                Directory.CreateDirectory(Options.CacheDirectory);
                string temporary = path + ".tmp";
                File.WriteAllBytes(temporary, data);
                File.Delete(path);
                File.Move(temporary, path);
                return path;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _lastError = e.Message;
                return string.Empty;
            }
        }

        // Call inside _stateLock.
        Track GetTrack(string hash)
        {
            if (!_tracks.TryGetValue(hash, out Track track))
            {
                if (_tracks.Count >= MaxTracksInMemory) EvictOldestTrack();
                track = new Track();
                _tracks[hash] = track;
            }
            track.LastUsed = ++_trackUseCounter;
            return track;
        }

        // Call inside _stateLock.
        void EvictOldestTrack()
        {
            string oldest = null;
            long lastUsed = long.MaxValue;
            foreach (KeyValuePair<string, Track> pair in _tracks)
            {
                if (pair.Value.LastUsed >= lastUsed || IsLoaded(pair.Key)) continue;
                lastUsed = pair.Value.LastUsed;
                oldest = pair.Key;
            }
            if (oldest != null) _tracks.Remove(oldest);
        }

        // Call inside _stateLock.
        bool IsLoaded(string hash)
        {
            foreach (DeckSlot slot in _decks)
            {
                if (slot != null && slot.TrackHash == hash) return true;
            }
            return false;
        }

        // Call inside _stateLock.
        DeckSlot Slot(int deck)
        {
            if (deck < 0 || deck >= DeckSlots) return null;
            return _decks[deck] ?? (_decks[deck] = new DeckSlot());
        }

        // Call inside _stateLock.
        DeckSlot SlotOrNull(int deck) => deck < 0 || deck >= DeckSlots ? null : _decks[deck];

        void Enqueue(QueuedEvent item)
        {
            lock (_queueLock)
            {
                if (_incoming.Count >= MaxQueuedEvents) _incoming.Dequeue();
                _incoming.Enqueue(item);
            }
        }

        /// <summary>
        /// Raises the events for everything received since the last call, on the
        /// calling thread. Returns how many were raised.
        /// </summary>
        public int Pump()
        {
            lock (_queueLock)
            {
                Queue<QueuedEvent> swap = _dispatching;
                _dispatching = _incoming;
                _incoming = swap;
            }

            int count = 0;
            while (_dispatching.Count > 0)
            {
                QueuedEvent item = _dispatching.Dequeue();
                count++;
                try
                {
                    Raise(item);
                }
                catch (Exception e) when (HandlerError != null)
                {
                    HandlerError(e);
                }
            }
            return count;
        }

        void Raise(in QueuedEvent item)
        {
            switch (item.Kind)
            {
                case EventKind.Control: ControlReceived?.Invoke((ControlPacket)item.Payload); break;
                case EventKind.Deck: DeckReceived?.Invoke(item.Deck); break;
                case EventKind.Meta: MetaReceived?.Invoke((MetaPacket)item.Payload); break;
                case EventKind.Event: EventReceived?.Invoke((EventPacket)item.Payload); break;
                case EventKind.Leader: LeaderReceived?.Invoke((LeaderPacket)item.Payload); break;
                case EventKind.TrackChanged: TrackChanged?.Invoke((MetaPacket)item.Payload); break;
                case EventKind.Waveform: WaveformComplete?.Invoke((CLXWaveform)item.Payload); break;
                case EventKind.Beatgrid: BeatgridComplete?.Invoke((BeatgridPayload)item.Payload); break;
                case EventKind.Cues: CuesComplete?.Invoke((CuesPayload)item.Payload); break;
            }
        }

        /// <summary>Latest state of a deck. False until one of its packets has arrived.</summary>
        public bool TryGetDeck(int deck, out DeckPacket packet)
        {
            lock (_stateLock)
            {
                DeckSlot slot = SlotOrNull(deck);
                packet = slot != null ? slot.Deck : default;
                return slot != null && slot.HasDeck;
            }
        }

        /// <summary>Latest metadata of a deck. False until one of its packets has arrived.</summary>
        public bool TryGetMeta(int deck, out MetaPacket packet)
        {
            lock (_stateLock)
            {
                DeckSlot slot = SlotOrNull(deck);
                packet = slot != null ? slot.Meta : default;
                return slot != null && slot.HasMeta;
            }
        }

        public bool TryGetControl(out ControlPacket packet)
        {
            lock (_stateLock)
            {
                packet = _control;
                return _hasControl;
            }
        }

        public bool TryGetLeader(out LeaderPacket packet)
        {
            lock (_stateLock)
            {
                packet = _leader;
                return _hasLeader;
            }
        }

        /// <summary>The waveform of the track on a deck, or null if it hasn't arrived.</summary>
        public CLXWaveform GetWaveform(int deck) => GetLoadedTrack(deck)?.Waveform;

        /// <summary>The beat grid of the track on a deck, or null if it hasn't arrived.</summary>
        public BeatgridPayload GetBeatgrid(int deck) => GetLoadedTrack(deck)?.Beatgrid;

        /// <summary>The cues of the track on a deck, or null if they haven't arrived.</summary>
        public CuesPayload GetCues(int deck) => GetLoadedTrack(deck)?.Cues;

        Track GetLoadedTrack(int deck)
        {
            lock (_stateLock)
            {
                DeckSlot slot = SlotOrNull(deck);
                if (slot == null || slot.TrackHash.Length == 0) return null;
                _tracks.TryGetValue(slot.TrackHash, out Track track);
                return track;
            }
        }

        /// <summary>The address of every sender heard so far.</summary>
        public string[] DiscoveredSources
        {
            get
            {
                lock (_stateLock) return _sourceList;
            }
        }

        /// <summary>True if <see cref="DiscoveredSources"/> changed since <see cref="ResetSourcesFlag"/>.</summary>
        public bool SourcesFlag
        {
            get
            {
                lock (_stateLock) return _sourcesFlag;
            }
        }

        public void ResetSourcesFlag()
        {
            lock (_stateLock) _sourcesFlag = false;
        }
    }
}
