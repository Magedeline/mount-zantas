using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Celeste.Mod.ZantasLoader;

// The load report the loader hands back to Mount Zantas.
// It is written to zantas-session.json in the Celeste folder once the game has finished loading,
// and Mount Zantas reads it to show what actually loaded and what went wrong.
public class SessionReport {
    public const string FileName = "zantas-session.json";

    public string Loader { get; set; }

    public string Everest { get; set; }

    public string Profile { get; set; }

    public DateTime Started { get; set; }

    public List<LoadedMod> Mods { get; set; } = new List<LoadedMod>();

    public List<string> Problems { get; set; } = new List<string>();

    public class LoadedMod {
        public string Name { get; set; }

        public string Version { get; set; }

        public string File { get; set; }

        public bool Code { get; set; }
    }

    public static SessionReport Collect() {
        SessionReport report = new SessionReport();
        report.Loader = ZantasLoaderModule.Instance.Metadata.VersionString;
        report.Everest = global::Celeste.Mod.Everest.VersionString;
        report.Profile = LaunchProfile.ActiveName();
        report.Started = DateTime.Now;

        foreach (EverestModule module in global::Celeste.Mod.Everest.Modules) {
            EverestModuleMetadata meta = module.Metadata;
            if (meta == null) {
                continue;
            }
            // Everest lists the game and itself as modules too; they are not mods.
            if (meta.Name == "Celeste" || meta.Name == "Everest") {
                continue;
            }
            LoadedMod mod = new LoadedMod();
            mod.Name = meta.Name;
            mod.Version = meta.VersionString;
            mod.File = FileOf(meta);
            mod.Code = !string.IsNullOrEmpty(meta.DLL);
            report.Mods.Add(mod);
        }
        report.Mods = report.Mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // Everest's failure lists are internal and may change between Everest builds,
        // so reading them must never stop the report (or the game) from working.
        try {
            AddProblems(report.Problems);
        } catch (Exception e) {
            report.Problems.Add("Could not read Everest's load problems: " + e.Message);
        }
        return report;
    }

    // Reads one of Everest's internal static lists by name. Returns nothing if this Everest build doesn't have it.
    static IEnumerable<T> EverestList<T>(string fieldName) {
        Type loader = typeof(global::Celeste.Mod.Everest.Loader);
        FieldInfo field = loader.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (field == null) {
            return Enumerable.Empty<T>();
        }
        if (field.GetValue(null) is not IEnumerable<T> items) {
            return Enumerable.Empty<T>();
        }
        // Copy it, so Everest changing the list while we read it can't break the loop.
        return items.ToList();
    }

    static void AddProblems(List<string> problems) {
        foreach (string file in EverestList<string>("FilesWithMetadataLoadFailures")) {
            problems.Add($"{Path.GetFileName(file)}: its everest.yaml could not be read");
        }
        foreach (EverestModuleMetadata meta in EverestList<EverestModuleMetadata>("ModsWithAssemblyLoadFailures")) {
            problems.Add($"{meta.Name}: its code (DLL) failed to load");
        }
        foreach (Tuple<EverestModuleMetadata, Action> waiting in EverestList<Tuple<EverestModuleMetadata, Action>>("Delayed")) {
            EverestModuleMetadata meta = waiting.Item1;
            List<string> missing = new List<string>();
            foreach (EverestModuleMetadata dep in meta.Dependencies) {
                if (!global::Celeste.Mod.Everest.Loader.DependencyLoaded(dep)) {
                    missing.Add($"{dep.Name} {dep.VersionString}");
                }
            }
            problems.Add($"{meta.Name}: not loaded, it needs " + string.Join(", ", missing));
        }
    }

    static string FileOf(EverestModuleMetadata meta) {
        if (!string.IsNullOrEmpty(meta.PathArchive)) {
            return Path.GetFileName(meta.PathArchive);
        }
        if (!string.IsNullOrEmpty(meta.PathDirectory)) {
            return Path.GetFileName(meta.PathDirectory.TrimEnd('/', '\\'));
        }
        return "";
    }

    public void Write() {
        string path = Path.Combine(global::Celeste.Mod.Everest.PathGame, FileName);
        JsonSerializerOptions options = new JsonSerializerOptions();
        options.WriteIndented = true;
        string json = JsonSerializer.Serialize(this, options);
        // Write to a temporary file first, so Mount Zantas never reads a half-written report.
        string temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, true);
    }
}
