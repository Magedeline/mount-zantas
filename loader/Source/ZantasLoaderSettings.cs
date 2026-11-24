namespace Celeste.Mod.ZantasLoader;

// The options on the "Zantas Loader" page in Mod Options. Everest builds the on/off switches from these properties
// and saves them in Saves/modsettings-ZantasLoader.celeste.
public class ZantasLoaderSettings : EverestModuleSettings {
    public bool ShowOnTitleScreen { get; set; } = true;

    public bool BrandWindowTitle { get; set; } = true;

    public bool WriteSessionReport { get; set; } = true;
}
