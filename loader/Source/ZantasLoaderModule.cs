using System;
using System.Linq;
using Celeste.Mod.UI;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.ZantasLoader;

// Zantas Loader: Mount Zantas's own layer on top of Everest.
// Everest still does the real loading (so every Everest mod works); this module
//   - shows "Zantas Loader" and the active Mount Zantas preset on the title screen and in the window title,
//   - reports back to Mount Zantas which mods loaded and which failed (zantas-session.json),
//   - adds a "Zantas Loader" page to Mod Options with that same report and in-game mod profiles,
//   - changes the main-menu credits (OuiCreditsPatch),
//   - adds zantas_* debug console commands: mods, profiles, load times and hook conflicts.
// The profiles, load times and conflict commands used to be a separate mod, Cairn, and were merged in here.
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
        OuiCreditsPatch.Load();
        Logger.Log(LogLevel.Info, "ZantasLoader", $"Zantas Loader {Metadata.VersionString} on Everest {Everest.VersionString}");
    }

    public override void Unload() {
        On.Celeste.OuiTitleScreen.ctor -= OnTitleScreenCreated;
        On.Celeste.OuiTitleScreen.Render -= OnTitleScreenRender;
        OuiCreditsPatch.Unload();
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
        AddReportSection(menu);
        AddProfilesSection(menu, inGame);
    }

    // The load report: which preset is active, how many mods loaded, and what failed.
    static void AddReportSection(TextMenu menu) {
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

    // In-game mod profiles (from Cairn): save the set of disabled mods under a name, and apply it later.
    static void AddProfilesSection(TextMenu menu, bool inGame) {
        int disabledNow = Profiles.CurrentDisabled().Count;
        menu.Add(new TextMenu.SubHeader($"Mod profiles ({disabledNow} mods disabled right now)", false));

        if (inGame) {
            // Applying restarts the game, and naming a profile needs the main menu's text entry.
            menu.Add(new TextMenu.SubHeader("Profiles can be saved and applied from the main menu", false));
            return;
        }

        foreach (string name in Profiles.List()) {
            string profile = name;
            int count = Profiles.Read(profile).Count;
            string label;
            if (profile == Profiles.PreviousName) {
                label = $"Undo last apply ({count} mods disabled)";
            } else {
                label = $"Apply \"{profile}\" ({count} mods disabled)";
            }
            menu.Add(new TextMenu.Button(label).Pressed(() => {
                if (Profiles.Apply(profile)) {
                    Everest.QuickFullRestart();
                }
            }));
        }

        menu.Add(new TextMenu.Button("Save current mods as a new profile").Pressed(() => {
            Audio.Play(SFX.ui_main_savefile_rename_start);
            // The text entry reports every keystroke; only save once the name is confirmed.
            string typed = "";
            menu.SceneAs<Overworld>().Goto<OuiModOptionString>().Init<OuiModOptions>(
                "",
                value => typed = value,
                confirmed => {
                    if (confirmed) {
                        Profiles.Save(typed);
                    }
                },
                20,
                1
            );
        }));

        menu.Add(new TextMenu.SubHeader("Applying a profile restarts the game. Use Toggle Mods to change which mods are on.", false));
    }

    // Debug console (~): lists every loaded mod with its version; code mods are marked [dll].
    [Command("zantas_mods", "Zantas Loader: list loaded mods and versions")]
    public static void CmdMods() {
        var modules = Everest.Modules.OrderBy(m => m.Metadata?.Name, StringComparer.OrdinalIgnoreCase);
        foreach (EverestModule module in modules) {
            EverestModuleMetadata meta = module.Metadata;
            if (meta == null) {
                continue;
            }
            string kind = string.IsNullOrEmpty(meta.DLL) ? "     " : "[dll]";
            Engine.Commands.Log($"{kind} {meta.Name} {meta.VersionString}");
        }
        Engine.Commands.Log($"{Everest.Modules.Count} modules loaded");
    }

    // Called by Everest when a setting on our Mod Options page changes.
    public override void SaveSettings() {
        base.SaveSettings();
        ApplyWindowTitle();
    }
}
