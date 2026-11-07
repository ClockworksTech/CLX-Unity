using System;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

namespace Clockworks.CLX
{
    [Serializable] public class CLXControlEvent : UnityEvent<ControlPacket> { }
    [Serializable] public class CLXDeckEvent : UnityEvent<DeckPacket> { }
    [Serializable] public class CLXMetaEvent : UnityEvent<MetaPacket> { }
    [Serializable] public class CLXEventEvent : UnityEvent<EventPacket> { }
    [Serializable] public class CLXLeaderEvent : UnityEvent<LeaderPacket> { }
    [Serializable] public class CLXWaveformEvent : UnityEvent<CLXWaveform> { }
    [Serializable] public class CLXBeatgridEvent : UnityEvent<BeatgridPayload> { }
    [Serializable] public class CLXCuesEvent : UnityEvent<CuesPayload> { }

    /// <summary>
    /// The same events as <see cref="CLXReceiver"/>'s C# events, for hooking up
    /// in the Inspector without writing a script.
    /// </summary>
    [Serializable]
    public class CLXReceiverEvents
    {
        public CLXControlEvent Control = new CLXControlEvent();
        public CLXDeckEvent Deck = new CLXDeckEvent();
        public CLXMetaEvent Meta = new CLXMetaEvent();
        public CLXEventEvent Event = new CLXEventEvent();
        public CLXLeaderEvent Leader = new CLXLeaderEvent();
        public CLXMetaEvent TrackChanged = new CLXMetaEvent();
        public CLXWaveformEvent WaveformComplete = new CLXWaveformEvent();
        public CLXBeatgridEvent BeatgridComplete = new CLXBeatgridEvent();
        public CLXCuesEvent CuesComplete = new CLXCuesEvent();
    }

