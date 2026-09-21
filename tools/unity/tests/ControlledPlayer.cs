// Harness fixture only. This executable is not Unity and cannot establish UA01/UA02.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

internal static class ControlledPlayer
{
    private static string Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException(name);
        return args[index + 1];
    }

    private static int Main(string[] args)
    {
        string mode = Environment.GetEnvironmentVariable("COMPANION_FIXTURE_MODE") ?? "normal";
        string evidence = Argument(args, "-evidenceDirectory");
        string log = Argument(args, "-logFile");
        int seconds = int.Parse(Argument(args, "-smokeSeconds"), CultureInfo.InvariantCulture);
        Directory.CreateDirectory(evidence);
        File.WriteAllText(log, "CONTROLLED HARNESS FIXTURE; NOT UNITY. Mode=" + mode + "\n");
        File.WriteAllText(Path.Combine(evidence, "fixture.pid"),
            Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
        if (mode == "hang")
        {
            while (true) Thread.Sleep(1000);
        }
        if (mode == "normal") Thread.Sleep(seconds * 1000);
        // Synthetic data deliberately exercises the wrapper's output validators only.
        string result = "{\"fixtureOnly\":true,\"errors\":0,\"unsupportedMaterials\":" +
            (mode == "invalid-result" ? "1" : "0") +
            ",\"maskedDrawables\":1,\"elapsedSeconds\":" + seconds + "}";
        File.WriteAllText(Path.Combine(evidence, "player-result.json"), result);
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j4xkAAAAASUVORK5CYII=");
        File.WriteAllBytes(Path.Combine(evidence, "player-03s.png"), png);
        if (mode != "missing-image")
            File.WriteAllBytes(Path.Combine(evidence, "player-10s.png"), png);
        File.WriteAllText(Path.Combine(evidence, "frame-times.csv"), "fixtureOnly,seconds\ntrue,15\n");
        File.AppendAllText(log, "Controlled fixture exits normally.\n");
        return 0;
    }
}
