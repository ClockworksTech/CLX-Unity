using System.Security.Cryptography;
using System.Text;

namespace Clockworks.CLX
{
    /// <summary>
    /// CLX track hashes: the lowercase hex MD5 of the track title. A deck's Meta
    /// packet gives the title, and the title's hash finds that track's waveform,
    /// beat grid and cues.
    /// </summary>
    public static class CLXHash
    {
        /// <summary>The track hash for a title, or an empty string for an empty title.</summary>
        public static string ForTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return string.Empty;
            using (MD5 md5 = MD5.Create())
            {
                return ToHex(md5.ComputeHash(Encoding.UTF8.GetBytes(title)));
            }
        }

        /// <summary>
        /// Turns a Hash field into a track hash: 32 ASCII hex characters, lowercase.
        /// Tolerates padding with NULs or spaces. Returns null for anything else,
        /// so a malformed hash can't become a cache file name.
        /// </summary>
        public static string Normalize(byte[] hash)
        {
            if (hash == null) return null;
            int length = hash.Length;
            while (length > 0 && (hash[length - 1] == 0 || hash[length - 1] == (byte)' ')) length--;
            if (length != 32) return null;
            var chars = new char[32];
            for (int i = 0; i < 32; i++)
            {
                byte b = hash[i];
                bool digit = b >= '0' && b <= '9';
                bool lower = b >= 'a' && b <= 'f';
                bool upper = b >= 'A' && b <= 'F';
                if (!digit && !lower && !upper) return null;
                chars[i] = upper ? (char)(b + ('a' - 'A')) : (char)b;
            }
            return new string(chars);
        }

        /// <summary>Lowercase hex of any bytes.</summary>
        public static string ToHex(byte[] bytes)
        {
            const string digits = "0123456789abcdef";
            var chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 0x0f];
            }
            return new string(chars);
        }
    }
}
