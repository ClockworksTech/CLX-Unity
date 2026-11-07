using UnityEditor;
using UnityEngine;

namespace Clockworks.CLX.Editor
{
    /// <summary>Adds a live view of the stream to the CLX Receiver's Inspector in Play mode.</summary>
    [CustomEditor(typeof(CLXReceiver))]
    public sealed class CLXReceiverEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!Application.isPlaying) return;

            var receiver = (CLXReceiver)target;
            CLXClient client = receiver.Client;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Status", Status(receiver));
            EditorGUILayout.LabelField("Sources", client.DiscoveredSources.Length == 0
                ? "None heard yet"
                : string.Join(", ", client.DiscoveredSources));
            EditorGUILayout.LabelField("Packets", $"{client.PacketsReceived} received, "
                + $"{client.PacketsDropped} dropped, {client.PacketsMalformed} malformed");
            if (!string.IsNullOrEmpty(client.LastError))
            {
                EditorGUILayout.HelpBox(client.LastError, MessageType.Warning);
            }

            for (int deck = 1; deck <= 6; deck++)
            {
                if (!client.TryGetDeck(deck, out DeckPacket packet)) continue;
                client.TryGetMeta(deck, out MetaPacket meta);
                EditorGUILayout.LabelField(
                    $"Deck {deck}",
                    $"{packet.Position:0.00}s  {packet.BPM:0.00} BPM  {meta.Title}");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Announce")) receiver.Announce();
                if (receiver.IsListening)
                {
                    if (GUILayout.Button("Stop")) receiver.StopListening();
                }
                else if (GUILayout.Button("Start"))
                {
                    receiver.StartListening();
                }
            }
        }

        static string Status(CLXReceiver receiver)
        {
            if (!receiver.IsListening) return "Stopped";
            double age = receiver.Client.SecondsSinceLastPacket;
            return double.IsInfinity(age) ? "Listening, no packets yet" : $"Listening, last packet {age:0.0}s ago";
        }

        [MenuItem("GameObject/Clockworks/CLX Receiver", false, 10)]
        static void CreateReceiver(MenuCommand command)
        {
            var gameObject = new GameObject("CLX Receiver", typeof(CLXReceiver));
            GameObjectUtility.SetParentAndAlign(gameObject, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create CLX Receiver");
            Selection.activeGameObject = gameObject;
        }
    }
}
