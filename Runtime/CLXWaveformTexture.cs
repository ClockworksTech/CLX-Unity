using System;
using UnityEngine;

namespace Clockworks.CLX
{
    /// <summary>Draws a <see cref="CLXWaveform"/> into a Texture2D.</summary>
    public static class CLXWaveformTexture
    {
        /// <summary>
        /// Renders the whole track, left to right, on a transparent background.
        /// Each column uses the waveform's own colour, or <paramref name="fallback"/>
        /// if it has none. Call this on the main thread.
        /// </summary>
        /// <param name="waveform">The waveform.</param>
        /// <param name="width">Texture width in pixels.</param>
        /// <param name="height">Texture height in pixels.</param>
        /// <param name="fallback">Colour for a waveform with no colour table.</param>
        /// <param name="reuse">A texture to draw into if it's the right size. Otherwise a new one is made.</param>
        public static Texture2D Render(
            CLXWaveform waveform, int width, int height, Color32 fallback, Texture2D reuse = null)
        {
            if (waveform == null) throw new ArgumentNullException(nameof(waveform));
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);

            Texture2D texture = reuse;
            if (texture == null || texture.width != width || texture.height != height)
            {
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            var pixels = new Color32[width * height];
            float middle = (height - 1) * 0.5f;
            for (int x = 0; x < width; x++)
            {
                int first = (int)((long)x * waveform.Length / width);
                int last = Mathf.Max(first + 1, (int)((long)(x + 1) * waveform.Length / width));
                GetColumn(waveform, first, last, fallback, out float min, out float max, out Color32 color);

                int bottom = Mathf.Clamp(Mathf.RoundToInt(middle + min * middle), 0, height - 1);
                int top = Mathf.Clamp(Mathf.RoundToInt(middle + max * middle), 0, height - 1);
                for (int y = bottom; y <= top; y++)
                {
                    pixels[y * width + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        static void GetColumn(
            CLXWaveform waveform, int first, int last, Color32 fallback,
            out float min, out float max, out Color32 color)
        {
            min = 0;
            max = 0;
            int r = 0, g = 0, b = 0;
            for (int pixel = first; pixel < last; pixel++)
            {
                for (int channel = 0; channel < waveform.Channels; channel++)
                {
                    waveform.GetPeak(pixel, channel, out float low, out float high);
                    if (low < min) min = low;
                    if (high > max) max = high;
                }
                if (waveform.HasColors)
                {
                    r += waveform.Colors[pixel * 3];
                    g += waveform.Colors[pixel * 3 + 1];
                    b += waveform.Colors[pixel * 3 + 2];
                }
            }

            if (!waveform.HasColors)
            {
                color = fallback;
                return;
            }
            int count = last - first;
            color = new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), 255);
        }
    }
}
