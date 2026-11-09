using System;
using System.Text;

namespace CityBiz.Core.Net
{
    /// <summary>Компактная бинарная запись: varint для целых, без рефлексии и аллокаций на поле.</summary>
    public sealed class NetWriter
    {
        byte[] _buffer;
        int _length;

        public NetWriter(int capacity = 256) { _buffer = new byte[capacity]; }

        public int Length => _length;
        public void Clear() => _length = 0;

        public byte[] ToArray()
        {
            var result = new byte[_length];
            Buffer.BlockCopy(_buffer, 0, result, 0, _length);
            return result;
        }

        public ArraySegment<byte> Segment => new ArraySegment<byte>(_buffer, 0, _length);

        void Ensure(int extra)
        {
            if (_length + extra <= _buffer.Length) return;
            int size = Math.Max(_buffer.Length * 2, _length + extra);
            Array.Resize(ref _buffer, size);
        }

        public void WriteByte(byte v) { Ensure(1); _buffer[_length++] = v; }
        public void WriteBool(bool v) => WriteByte(v ? (byte)1 : (byte)0);

        public void WriteVarULong(ulong v)
        {
            Ensure(10);
            while (v >= 0x80) { _buffer[_length++] = (byte)(v | 0x80); v >>= 7; }
            _buffer[_length++] = (byte)v;
        }

        public void WriteLong(long v) => WriteVarULong((ulong)((v << 1) ^ (v >> 63)));
        public void WriteInt(int v) => WriteLong(v);

        public void WriteFloat(float v)
        {
            int bits = BitConverter.SingleToInt32Bits(v);
            Ensure(4);
            _buffer[_length++] = (byte)bits;
            _buffer[_length++] = (byte)(bits >> 8);
            _buffer[_length++] = (byte)(bits >> 16);
            _buffer[_length++] = (byte)(bits >> 24);
        }

        public void WriteDouble(double v) => WriteVarULong((ulong)BitConverter.DoubleToInt64Bits(v));

        public void WriteString(string s)
        {
            if (s == null) { WriteVarULong(0); return; }
            int count = Encoding.UTF8.GetByteCount(s);
            WriteVarULong((ulong)count + 1);
            Ensure(count);
            Encoding.UTF8.GetBytes(s, 0, s.Length, _buffer, _length);
            _length += count;
        }

        public void WriteRaw(byte[] data, int offset, int count)
        {
            Ensure(count);
            Buffer.BlockCopy(data, offset, _buffer, _length, count);
            _length += count;
        }

        public void WriteBytes(byte[] data, int offset, int count)
        {
            WriteVarULong((ulong)count);
            Ensure(count);
            Buffer.BlockCopy(data, offset, _buffer, _length, count);
            _length += count;
        }
    }

    public sealed class NetReader
    {
        readonly byte[] _data;
        int _pos;
        readonly int _end;

        public NetReader(byte[] data) : this(data, 0, data.Length) { }
        public NetReader(ArraySegment<byte> seg) : this(seg.Array, seg.Offset, seg.Count) { }

        public NetReader(byte[] data, int offset, int count)
        {
            _data = data;
            _pos = offset;
            _end = offset + count;
        }

        public bool EndOfData => _pos >= _end;

        byte Next()
        {
            if (_pos >= _end) throw new FormatException("Unexpected end of network message");
            return _data[_pos++];
        }

        public byte ReadByte() => Next();
        public bool ReadBool() => Next() != 0;

        public ulong ReadVarULong()
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                byte b = Next();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift > 63) throw new FormatException("Varint too long");
            }
        }

        public long ReadLong()
        {
            ulong v = ReadVarULong();
            return (long)(v >> 1) ^ -(long)(v & 1);
        }

        public int ReadInt() => (int)ReadLong();

        public float ReadFloat()
        {
            int bits = Next() | (Next() << 8) | (Next() << 16) | (Next() << 24);
            return BitConverter.Int32BitsToSingle(bits);
        }

        public double ReadDouble() => BitConverter.Int64BitsToDouble((long)ReadVarULong());

        public string ReadString()
        {
            ulong len = ReadVarULong();
            if (len == 0) return null;
            int count = (int)len - 1;
            if (_pos + count > _end) throw new FormatException("String exceeds message");
            string s = Encoding.UTF8.GetString(_data, _pos, count);
            _pos += count;
            return s;
        }

        public byte[] ReadBytes()
        {
            int count = (int)ReadVarULong();
            if (_pos + count > _end) throw new FormatException("Byte block exceeds message");
            var result = new byte[count];
            Buffer.BlockCopy(_data, _pos, result, 0, count);
            _pos += count;
            return result;
        }
    }
}
