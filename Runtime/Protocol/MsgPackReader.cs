using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Clockworks.CLX
{
    /// <summary>The kind of the next value in a MessagePack stream.</summary>
    public enum MsgPackType
    {
        Invalid,
        Nil,
        Bool,
        Integer,
        Float,
        String,
        Binary,
        Array,
        Map,
        Extension,
    }

    /// <summary>A map key, read without allocating a string.</summary>
    public readonly struct MsgPackKey
    {
        readonly byte[] _data;
        readonly int _offset;
        readonly int _length;

        internal MsgPackKey(byte[] data, int offset, int length)
        {
            _data = data;
            _offset = offset;
            _length = length;
        }

        /// <summary>True when the key equals an ASCII string.</summary>
        public bool Is(string ascii)
        {
            if (ascii.Length != _length) return false;
            for (int i = 0; i < _length; i++)
            {
                if (_data[_offset + i] != ascii[i]) return false;
            }
            return true;
        }

        public override string ToString() =>
            _data == null ? string.Empty : Encoding.UTF8.GetString(_data, _offset, _length);
    }

    /// <summary>
    /// A forward-only MessagePack reader over a byte array. It never throws on
    /// bad input, since UDP traffic is untrusted: a truncated or malformed value
    /// sets <see cref="Failed"/> and later reads return defaults. A value of an
    /// unexpected type is skipped and reads as the default.
    ///
    /// This is a mutable struct: pass it by <c>ref</c>.
    /// </summary>
    public struct MsgPackReader
    {
        const int MaxDepth = 32;

        [StructLayout(LayoutKind.Explicit)]
        struct FloatBits
        {
            [FieldOffset(0)] public uint Bits;
            [FieldOffset(0)] public float Value;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct DoubleBits
        {
            [FieldOffset(0)] public ulong Bits;
            [FieldOffset(0)] public double Value;
        }

        readonly byte[] _data;
        readonly int _end;
        int _pos;
        bool _failed;

        public MsgPackReader(byte[] data, int offset, int count)
        {
            bool valid = data != null && offset >= 0 && count >= 0 && offset + count <= data.Length;
            _data = data;
            _pos = valid ? offset : 0;
            _end = valid ? offset + count : 0;
            _failed = !valid;
        }

        /// <summary>True once the input turned out truncated or malformed.</summary>
        public bool Failed => _failed;

        /// <summary>Offset of the next unread byte.</summary>
        public int Position => _pos;

        /// <summary>The kind of the next value, without consuming it.</summary>
        public MsgPackType PeekType()
        {
            if (_failed || _pos >= _end) return MsgPackType.Invalid;
            byte b = _data[_pos];
            if (b <= 0x7f || b >= 0xe0) return MsgPackType.Integer;
            if (b <= 0x8f) return MsgPackType.Map;
            if (b <= 0x9f) return MsgPackType.Array;
            if (b <= 0xbf) return MsgPackType.String;
            switch (b)
            {
                case 0xc0: return MsgPackType.Nil;
                case 0xc2:
                case 0xc3: return MsgPackType.Bool;
                case 0xc4:
                case 0xc5:
                case 0xc6: return MsgPackType.Binary;
                case 0xc7:
                case 0xc8:
                case 0xc9:
                case 0xd4:
                case 0xd5:
                case 0xd6:
                case 0xd7:
                case 0xd8: return MsgPackType.Extension;
                case 0xca:
                case 0xcb: return MsgPackType.Float;
                case 0xcc:
                case 0xcd:
                case 0xce:
                case 0xcf:
                case 0xd0:
                case 0xd1:
                case 0xd2:
                case 0xd3: return MsgPackType.Integer;
                case 0xd9:
                case 0xda:
                case 0xdb: return MsgPackType.String;
                case 0xdc:
                case 0xdd: return MsgPackType.Array;
                case 0xde:
                case 0xdf: return MsgPackType.Map;
                default: return MsgPackType.Invalid; // 0xc1 is never used
            }
        }

        /// <summary>Reads a map header. False if the next value isn't a map.</summary>
        public bool TryReadMapHeader(out int count)
        {
            count = 0;
            if (PeekType() != MsgPackType.Map) return false;
            byte b = _data[_pos++];
            if (b <= 0x8f) count = b & 0x0f;
            else if (!TryReadLength(b == 0xde ? 2 : 4, out count)) return false;
            // Every map entry takes at least two bytes.
            if (count > (_end - _pos) / 2) return Fail();
            return true;
        }

        /// <summary>Reads an array header. False if the next value isn't an array.</summary>
        public bool TryReadArrayHeader(out int count)
        {
            count = 0;
            if (PeekType() != MsgPackType.Array) return false;
            byte b = _data[_pos++];
            if (b <= 0x9f) count = b & 0x0f;
            else if (!TryReadLength(b == 0xdc ? 2 : 4, out count)) return false;
            if (count > _end - _pos) return Fail();
            return true;
        }

        /// <summary>
        /// Reads a map key. A key that isn't a string is consumed and comes back
        /// empty, so no name matches it and the caller skips its value.
        /// </summary>
        public MsgPackKey ReadKey()
        {
            if (TryReadStringSlice(out int offset, out int length))
            {
                return new MsgPackKey(_data, offset, length);
            }
            Skip();
            return default;
        }

        public double ReadDouble(double fallback = 0)
        {
            switch (PeekType())
            {
                case MsgPackType.Float:
                    byte b = _data[_pos++];
                    if (b == 0xca)
                    {
                        return TryReadBigEndian(4, out ulong f)
                            ? new FloatBits { Bits = (uint)f }.Value
                            : fallback;
                    }
                    return TryReadBigEndian(8, out ulong d)
                        ? new DoubleBits { Bits = d }.Value
                        : fallback;
                case MsgPackType.Integer:
                    if (!TryReadInteger(out long signed, out ulong unsigned, out bool isUnsigned)) return fallback;
                    return isUnsigned ? unsigned : (double)signed;
                default:
                    Skip();
                    return fallback;
            }
        }

        public float ReadSingle(float fallback = 0) => (float)ReadDouble(fallback);

        public long ReadInt64(long fallback = 0)
        {
            switch (PeekType())
            {
                case MsgPackType.Integer:
                    if (!TryReadInteger(out long signed, out ulong unsigned, out bool isUnsigned)) return fallback;
                    if (!isUnsigned) return signed;
                    return unsigned > long.MaxValue ? fallback : (long)unsigned;
                case MsgPackType.Float:
                    return (long)ReadDouble(fallback);
                default:
                    Skip();
                    return fallback;
            }
        }

        public int ReadInt32(int fallback = 0)
        {
            long value = ReadInt64(fallback);
            return value < int.MinValue || value > int.MaxValue ? fallback : (int)value;
        }

        public ulong ReadUInt64(ulong fallback = 0)
        {
            switch (PeekType())
            {
                case MsgPackType.Integer:
                    if (!TryReadInteger(out long signed, out ulong unsigned, out bool isUnsigned)) return fallback;
                    if (isUnsigned) return unsigned;
                    return signed < 0 ? fallback : (ulong)signed;
                case MsgPackType.Float:
                    double d = ReadDouble();
                    return d < 0 ? fallback : (ulong)d;
                default:
                    Skip();
                    return fallback;
            }
        }

        public bool ReadBool(bool fallback = false)
        {
            switch (PeekType())
            {
                case MsgPackType.Bool:
                    return _data[_pos++] == 0xc3;
                case MsgPackType.Integer:
                    return ReadInt64() != 0;
                default:
                    Skip();
                    return fallback;
            }
        }

        /// <summary>Reads a string. nil and values of other types read as empty.</summary>
        public string ReadString()
        {
            MsgPackType type = PeekType();
            if (type == MsgPackType.String && TryReadStringSlice(out int offset, out int length))
            {
                return Encoding.UTF8.GetString(_data, offset, length);
            }
            if (type == MsgPackType.Binary)
            {
                byte[] bytes = ReadBytes();
                return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            }
            if (type != MsgPackType.String) Skip();
            return string.Empty;
        }

        /// <summary>
        /// Reads bytes from <c>bin</c>, <c>str</c> or an array of integers, all
        /// of which CLX senders use for a Hash: Go and <c>serde_bytes</c> write
        /// <c>bin</c>, a plain Rust <c>[u8; 32]</c> writes an array. nil and other
        /// types read as empty.
        /// </summary>
        public byte[] ReadBytes()
        {
            switch (PeekType())
            {
                case MsgPackType.Binary:
                    byte b = _data[_pos++];
                    int size = b == 0xc4 ? 1 : b == 0xc5 ? 2 : 4;
                    if (!TryReadLength(size, out int binLength)) return Array.Empty<byte>();
                    return CopyBytes(binLength);
                case MsgPackType.String:
                    if (!TryReadStringSlice(out int offset, out int length)) return Array.Empty<byte>();
                    var text = new byte[length];
                    Buffer.BlockCopy(_data, offset, text, 0, length);
                    return text;
                case MsgPackType.Array:
                    if (!TryReadArrayHeader(out int count)) return Array.Empty<byte>();
                    var items = new byte[count];
                    for (int i = 0; i < count && !_failed; i++)
                    {
                        items[i] = unchecked((byte)ReadInt64());
                    }
                    return items;
                default:
                    Skip();
                    return Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Reads any value as an object: null, bool, long, ulong (only above
        /// long.MaxValue), double, string, byte[], object[] or
        /// Dictionary&lt;string, object&gt;. Extension values read as null.
        /// </summary>
        public object ReadValue() => ReadValue(0);

        object ReadValue(int depth)
        {
            if (depth > MaxDepth)
            {
                Fail();
                return null;
            }
            switch (PeekType())
            {
                case MsgPackType.Bool:
                    return ReadBool();
                case MsgPackType.Integer:
                    if (!TryReadInteger(out long signed, out ulong unsigned, out bool isUnsigned)) return null;
                    if (!isUnsigned) return signed;
                    return unsigned > long.MaxValue ? (object)unsigned : (long)unsigned;
                case MsgPackType.Float:
                    return ReadDouble();
                case MsgPackType.String:
                    return ReadString();
                case MsgPackType.Binary:
                    return ReadBytes();
                case MsgPackType.Array:
                    if (!TryReadArrayHeader(out int count)) return null;
                    var items = new object[count];
                    for (int i = 0; i < count && !_failed; i++) items[i] = ReadValue(depth + 1);
                    return items;
                case MsgPackType.Map:
                    if (!TryReadMapHeader(out int pairs)) return null;
                    var map = new Dictionary<string, object>(pairs);
                    for (int i = 0; i < pairs && !_failed; i++)
                    {
                        string key = ReadKey().ToString();
                        map[key] = ReadValue(depth + 1);
                    }
                    return map;
                default:
                    Skip();
                    return null;
            }
        }

        /// <summary>Consumes one value of any type, including anything nested in it.</summary>
        public void Skip() => Skip(0);

        void Skip(int depth)
        {
            if (depth > MaxDepth)
            {
                Fail();
                return;
            }
            switch (PeekType())
            {
                case MsgPackType.Nil:
                case MsgPackType.Bool:
                    _pos++;
                    break;
                case MsgPackType.Integer:
                    TryReadInteger(out _, out _, out _);
                    break;
                case MsgPackType.Float:
                    Advance(_data[_pos++] == 0xca ? 4 : 8);
                    break;
                case MsgPackType.String:
                    TryReadStringSlice(out _, out _);
                    break;
                case MsgPackType.Binary:
                    byte b = _data[_pos++];
                    if (TryReadLength(b == 0xc4 ? 1 : b == 0xc5 ? 2 : 4, out int length)) Advance(length);
                    break;
                case MsgPackType.Array:
                    if (!TryReadArrayHeader(out int count)) break;
                    for (int i = 0; i < count && !_failed; i++) Skip(depth + 1);
                    break;
                case MsgPackType.Map:
                    if (!TryReadMapHeader(out int pairs)) break;
                    for (int i = 0; i < pairs * 2L && !_failed; i++) Skip(depth + 1);
                    break;
                case MsgPackType.Extension:
                    SkipExtension();
                    break;
                default:
                    Fail();
                    break;
            }
        }

        void SkipExtension()
        {
            byte b = _data[_pos++];
            switch (b)
            {
                case 0xd4: Advance(1 + 1); break;
                case 0xd5: Advance(1 + 2); break;
                case 0xd6: Advance(1 + 4); break;
                case 0xd7: Advance(1 + 8); break;
                case 0xd8: Advance(1 + 16); break;
                default:
                    if (TryReadLength(b == 0xc7 ? 1 : b == 0xc8 ? 2 : 4, out int length)) Advance(1 + (long)length);
                    break;
            }
        }

        bool TryReadInteger(out long signed, out ulong unsigned, out bool isUnsigned)
        {
            signed = 0;
            unsigned = 0;
            isUnsigned = false;
            byte b = _data[_pos++];
            if (b <= 0x7f)
            {
                signed = b;
                return true;
            }
            if (b >= 0xe0)
            {
                signed = (sbyte)b;
                return true;
            }
            switch (b)
            {
                case 0xcc:
                case 0xcd:
                case 0xce:
                case 0xcf:
                    if (!TryReadBigEndian(1 << (b - 0xcc), out unsigned)) return false;
                    isUnsigned = true;
                    return true;
                case 0xd0:
                    if (!TryReadBigEndian(1, out ulong i8)) return false;
                    signed = (sbyte)i8;
                    return true;
                case 0xd1:
                    if (!TryReadBigEndian(2, out ulong i16)) return false;
                    signed = (short)i16;
                    return true;
                case 0xd2:
                    if (!TryReadBigEndian(4, out ulong i32)) return false;
                    signed = (int)i32;
                    return true;
                default: // 0xd3
                    if (!TryReadBigEndian(8, out ulong i64)) return false;
                    signed = unchecked((long)i64);
                    return true;
            }
        }

        bool TryReadStringSlice(out int offset, out int length)
        {
            offset = 0;
            length = 0;
            if (PeekType() != MsgPackType.String) return false;
            byte b = _data[_pos++];
            if (b >= 0xa0 && b <= 0xbf) length = b & 0x1f;
            else if (!TryReadLength(b == 0xd9 ? 1 : b == 0xda ? 2 : 4, out length)) return false;
            if (length > _end - _pos) return Fail();
            offset = _pos;
            _pos += length;
            return true;
        }

        bool TryReadLength(int size, out int length)
        {
            length = 0;
            if (!TryReadBigEndian(size, out ulong value)) return false;
            if (value > int.MaxValue) return Fail();
            length = (int)value;
            return true;
        }

        bool TryReadBigEndian(int size, out ulong value)
        {
            value = 0;
            if (size > _end - _pos) return Fail();
            for (int i = 0; i < size; i++) value = (value << 8) | _data[_pos++];
            return true;
        }

        byte[] CopyBytes(int length)
        {
            if (length > _end - _pos)
            {
                Fail();
                return Array.Empty<byte>();
            }
            var copy = new byte[length];
            Buffer.BlockCopy(_data, _pos, copy, 0, length);
            _pos += length;
            return copy;
        }

        void Advance(long count)
        {
            if (count > _end - _pos) Fail();
            else _pos += (int)count;
        }

        bool Fail()
        {
            _failed = true;
            _pos = _end;
            return false;
        }
    }
}
