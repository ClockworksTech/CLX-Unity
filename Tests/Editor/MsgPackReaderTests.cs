using System.Collections.Generic;
using NUnit.Framework;

namespace Clockworks.CLX.Tests
{
    [TestFixture]
    public class MsgPackReaderTests
    {
        static MsgPackReader Reader(params byte[] bytes) => new MsgPackReader(bytes, 0, bytes.Length);

        [Test]
        public void ReadsEveryIntegerWidth()
        {
            Assert.AreEqual(5L, Reader(0x05).ReadInt64());
            Assert.AreEqual(-1L, Reader(0xff).ReadInt64());
            Assert.AreEqual(200L, Reader(0xcc, 0xc8).ReadInt64());
            Assert.AreEqual(0x1234L, Reader(0xcd, 0x12, 0x34).ReadInt64());
            Assert.AreEqual(0x12345678L, Reader(0xce, 0x12, 0x34, 0x56, 0x78).ReadInt64());
            Assert.AreEqual(0x0102030405060708L, Reader(0xcf, 1, 2, 3, 4, 5, 6, 7, 8).ReadInt64());
            Assert.AreEqual(-2L, Reader(0xd0, 0xfe).ReadInt64());
            Assert.AreEqual(-2L, Reader(0xd1, 0xff, 0xfe).ReadInt64());
            Assert.AreEqual(-2L, Reader(0xd2, 0xff, 0xff, 0xff, 0xfe).ReadInt64());
            Assert.AreEqual(-2L, Reader(0xd3, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xfe).ReadInt64());
        }

        [Test]
        public void UInt64KeepsItsHighBit()
        {
            Assert.AreEqual(ulong.MaxValue, Reader(0xcf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff).ReadUInt64());
        }

        [Test]
        public void NumbersConvertBetweenIntAndFloat()
        {
            Assert.AreEqual(1.5, Reader(0xca, 0x3f, 0xc0, 0x00, 0x00).ReadDouble());
            Assert.AreEqual(1.5, Reader(0xcb, 0x3f, 0xf8, 0, 0, 0, 0, 0, 0).ReadDouble());
            Assert.AreEqual(128.0, Reader(0xcc, 0x80).ReadDouble());
            Assert.AreEqual(1L, Reader(0xcb, 0x3f, 0xf8, 0, 0, 0, 0, 0, 0).ReadInt64());
        }

        [Test]
        public void WrongTypeReadsAsDefaultAndKeepsTheStreamAligned()
        {
            // ["x", 7]
            MsgPackReader reader = Reader(0x92, 0xa1, 0x78, 0x07);
            Assert.IsTrue(reader.TryReadArrayHeader(out int count));
            Assert.AreEqual(2, count);
            Assert.AreEqual(0.0, reader.ReadDouble());
            Assert.AreEqual(7L, reader.ReadInt64());
            Assert.IsFalse(reader.Failed);
        }

        [Test]
        public void ReadsBytesFromBinStrAndArray()
        {
            Assert.AreEqual(new byte[] { 1, 2 }, Reader(0xc4, 2, 1, 2).ReadBytes());
            Assert.AreEqual(new byte[] { 0x61, 0x62 }, Reader(0xa2, 0x61, 0x62).ReadBytes());
            Assert.AreEqual(new byte[] { 1, 200 }, Reader(0x92, 0x01, 0xcc, 0xc8).ReadBytes());
            Assert.AreEqual(new byte[0], Reader(0xc0).ReadBytes());
        }

        [Test]
        public void ReadsStringForms()
        {
            Assert.AreEqual("ab", Reader(0xa2, 0x61, 0x62).ReadString());
            Assert.AreEqual("ab", Reader(0xd9, 2, 0x61, 0x62).ReadString());
            Assert.AreEqual("ab", Reader(0xda, 0, 2, 0x61, 0x62).ReadString());
            Assert.AreEqual("ab", Reader(0xc4, 2, 0x61, 0x62).ReadString());
            Assert.AreEqual(string.Empty, Reader(0xc0).ReadString());
        }

        [Test]
        public void SkipHandlesNestingAndExtensions()
        {
            // {"a": [1, {"b": nil}], "c": ext8} then 9
            MsgPackReader reader = Reader(
                0x82,
                0xa1, 0x61, 0x92, 0x01, 0x81, 0xa1, 0x62, 0xc0,
                0xa1, 0x63, 0xc7, 0x02, 0x05, 0xaa, 0xbb,
                0x09);
            reader.Skip();
            Assert.IsFalse(reader.Failed);
            Assert.AreEqual(9L, reader.ReadInt64());
        }

        [Test]
        public void ReadValueBuildsObjects()
        {
            // [1, 2.5, "x", true, nil, {"k": 3}]
            MsgPackReader reader = Reader(
                0x96, 0x01, 0xcb, 0x40, 0x04, 0, 0, 0, 0, 0, 0, 0xa1, 0x78, 0xc3, 0xc0, 0x81, 0xa1, 0x6b, 0x03);
            var value = (object[])reader.ReadValue();
            Assert.AreEqual(1L, value[0]);
            Assert.AreEqual(2.5, value[1]);
            Assert.AreEqual("x", value[2]);
            Assert.AreEqual(true, value[3]);
            Assert.IsNull(value[4]);
            Assert.AreEqual(3L, ((Dictionary<string, object>)value[5])["k"]);
        }

        [Test]
        public void TruncatedValuesFailInsteadOfThrowing()
        {
            byte[][] inputs =
            {
                new byte[] { 0xcb, 0x3f },
                new byte[] { 0xa5, 0x61 },
                new byte[] { 0xc4, 0x05, 0x01 },
                new byte[] { 0xdc, 0xff, 0xff },
                new byte[] { 0xdd, 0x7f, 0xff, 0xff, 0xff },
                new byte[] { 0xde, 0xff, 0xff },
                new byte[] { 0xc6, 0xff, 0xff, 0xff, 0xff },
                new byte[] { 0xc1 },
            };
            foreach (byte[] input in inputs)
            {
                var reader = new MsgPackReader(input, 0, input.Length);
                reader.Skip();
                Assert.IsTrue(reader.Failed);
            }
        }

        [Test]
        public void DeepNestingFailsInsteadOfOverflowingTheStack()
        {
            var input = new byte[100000];
            for (int i = 0; i < input.Length; i++) input[i] = 0x91; // [[[[...
            var reader = new MsgPackReader(input, 0, input.Length);
            reader.Skip();
            Assert.IsTrue(reader.Failed);
        }

        [Test]
        public void KeyMatchingIsExact()
        {
            MsgPackReader reader = Reader(0xa3, 0x42, 0x50, 0x4d);
            MsgPackKey key = reader.ReadKey();
            Assert.IsTrue(key.Is("BPM"));
            Assert.IsFalse(key.Is("BP"));
            Assert.IsFalse(key.Is("Bpm"));
        }
    }
}
