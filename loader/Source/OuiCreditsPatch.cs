using System.Collections.Generic;
using Monocle;

namespace Celeste.Mod.ZantasLoader;

// What the credits roll shows. Picked on the "Zantas Loader" page in Mod Options.
public enum CreditsMode {
    // The game's credits, untouched.
    Original,
    // The Mount Zantas credits first, then the game's credits.
    AddZantas,
    // Only the Mount Zantas credits. The original developers still get one closing line (see ReplaceCredits).
    ReplaceWithZantas,
}

// OuiCreditsPatch: changes the credits screen behind the "Credits" button on the main menu (OuiCredits).
// The game builds every credits roll (that screen and the end of chapter 7)
// by calling Credits.CreateCredits, which returns a list of "nodes" (a title, a role, a list of names...).
// We hook that method and only change the list when we are on the main menu.
public static class OuiCreditsPatch {

    // ---- Edit the Mount Zantas credits here. ----

    const string ModTitle = "Mount Zantas";

    // Each role is one heading with the people under it.
    static readonly (string Role, string[] People)[] Roles = {
        ("Created by", new[] { "Pusheencutangy" }),
        ("Zantas Loader", new[] { "Pusheencutangy" }),
        ("Art and Zantas mark", new[] { "Pusheencutangy" }),
    };

    // Shown in a "Special thanks" block.
    static readonly string[] Thanks = {
        "The Everest team",
        "The Celeste modding community",
        "Everyone who played and tested",
    };

    // ---- End of the editable part. ----

    public static void Load() {
        On.Celeste.Credits.CreateCredits += OnCreateCredits;
    }

    public static void Unload() {
        On.Celeste.Credits.CreateCredits -= OnCreateCredits;
    }

    // title: whether the roll starts with the big Celeste logo.
    // polaroids: whether the photos from chapter 7's ending are mixed in.
    // The hook says "object" because CreditNode was private when the hook file was generated;
    // the value really is a List<Credits.CreditNode>, so we cast it back.
    static object OnCreateCredits(On.Celeste.Credits.orig_CreateCredits orig, bool title, bool polaroids) {
        var original = (List<Credits.CreditNode>) orig(title, polaroids);

        // The main-menu Credits screen (OuiCredits) runs in the Overworld scene.
        // The chapter 7 ending runs in a Level, so it keeps the game's own credits.
        if (Engine.Scene is not Overworld) {
            return original;
        }

        CreditsMode mode = ZantasLoaderModule.Settings.Credits;
        if (mode == CreditsMode.AddZantas) {
            return AddCredits(original);
        }
        if (mode == CreditsMode.ReplaceWithZantas) {
            return ReplaceCredits();
        }
        return original;
    }

    // Our credits, then a gap, then the full original roll.
    static List<Credits.CreditNode> AddCredits(List<Credits.CreditNode> original) {
        var nodes = ZantasNodes();
        nodes.Add(new Credits.Break(540f));
        nodes.AddRange(original);
        return nodes;
    }

    // Only our credits. We still end with who made Celeste:
    // Mount Zantas runs on their game, so leaving them out would make it look like ours.
    static List<Credits.CreditNode> ReplaceCredits() {
        var nodes = ZantasNodes();
        nodes.Add(new Credits.Break(540f));
        nodes.Add(new Credits.Thanks("Celeste by", "Maddy Thorson", "Noel Berry", "and the Extremely OK Games team"));
        nodes.Add(new Credits.Break(540f));
        nodes.Add(new Credits.Ending("Thank you for playing", false));
        return nodes;
    }

    static List<Credits.CreditNode> ZantasNodes() {
        var nodes = new List<Credits.CreditNode>();
        nodes.Add(new Credits.Break(270f));
        nodes.Add(new Credits.Thanks(ModTitle));
        nodes.Add(new Credits.Break(128f));
        foreach (var (role, people) in Roles) {
            foreach (string person in people) {
                nodes.Add(new Credits.Role(person, role));
            }
        }
        nodes.Add(new Credits.Break(128f));
        nodes.Add(new Credits.Thanks("Special thanks", Thanks));
        return nodes;
    }
}
