using System;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.ZantasLoader;

// Zantas Loader: Mount Zantas's own layer on top of Everest.
// Everest still does the real loading (so every Everest mod works); this module
//   - shows "Zantas Loader" and the active Mount Zantas preset on the title screen and in the window title,
//   - reports back to Mount Zantas which mods loaded and which failed (zantas-session.json),
//   - adds a "Zantas Loader" page to Mod Options with that same report.
public class ZantasLoaderModule : EverestModule {
    public static ZantasLoaderModule Instance { get; private set; }

    public override Type SettingsType => typeof(ZantasLoaderSettings);

    public static ZantasLoaderSettings Settings => (ZantasLoaderSettings) Instance._Settings;

    // Filled in once loading has finished; shown in Mod Options.
    public static SessionReport LastReport { get; private set; }

    public ZantasLoaderModule() {
        Instance = this;
    }

    public override void Load() {
        On.Celeste.OuiTitleScreen.ctor += OnTitleScreenCreated;
        On.Celeste.OuiTitleScreen.Render += OnTitleScreenRender;
        Logger.Log(LogLevel.Info, "ZantasLoader", $"Zantas Loader {Metadata.VersionString} on Everest {Everest.VersionString}");
    }

    public override void Unload() {
        On.Celeste.OuiTitleScreen.ctor -= OnTitleScreenCreated;
        On.Celeste.OuiTitleScreen.Render -= OnTitleScreenRender;
    }

    public override void Initialize() {
        ApplyWindowTitle();
    }

    // LoadContent(true) runs once, after Everest has loaded every mod, so the report is complete here.
    public override void LoadContent(bool firstLoad) {
        if (!firstLoad) {
            return;
        }
        LastReport = SessionReport.Collect();
        Logger.Log(LogLevel.Info, "ZantasLoader", $"{LastReport.Mods.Count} mods loaded, {LastReport.Problems.Count} problems");
        if (!Settings.WriteSessionReport) {
            return;
        }
        try {
            LastReport.Write();
        } catch (Exception e) {
            Logger.Log(LogLevel.Warn, "ZantasLoader", "Could not write the session report: " + e.Message);
        }
    }

    // The title screen draws its "version" text in the corner; we add our own line under Everest's.
    static void OnTitleScreenCreated(On.Celeste.OuiTitleScreen.orig_ctor orig, OuiTitleScreen self) {
        orig(self);
        if (!Settings.ShowOnTitleScreen) {
            return;
        }
        string line = $"\nZantas Loader v.{Instance.Metadata.VersionString}";
        string profile = LaunchProfile.ActiveName();
        if (profile != null) {
            line += $" (preset: {profile})";
        }
        self.version += line;
    }

    // The Zantas mark (Graphics/Atlases/Gui/zantasloader/mark.png): a mountain with a gear and a star in its middle.
    const string MarkTexture = "zantasloader/mark";
    // Drawn at half size: the PNG is 128 px, so the mark is 64 px on the 1920 x 1080 screen.
    const float MarkScale = 0.5f;

    // Draws the Zantas mark just above the version lines in the bottom-left corner.
    // The title screen draws those lines with their bottom at textY, at half font size, growing upwards,
    // so the mark goes above the whole block and slides in and out with it.
    static void OnTitleScreenRender(On.Celeste.OuiTitleScreen.orig_Render orig, OuiTitleScreen self) {
        orig(self);
        if (!Settings.ShowOnTitleScreen) {
            return;
        }
        if (!GFX.Gui.Has(MarkTexture)) {
            return;
        }
        float textHeight = ActiveFont.Measure(self.version).Y * 0.5f;
        var position = new Vector2(80f, self.textY - textHeight - 6f);
        GFX.Gui[MarkTexture].DrawJustified(position, new Vector2(0f, 1f), Color.White * self.alpha, MarkScale);
    }

    static void ApplyWindowTitle() {
        if (Engine.Instance?.Window == null) {
            return;
        }
        if (Settings.BrandWindowTitle) {
            Engine.Instance.Window.Title = "Celeste - Zantas Loader";
        } else {
            Engine.Instance.Window.Title = "Celeste";
        }
    }

    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
        // The on/off switches from ZantasLoaderSettings.
        base.CreateModMenuSection(menu, inGame, snapshot);

        SessionReport report = LastReport;
        if (report == null) {
            return;
        }
        string profile = report.Profile ?? "none (normal start)";
        menu.Add(new TextMenu.SubHeader($"Mount Zantas preset: {profile}", false));
        menu.Add(new TextMenu.SubHeader($"{report.Mods.Count} mods loaded by Everest", false));
        if (report.Problems.Count == 0) {
            menu.Add(new TextMenu.SubHeader("No load problems", false));
            return;
        }
        menu.Add(new TextMenu.SubHeader($"{report.Problems.Count} load problems:", false));
        foreach (string problem in report.Problems) {
            menu.Add(new TextMenu.SubHeader(problem, false));
        }
    }

    // Called by Everest when a setting on our Mod Options page changes.
    public override void SaveSettings() {
        base.SaveSettings();
        ApplyWindowTitle();
    }
}