    /// <summary>
    /// Joins a CLX stream and exposes it to a scene: the Unity counterpart of
    /// CLX-UE's CLXSubsystem. Add one to a GameObject, then bind its events or read
    /// its state from any script.
    ///
    /// Events fire on the main thread during this component's Update, which runs
    /// before default scripts' Update, so what you read there is the latest.
    /// </summary>
    [AddComponentMenu("Clockworks/CLX Receiver")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class CLXReceiver : MonoBehaviour
    {
        [Header("Network")]
        [Tooltip("Local address to listen on, or 0.0.0.0 for all of them.")]
        public string ListenIPAddress = "0.0.0.0";

        [Tooltip("UDP port senders stream to. CLX uses 3650.")]
        public int ListenPort = CLXClient.DefaultPort;

        [Tooltip("Also listen to the CLX multicast group, 239.0.0.1. Unicast keeps working.")]
        public bool MulticastListen = true;

        [Tooltip("Accept each kind of packet only from this sender's address. Blank accepts any sender.")]
        public CLXSourceSelection SourceSelection = new CLXSourceSelection();

        [Header("Behaviour")]
        [Tooltip("Start listening when this component is enabled.")]
        public bool AutoStart = true;

        [Tooltip("Seconds between join packets, which also finds senders that start later. 0 sends just one.")]
        [Min(0)]
        public float AnnounceInterval = 5;

        [Tooltip("Ask for a track's waveform when it loads. Without this a waveform arrives only when the sender first generates it.")]
        public bool AutoRequestWaveforms = true;

        [Tooltip("Keep received waveforms, beat grids and cues in Application.persistentDataPath/CLX/cache.")]
        public bool CacheToDisk = true;

        [Tooltip("Keep this object, and the connection, when a new scene loads.")]
        public bool PersistAcrossScenes;

        [Header("Events")]
        public CLXReceiverEvents Events = new CLXReceiverEvents();

        CLXClient _client;

        /// <summary>The most recently enabled receiver, or null. Handy when a scene has just one.</summary>
        public static CLXReceiver Instance { get; private set; }

        public event Action<ControlPacket> OnControlMessageReceived;
        public event Action<DeckPacket> OnDeckMessageReceived;

        /// <summary>Every Meta packet. Senders repeat these, so see <see cref="OnTrackChanged"/>.</summary>
        public event Action<MetaPacket> OnMetaMessageReceived;

        public event Action<EventPacket> OnEventMessageReceived;
        public event Action<LeaderPacket> OnLeaderMessageReceived;

        /// <summary>Fires once when a deck's title or file changes.</summary>
        public event Action<MetaPacket> OnTrackChanged;

        public event Action<CLXWaveform> OnWaveformComplete;
        public event Action<BeatgridPayload> OnBeatgridComplete;
        public event Action<CuesPayload> OnCuesComplete;

        /// <summary>The protocol client underneath, for anything this component doesn't wrap.</summary>
        public CLXClient Client
        {
            get
            {
                if (_client == null) _client = CreateClient();
                return _client;
            }
        }

        public bool IsListening => _client != null && _client.IsListening;

        /// <summary>The address of every sender heard so far.</summary>
        public string[] DiscoveredSources => Client.DiscoveredSources;

        /// <summary>True if <see cref="DiscoveredSources"/> changed since <see cref="ResetSourcesFlag"/>.</summary>
        public bool SourcesFlag => Client.SourcesFlag;

        public void ResetSourcesFlag() => Client.ResetSourcesFlag();

        /// <summary>Starts listening. False if it couldn't, and the reason is logged.</summary>
        public bool StartListening()
        {
            if (IsListening) return false;

            CLXClientOptions options = Client.Options;
            options.ListenIPAddress = ListenIPAddress;
            options.ListenPort = ListenPort;
            options.MulticastListen = MulticastListen;
            options.SourceSelection = SourceSelection;
            options.AnnounceIntervalSeconds = AnnounceInterval;
            options.AutoRequestWaveforms = AutoRequestWaveforms;
            options.CacheDirectory = CacheToDisk
                ? Path.Combine(Application.persistentDataPath, "CLX", "cache")
                : null;

            bool started = Client.Start();
            if (!started)
            {
                Debug.LogError($"[CLX] Couldn't listen on {ListenIPAddress}:{ListenPort}: {Client.LastError}", this);
            }
            return started;
        }

        public void StopListening() => _client?.Stop();

        /// <summary>Asks senders to resend their state. Sent once on start and then every <see cref="AnnounceInterval"/> seconds.</summary>
        public void Announce() => _client?.Announce();

        /// <summary>
        /// Asks a deck's sender for its waveform, which then arrives through
        /// <see cref="OnWaveformComplete"/>. False if no sender is known for that deck.
        /// </summary>
        public bool RequestWaveform(int deck) => _client != null && _client.RequestWaveform(deck);

        /// <summary>Latest state of a deck (1 to 6). False until one of its packets has arrived.</summary>
        public bool TryGetDeck(int deck, out DeckPacket packet) => Client.TryGetDeck(deck, out packet);

        /// <summary>Latest metadata of a deck (1 to 6). False until one of its packets has arrived.</summary>
        public bool TryGetMeta(int deck, out MetaPacket packet) => Client.TryGetMeta(deck, out packet);

        /// <summary>Latest mixer state. False until a Control packet has arrived.</summary>
        public bool TryGetControl(out ControlPacket packet) => Client.TryGetControl(out packet);

        /// <summary>Latest clock leader state. False unless a sender sends it.</summary>
        public bool TryGetLeader(out LeaderPacket packet) => Client.TryGetLeader(out packet);

        /// <summary>The waveform of the track on a deck, or null if it hasn't arrived.</summary>
        public CLXWaveform GetWaveform(int deck) => Client.GetWaveform(deck);

        /// <summary>The beat grid of the track on a deck, or null if it hasn't arrived.</summary>
        public BeatgridPayload GetBeatgrid(int deck) => Client.GetBeatgrid(deck);

        /// <summary>The cues of the track on a deck, or null if they haven't arrived.</summary>
        public CuesPayload GetCues(int deck) => Client.GetCues(deck);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            if (PersistAcrossScenes)
            {
                DontDestroyOnLoad(transform.root.gameObject);
            }
        }

        void OnEnable()
        {
            Instance = this;
            if (AutoStart) StartListening();
        }

        void OnDisable()
        {
            StopListening();
            if (Instance == this) Instance = null;
        }

        void OnDestroy()
        {
            _client?.Dispose();
            _client = null;
        }

        void Update()
        {
            _client?.Pump();
        }

        CLXClient CreateClient()
        {
            var client = new CLXClient();
            client.HandlerError += Debug.LogException;
            client.ControlReceived += packet =>
            {
                OnControlMessageReceived?.Invoke(packet);
                Events.Control.Invoke(packet);
            };
            client.DeckReceived += packet =>
            {
                OnDeckMessageReceived?.Invoke(packet);
                Events.Deck.Invoke(packet);
            };
            client.MetaReceived += packet =>
            {
                OnMetaMessageReceived?.Invoke(packet);
                Events.Meta.Invoke(packet);
            };
            client.EventReceived += packet =>
            {
                OnEventMessageReceived?.Invoke(packet);
                Events.Event.Invoke(packet);
            };
            client.LeaderReceived += packet =>
            {
                OnLeaderMessageReceived?.Invoke(packet);
                Events.Leader.Invoke(packet);
            };
            client.TrackChanged += packet =>
            {
                OnTrackChanged?.Invoke(packet);
                Events.TrackChanged.Invoke(packet);
            };
            client.WaveformComplete += waveform =>
            {
                OnWaveformComplete?.Invoke(waveform);
                Events.WaveformComplete.Invoke(waveform);
            };
            client.BeatgridComplete += beatgrid =>
            {
                OnBeatgridComplete?.Invoke(beatgrid);
                Events.BeatgridComplete.Invoke(beatgrid);
            };
            client.CuesComplete += cues =>
            {
                OnCuesComplete?.Invoke(cues);
                Events.CuesComplete.Invoke(cues);
            };
            return client;
        }
    }
}
