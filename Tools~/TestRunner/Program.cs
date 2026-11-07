// Runs the tests in Tests/Editor outside Unity:
//   TestRunner [--cache <folder>]
// --cache also decodes every .rwf, .bg and .cues file in a Viewer cache folder,
// which holds what real senders really sent. Those files aren't in this repo.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Clockworks.CLX;
using NUnit.Framework;

static class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");

        int passed = 0, failed = 0;
        var fixtures = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetCustomAttribute<TestFixtureAttribute>() != null)
            .OrderBy(t => t.Name);
        foreach (Type fixture in fixtures)
        {
            var tests = fixture.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .OrderBy(m => m.Name);
            foreach (MethodInfo test in tests)
            {
                try
                {
                    test.Invoke(Activator.CreateInstance(fixture), null);
                    passed++;
                }
                catch (TargetInvocationException e)
                {
                    failed++;
                    Console.WriteLine($"FAILED {fixture.Name}.{test.Name}: {e.InnerException}");
                }
            }
        }
        Console.WriteLine($"{passed} passed, {failed} failed");

        int index = Array.IndexOf(args, "--cache");
        if (index >= 0 && index + 1 < args.Length) failed += DecodeCache(args[index + 1]);
        return failed == 0 ? 0 : 1;
    }

    static int DecodeCache(string folder)
    {
        int bad = 0, waveforms = 0, grids = 0, gridsChecked = 0, cueLists = 0, cues = 0, hotcues = 0;
        long pixels = 0;
        foreach (string path in Directory.GetFiles(folder))
        {
            string hash = Path.GetFileNameWithoutExtension(path);
            byte[] data;
            switch (Path.GetExtension(path))
            {
                case ".rwf":
                    data = File.ReadAllBytes(path);
                    if (CLXWaveform.TryParse(data, out CLXWaveform waveform)
                        && waveform.HasColors
                        && waveform.Peaks.Length == waveform.Length * waveform.Channels * 2)
                    {
                        waveforms++;
                        pixels += waveform.Length;
                    }
                    else
                    {
                        bad++;
                        Console.WriteLine($"Bad waveform: {path}");
                    }
                    break;
                case ".bg":
                    data = File.ReadAllBytes(path);
                    if (CLXDecoder.TryDecodeBeatgrid(data, out BeatgridPayload grid) && grid.Hash == hash)
                    {
                        grids++;
                        if (grid.Markers.Length > 0)
                        {
                            BeatgridMarker last = grid.Markers[grid.Markers.Length - 1];
                            float seconds = last.Position + 60 * 60;
                            if (grid.ExpandBeats(seconds).Length > 0) gridsChecked++;
                        }
                    }
                    else
                    {
                        bad++;
                        Console.WriteLine($"Bad beat grid: {path}");
                    }
                    break;
                case ".cues":
                    data = File.ReadAllBytes(path);
                    if (CLXDecoder.TryDecodeCues(data, out CuesPayload list) && list.Hash == hash)
                    {
                        cueLists++;
                        cues += list.Cues.Length;
                        hotcues += list.Cues.Count(c => c.Hotcue);
                    }
                    else
                    {
                        bad++;
                        Console.WriteLine($"Bad cue list: {path}");
                    }
                    break;
            }
        }
        Console.WriteLine(
            $"Real cache: {waveforms} waveforms ({pixels} pixels), {grids} beat grids ({gridsChecked} expanded), "
            + $"{cueLists} cue lists ({cues} cues, {hotcues} hot), {bad} bad");
        return bad;
    }
}
