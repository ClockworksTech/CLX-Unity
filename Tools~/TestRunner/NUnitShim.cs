// The small part of NUnit the tests use, so they run outside Unity with no
// packages. In Unity's Test Runner they use the real NUnit instead.
using System;
using System.Collections;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class TestFixtureAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    public sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void Fail(string message) => throw new AssertionException(message);

        public static void IsTrue(bool condition, string message = null)
        {
            if (!condition) Fail(message ?? "Expected true.");
        }

        public static void IsFalse(bool condition, string message = null)
        {
            if (condition) Fail(message ?? "Expected false.");
        }

        public static void IsNull(object value)
        {
            if (value != null) Fail($"Expected null, got {value}.");
        }

        public static void IsNotNull(object value)
        {
            if (value == null) Fail("Expected a value, got null.");
        }

        public static void AreEqual(double expected, double actual, double delta)
        {
            if (Math.Abs(expected - actual) > delta) Fail($"Expected {expected}, got {actual}.");
        }

        public static void AreEqual(object expected, object actual)
        {
            if (expected is IEnumerable a && actual is IEnumerable b && !(expected is string))
            {
                IEnumerator x = a.GetEnumerator();
                IEnumerator y = b.GetEnumerator();
                int index = 0;
                while (true)
                {
                    bool more = x.MoveNext();
                    if (more != y.MoveNext()) Fail("Sequences differ in length.");
                    if (!more) return;
                    if (!Equals(x.Current, y.Current)) Fail($"Sequences differ at {index}.");
                    index++;
                }
            }
            if (!Equals(expected, actual)) Fail($"Expected {expected}, got {actual}.");
        }

        public static void AreNotEqual(object expected, object actual)
        {
            if (Equals(expected, actual)) Fail($"Expected anything but {expected}.");
        }
    }
}
