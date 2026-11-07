// Just enough of the UnityEngine and UnityEditor API to type-check the package
// without Unity, written by hand from Unity's documentation. This proves the code
// is valid C# against these signatures. It doesn't prove the signatures are right.
#pragma warning disable CS0067, CS0649, CS1591
using System;

namespace UnityEngine
{
    public class Object
    {
        public static void Destroy(Object target) { }
        public static void DontDestroyOnLoad(Object target) { }
    }

    public class Component : Object
    {
        public Transform transform => null;
        public GameObject gameObject => null;
        public T GetComponent<T>() => default;
    }

    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }
    public class Transform : Component { public Transform root => null; }

    public sealed class GameObject : Object
    {
        public GameObject(string name, params Type[] components) { }
    }

    public enum LogType { Error }

    public static class Debug
    {
        public static void LogError(object message) { }
        public static void LogError(object message, Object context) { }
        public static void LogException(Exception exception) { }
        public static void LogException(Exception exception, Object context) { }
    }

    public static class Application
    {
        public static string persistentDataPath => null;
        public static bool isPlaying => false;
    }

    public static class Screen
    {
        public static int width => 0;
        public static int height => 0;
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public float x, y, width, height;
    }

    public struct Color
    {
        public static Color white => default;
        public static Color yellow => default;
        public static Color cyan => default;
    }

    public struct Color32
    {
        public Color32(byte r, byte g, byte b, byte a) { }
    }

    public static class Mathf
    {
        public static int Max(int a, int b) => 0;
        public static int Clamp(int value, int min, int max) => 0;
        public static int RoundToInt(float f) => 0;
    }

    public enum TextureFormat { RGBA32 }
    public enum FilterMode { Bilinear }
    public enum TextureWrapMode { Clamp }
    public enum ScaleMode { StretchToFill }

    public class Texture : Object
    {
        public virtual int width { get; set; }
        public virtual int height { get; set; }
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
    }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public static Texture2D whiteTexture => null;
        public void SetPixels32(Color32[] colors, int miplevel = 0) { }
        public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
    }

    public class GUIContent
    {
        public static GUIContent none => null;
    }

    public class GUIStyle { }
    public sealed class GUILayoutOption { }

    public static class GUI
    {
        public static Color color { get; set; }
        public static void Label(Rect position, string text) { }
        public static void Box(Rect position, GUIContent content) { }
        public static void DrawTexture(Rect position, Texture image) { }
        public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode) { }
    }

    public static class GUILayout
    {
        public static bool Button(string text, params GUILayoutOption[] options) => false;
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string componentMenu) { } }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireComponent : Attribute { public RequireComponent(Type requiredComponent) { } }

    public abstract class PropertyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class HeaderAttribute : PropertyAttribute { public HeaderAttribute(string header) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public class TooltipAttribute : PropertyAttribute { public TooltipAttribute(string tooltip) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : PropertyAttribute { public MinAttribute(float min) { } }

    public enum RuntimeInitializeLoadType { SubsystemRegistration }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
}

namespace UnityEngine.Events
{
    public abstract class UnityEventBase { }

    public class UnityEvent<T0> : UnityEventBase
    {
        public void AddListener(Action<T0> call) { }
        public void Invoke(T0 arg0) { }
    }
}

namespace UnityEditor
{
    using UnityEngine;

    public class Editor : UnityEngine.Object
    {
        public UnityEngine.Object target => null;
        public virtual void OnInspectorGUI() { }
        public virtual bool RequiresConstantRepaint() => false;
        public bool DrawDefaultInspector() => false;
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CustomEditor : Attribute { public CustomEditor(Type inspectedType) { } }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItem : Attribute { public MenuItem(string itemName, bool isValidateFunction, int priority) { } }

    public sealed class MenuCommand { public UnityEngine.Object context; }

    public enum MessageType { Warning }

    public static class EditorStyles { public static GUIStyle boldLabel => null; }

    public static class EditorGUILayout
    {
        public static void Space() { }
        public static void LabelField(string label, params GUILayoutOption[] options) { }
        public static void LabelField(string label, GUIStyle style, params GUILayoutOption[] options) { }
        public static void LabelField(string label, string label2, params GUILayoutOption[] options) { }
        public static void HelpBox(string message, MessageType type) { }

        public class HorizontalScope : IDisposable
        {
            public HorizontalScope(params GUILayoutOption[] options) { }
            public void Dispose() { }
        }
    }

    public static class GameObjectUtility
    {
        public static void SetParentAndAlign(GameObject child, GameObject parent) { }
    }

    public static class Undo
    {
        public static void RegisterCreatedObjectUndo(UnityEngine.Object objectToUndo, string name) { }
    }

    public static class Selection { public static GameObject activeGameObject { get; set; } }
}
