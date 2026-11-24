using System;
using System.Collections;
using System.Reflection;
using Monocle;

namespace Celeste.Mod.ZantasLoader;

// Reads Lhotse's load profiler through reflection, so Zantas Loader still loads on stock Everest.
public static class LoadTimes {
    private static readonly Type Profiler =
        typeof(Everest).GetNestedType("Lhotse")?.GetNestedType("LoadProfiler");

    public static bool Available => Profiler != null;

    [Command("zantas_loadtimes", "Zantas Loader: show the slowest-loading mods (needs Lhotse). Optional argument: how many to show")]
    public static void CmdLoadTimes(int top = 15) {
        if (!Available) {
            Engine.Commands.Log("Load times need the Lhotse loader; this game is running stock Everest.");
            return;
        }
        if (top <= 0)
            top = 15;

        IEnumerable entries = (IEnumerable) Profiler.GetMethod("GetEntries").Invoke(null, null);
        Type phaseType = Profiler.GetNestedType("Phase");
        Array phases = Enum.GetValues(phaseType);

        Engine.Commands.Log("   total |   crawl |     dll |    load | content |    init | mod");
        int shown = 0, count = 0;
        double sum = 0;
        foreach (object entry in entries) {
            Type t = entry.GetType();
            double total = (double) t.GetProperty("TotalMs").GetValue(entry);
            sum += total;
            count++;
            if (shown >= top)
                continue;
            shown++;

            MethodInfo getMs = t.GetMethod("GetMs");
            string line = $"{total,6:F0}ms";
            foreach (object phase in phases)
                line += $" | {(double) getMs.Invoke(entry, new[] { phase }),5:F0}ms";
            Engine.Commands.Log($"{line} | {t.GetProperty("Name").GetValue(entry)}");
        }
        Engine.Commands.Log($"{sum:F0}ms measured across {count} mods");
    }
}
