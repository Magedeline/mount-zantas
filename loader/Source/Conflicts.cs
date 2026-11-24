using System;
using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.ZantasLoader;

// Reads Lhotse's hook conflict tracker through reflection, so Zantas Loader still loads on stock Everest.
public static class Conflicts {
    private static readonly Type Tracker =
        typeof(Everest).GetNestedType("Lhotse")?.GetNestedType("Conflicts");

    [Command("zantas_conflicts", "Zantas Loader: list game methods hooked by two or more mods (needs Lhotse). Optional argument: how many to show")]
    public static void CmdConflicts(int top = 20) {
        if (Tracker == null) {
            Engine.Commands.Log("Hook conflicts need the Lhotse loader; this game is running stock Everest.");
            return;
        }
        if (top <= 0)
            top = 20;

        int total = (int) Tracker.GetProperty("SharedMethodCount").GetValue(null);
        IEnumerable lines = (IEnumerable) Tracker.GetMethod("GetSharedMethodSummaries").Invoke(null, new object[] { top });
        foreach (object line in lines)
            Engine.Commands.Log(line.ToString());
        Engine.Commands.Log($"{total} methods are hooked by two or more mods. Sharing a method is normal; it only matters when something breaks.");
    }

    // Briefly hooks a small game method with a hook that throws, calls it, and prints what
    // Lhotse's crash analysis says about the resulting exception. Nothing is left hooked.
    [Command("zantas_selftest", "Zantas Loader: check that Lhotse's crash analysis names the right mod, using a harmless fake crash")]
    public static void CmdSelfTest() {
        if (Tracker == null) {
            Engine.Commands.Log("The self-test needs the Lhotse loader; this game is running stock Everest.");
            return;
        }

        MethodInfo target = typeof(Calc).GetMethod("SafeNormalize", new[] { typeof(Vector2) });
        string analysis;
        using (new Hook(target, (Func<Func<Vector2, Vector2>, Vector2, Vector2>) ThrowingHook)) {
            try {
                target.Invoke(null, new object[] { Vector2.One });
                analysis = "The fake crash did not happen: the test hook was not called.";
            } catch (Exception e) {
                analysis = (string) Tracker.GetMethod("Analyze").Invoke(null, new object[] { e });
            }
        }

        // Second case: the game method's own body is edited (IL hook) and the edit throws.
        string analysisIL;
        using (new ILHook(target, il => new ILCursor(il).EmitDelegate<Action>(ThrowFromEditedBody))) {
            try {
                target.Invoke(null, new object[] { Vector2.One });
                analysisIL = "The fake crash did not happen: the test edit was not run.";
            } catch (Exception e) {
                analysisIL = (string) Tracker.GetMethod("Analyze").Invoke(null, new object[] { e });
            }
        }

        string report = "Case 1, a hook that throws:\n" + analysis + "\nCase 2, an edited method body that throws:\n" + analysisIL;
        Logger.Log(LogLevel.Info, "ZantasLoader", "Self-test result:\n" + report);
        foreach (string line in report.Split('\n'))
            Engine.Commands.Log(line.TrimEnd());
    }

    private static void ThrowFromEditedBody()
        => throw new InvalidOperationException("Zantas Loader self-test: this crash is fake");

    private static Vector2 ThrowingHook(Func<Vector2, Vector2> orig, Vector2 vec)
        => throw new InvalidOperationException("Zantas Loader self-test: this crash is fake");
}
