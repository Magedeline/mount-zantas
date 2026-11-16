using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("MountZantas")]
[assembly: InternalsVisibleTo("MountZantas.CoreChecks")]

namespace MountZantas;

// Which operating system we are on, and the few OS-specific actions the rest of the code needs.
static class Platform {
    public static bool Windows => OperatingSystem.IsWindows();
    public static bool Mac => OperatingSystem.IsMacOS();
    public static bool Linux => OperatingSystem.IsLinux();
    public static string Name => Windows ? "windows" : Mac ? "macos" : "linux";

    // Opens a folder in the system file manager.
    public static void OpenFolder(string path) {
        var psi = Windows ? new ProcessStartInfo("explorer.exe") : Mac ? new ProcessStartInfo("open") : new ProcessStartInfo("xdg-open");
        psi.ArgumentList.Add(path); psi.UseShellExecute = false; Process.Start(psi);
    }

    // Zip extraction drops the executable bit on Linux/macOS, so set it on files we are about to run.
    public static void MakeExecutable(string file) {
        if (OperatingSystem.IsWindows()) return;
        var mode = File.GetUnixFileMode(file); File.SetUnixFileMode(file, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    // The Everest patcher that matches an OS (all of them ship in Everest's main.zip).
    public static string[] MiniInstallerNames(bool windows, bool mac) => windows ? new[] { "MiniInstaller-win64.exe", "MiniInstaller-win.exe" } : mac ? new[] { "MiniInstaller-osx" } : new[] { "MiniInstaller-linux" };
    public static string MiniInstallerPath(string dir) {
        var names = MiniInstallerNames(Windows, Mac);
        return names.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists) ?? Path.Combine(dir, names[0]);
    }
}

// Starts Celeste (vanilla or with Everest) from an install folder.
static class GameLauncher {
    // The file that would be started, or null if it can't be found.
    // Windows: Celeste.exe. Linux: the Celeste launcher in the install folder. macOS: the launcher in Celeste.app/Contents/MacOS
    // (the install folder itself is Celeste.app/Contents/Resources).
    public static string FindExecutable(Install i) {
        if (Platform.Windows) { var w = Path.Combine(i.Path, "Celeste.exe"); return File.Exists(w) ? w : null; }
        if (Platform.Mac) { var m = Path.GetFullPath(Path.Combine(i.Path, "..", "MacOS", "Celeste")); if (File.Exists(m)) return m; }
        var l = Path.Combine(i.Path, "Celeste"); return File.Exists(l) ? l : null;
    }
    public static void Launch(Install i) {
        var exe = FindExecutable(i) ?? throw new Exception("Celeste's launcher wasn't found in " + i.Path);
        Platform.MakeExecutable(exe);
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = i.Path, UseShellExecute = false });
    }
}
