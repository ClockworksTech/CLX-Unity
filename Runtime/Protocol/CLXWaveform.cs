using System;

namespace Clockworks.CLX
{
    /// <summary>
    /// A track's waveform, parsed from the bytes a sender transmits (the
    /// <c>.rwf</c> file format).
    ///
    /// The format is based on the BBC audiowaveform data format, with its header
    /// fields in a different order and a colour table added. All integers are
    /// little-endian:
    /// <code>
    /// int32 version, channels, sampleRate, samplesPerPixel, bits, length
    /// int8  peaks[length * channels * 2]    min, max per channel per pixel
    /// "CLRS", int32 colorCount, uint8 rgb[colorCount * 3]    optional
    /// </code>
    /// </summary>
    public sealed class CLXWaveform
    {
        const int HeaderSize = 24;

        // Guards the arithmetic and allocations against a hostile header.
        const int MaxLength = 256 * 1024 * 1024;
        const int MaxChannels = 64;

        /// <summary>The 32-character track hash.</summary>
        public string Hash = string.Empty;

        /// <summary>
        /// Where the file is cached, as CLX-UE's OnWaveformComplete reports. Empty
        /// when disk caching is off or the write failed.
        /// </summary>
        public string FilePath = string.Empty;

        /// <summary>The unparsed file. Empty if it didn't parse.</summary>
        public byte[] Data = Array.Empty<byte>();

        public int Version;
        public int Channels;
        public int SampleRate;
        public int SamplesPerPixel;
        public int Bits;

        /// <summary>The number of pixels (min/max pairs per channel).</summary>
        public int Length;

        /// <summary>Peaks as min, max per channel per pixel. Range -128 to 127.</summary>
        public sbyte[] Peaks = Array.Empty<sbyte>();

        /// <summary>RGB bytes, 3 per pixel. Empty when the file has no colour table.</summary>
        public byte[] Colors = Array.Empty<byte>();

        /// <summary>True when the file carried a colour table.</summary>
        public bool HasColors => Colors.Length > 0;

        /// <summary>The waveform's duration in seconds.</summary>
        public double Duration => SampleRate <= 0 ? 0 : (double)Length * SamplesPerPixel / SampleRate;

        /// <summary>Waveform pixels per second of audio.</summary>
        public double PixelsPerSecond => SamplesPerPixel <= 0 ? 0 : (double)SampleRate / SamplesPerPixel;

        /// <summary>The min and max peak of a pixel on a channel, from -1 to 1.</summary>
        public void GetPeak(int pixel, int channel, out float min, out float max)
        {
            int index = (pixel * Channels + channel) * 2;
            min = Peaks[index] / 128f;
            max = Peaks[index + 1] / 128f;
        }

        /// <summary>Parses a waveform file. Returns false if it's malformed.</summary>
        public static bool TryParse(byte[] data, out CLXWaveform waveform)
        {
            waveform = null;
            if (data == null || data.Length < HeaderSize) return false;

            var result = new CLXWaveform
            {
                Data = data,
                Version = ReadInt32(data, 0),
                Channels = ReadInt32(data, 4),
                SampleRate = ReadInt32(data, 8),
                SamplesPerPixel = ReadInt32(data, 12),
                Bits = ReadInt32(data, 16),
                Length = ReadInt32(data, 20),
            };
            if (result.Length <= 0 || result.Length > MaxLength) return false;
            if (result.Channels <= 0 || result.Channels > MaxChannels) return false;
            if (result.Bits != 8) return false; // Peaks are always int8 in practice.

            long peakBytes = (long)result.Length * result.Channels * 2;
            if (peakBytes > data.Length - HeaderSize) return false;

            result.Peaks = new sbyte[peakBytes];
            Buffer.BlockCopy(data, HeaderSize, result.Peaks, 0, (int)peakBytes);

            int marker = HeaderSize + (int)peakBytes;
            if (data.Length >= marker + 8
                && data[marker] == 'C' && data[marker + 1] == 'L'
                && data[marker + 2] == 'R' && data[marker + 3] == 'S')
            {
                long colors = Math.Min(ReadInt32(data, marker + 4), result.Length);
                long colorBytes = colors * 3;
                if (colors > 0 && colorBytes <= data.Length - (marker + 8))
                {
                    // A shorter table than Length still colours what it covers.
                    result.Colors = new byte[result.Length * 3];
                    Buffer.BlockCopy(data, marker + 8, result.Colors, 0, (int)colorBytes);
                }
            }

            waveform = result;
            return true;
        }

        static int ReadInt32(byte[] data, int offset) =>
            data[offset]
            | data[offset + 1] << 8
            | data[offset + 2] << 16
            | data[offset + 3] << 24;
    }
}
