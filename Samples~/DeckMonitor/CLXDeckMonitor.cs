using System.Collections.Generic;
using UnityEngine;

namespace Clockworks.CLX.Samples
{
    /// <summary>
    /// Shows every deck of a CLX stream: track, time, beat and waveform with the
    /// playhead and cues, plus recent events. Open the DeckMonitor scene and press
    /// Play, or add this to any GameObject. It's drawn with IMGUI so it needs no
    /// other setup.
    /// </summary>
    [RequireComponent(typeof(CLXReceiver))]
    public sealed class CLXDeckMonitor : MonoBehaviour
    {
        const int MaxDecks = 6;
        const int MaxLogLines = 8;
        const float RowHeight = 96;
        const float Margin = 12;

        static readonly Color32 DefaultWaveformColor = new Color32(90, 200, 120, 255);

        readonly Dictionary<string, Texture2D> _waveforms = new Dictionary<string, Texture2D>();
        readonly Queue<string> _log = new Queue<string>();
        CLXReceiver _receiver;

        void Awake()
        {
            _receiver = GetComponent<CLXReceiver>();
        }

        void OnEnable()
        {
            _receiver.OnWaveformComplete += HandleWaveform;
            _receiver.OnEventMessageReceived += HandleEvent;
            _receiver.OnTrackChanged += HandleTrackChanged;
        }

        void OnDisable()
        {
            _receiver.OnWaveformComplete -= HandleWaveform;
            _receiver.OnEventMessageReceived -= HandleEvent;
            _receiver.OnTrackChanged -= HandleTrackChanged;
        }

        void OnDestroy()
        {
            foreach (Texture2D texture in _waveforms.Values) Destroy(texture);
        }

        void HandleWaveform(CLXWaveform waveform)
        {
            _waveforms.TryGetValue(waveform.Hash, out Texture2D existing);
            _waveforms[waveform.Hash] = CLXWaveformTexture.Render(waveform, 1024, 64, DefaultWaveformColor, existing);
        }

        void HandleEvent(EventPacket packet)
        {
            string deck = packet.Deck == 0 ? string.Empty : $" deck {packet.Deck}";
            Log($"{packet.EventName}{deck} [{string.Join(", ", packet.Values)}]");
        }

        void HandleTrackChanged(MetaPacket meta)
        {
            Log($"Deck {meta.Deck} loaded {meta.Artist} - {meta.Title}");
        }

        void Log(string line)
        {
            _log.Enqueue(line);
            while (_log.Count > MaxLogLines) _log.Dequeue();
        }

        void OnGUI()
        {
            float width = Screen.width - Margin * 2;
            float y = Margin;

            GUI.Label(new Rect(Margin, y, width, 20), Summary());
            y += 24;

            for (int deck = 1; deck <= MaxDecks; deck++)
            {
                if (!_receiver.TryGetDeck(deck, out DeckPacket packet)) continue;
                DrawDeck(new Rect(Margin, y, width, RowHeight), deck, packet);
                y += RowHeight + 6;
            }

            foreach (string line in _log)
            {
                GUI.Label(new Rect(Margin, y, width, 20), line);
                y += 18;
            }
        }

        string Summary()
        {
            if (!_receiver.IsListening) return "Not listening. Is port 3650 free? See the Console.";
            string[] sources = _receiver.DiscoveredSources;
            if (sources.Length == 0) return "Listening. Waiting for a CLX sender...";
            return "Receiving from " + string.Join(", ", sources);
        }

        void DrawDeck(Rect area, int deck, in DeckPacket packet)
        {
            _receiver.TryGetMeta(deck, out MetaPacket meta);

            GUI.Box(area, GUIContent.none);
            string title = string.IsNullOrEmpty(meta.Title) ? "No track" : $"{meta.Artist} - {meta.Title}";
            GUI.Label(new Rect(area.x + 6, area.y + 2, area.width - 12, 20), $"Deck {deck}   {title}");
            GUI.Label(
                new Rect(area.x + 6, area.y + 20, area.width - 12, 20),
                $"{FormatTime(packet.Position)} / {FormatTime(packet.Length)}   "
                + $"{packet.BPM:0.00} BPM   Pitch {(packet.Pitch - 1) * 100:+0.00;-0.00}%   Beat {packet.Beat}");

            var strip = new Rect(area.x + 6, area.y + 42, area.width - 12, area.height - 48);
            CLXWaveform waveform = _receiver.GetWaveform(deck);
            if (waveform != null && _waveforms.TryGetValue(waveform.Hash, out Texture2D texture))
            {
                GUI.DrawTexture(strip, texture, ScaleMode.StretchToFill);
            }

            double length = packet.Length > 0 ? packet.Length : waveform?.Duration ?? 0;
            if (length <= 0) return;

            CuesPayload cues = _receiver.GetCues(deck);
            if (cues != null)
            {
                foreach (CLXCue cue in cues.Cues)
                {
                    DrawMarker(strip, cue.Time / length, cue.Hotcue ? Color.yellow : Color.cyan);
                }
            }
            DrawMarker(strip, packet.NormalizedPosition, Color.white);
        }

        static void DrawMarker(Rect strip, double position, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            float x = strip.x + (float)position * strip.width;
            GUI.DrawTexture(new Rect(x, strip.y, 1, strip.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static string FormatTime(double seconds)
        {
            int minutes = (int)(seconds / 60);
            return $"{minutes:00}:{seconds - minutes * 60:00.00}";
        }
    }
}
