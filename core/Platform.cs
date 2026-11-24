using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("MountZantas")]
[assembly: InternalsVisibleTo("MountZantas.CoreChecks")]
[assembly: InternalsVisibleTo("MountZantas.Desktop")]

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
        psi.ArgumentList.Add(path);
        psi.UseShellExecute = false;
        Process.Start(psi);
    }

    // Shows a file selected in the file manager (falls back to opening its folder where that isn't supported).
    public static void Reveal(string file) {
        if (Windows) {
            Process.Start("explorer.exe", $"/select,\"{file}\"");
            return;
        }
        if (Mac) {
            var psi = new ProcessStartInfo("open") { UseShellExecute = false };
            psi.ArgumentList.Add("-R");
            psi.ArgumentList.Add(file);
            Process.Start(psi);
            return;
        }
        OpenFolder(Path.GetDirectoryName(file));
    }

    // Opens an https link in the default browser.
    public static void OpenUrl(string url) {
        if (url == null || !url.StartsWith("https://", StringComparison.Ordinal)) return;
        if (Windows) {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return;
        }
        var psi = new ProcessStartInfo(Mac ? "open" : "xdg-open") { UseShellExecute = false };
        psi.ArgumentList.Add(url);
        Process.Start(psi);
    }

    // Moves a file or folder to the Recycle Bin / Trash instead of deleting it.
    public static void Trash(string path) {
        bool dir = Directory.Exists(path);
        if (!dir && !File.Exists(path)) return;
        if (Windows) {
            if (dir) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return;
        }
        if (Linux) {   // gio knows the desktop's trash rules (other drives, permissions); fall back to the freedesktop.org layout
            try {
                var psi = new ProcessStartInfo("gio") { UseShellExecute = false, RedirectStandardError = true };
                psi.ArgumentList.Add("trash");
                psi.ArgumentList.Add(path);
                using var p = Process.Start(psi);
                p.WaitForExit(15000);
                if (p.HasExited && p.ExitCode == 0) return;
            } catch { }
            var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            var files = Path.Combine(data, "Trash", "files");
            var info = Path.Combine(data, "Trash", "info");
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);
            var name = UniqueName(files, Path.GetFileName(path.TrimEnd('/')));
            File.WriteAllText(
                Path.Combine(info, name + ".trashinfo"),
                $"[Trash Info]\nPath={Uri.EscapeDataString(Path.GetFullPath(path)).Replace("%2F", "/")}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
            Move(path, Path.Combine(files, name), dir);
            return;
        }
        var trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
        Directory.CreateDirectory(trash);
        Move(path, Path.Combine(trash, UniqueName(trash, Path.GetFileName(path.TrimEnd('/')))), dir);
    }
    static void Move(string from, string to, bool dir) {
        if (dir) Directory.Move(from, to);
        else File.Move(from, to);
    }
    static string UniqueName(string folder, string name) {
        if (!File.Exists(Path.Combine(folder, name)) && !Directory.Exists(Path.Combine(folder, name))) return name;
        var n = Path.GetFileNameWithoutExtension(name);
        var e = Path.GetExtension(name);
        for (int i = 2; ; i++) {
            var c = $"{n} ({i}){e}";
            if (!File.Exists(Path.Combine(folder, c)) && !Directory.Exists(Path.Combine(folder, c))) return c;
        }
    }

    // Zip extraction drops the executable bit on Linux/macOS, so set it on files we are about to run.
    public static void MakeExecutable(string file) {
        if (OperatingSystem.IsWindows()) return;
        var mode = File.GetUnixFileMode(file);
        File.SetUnixFileMode(
            file,
            mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
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
        if (Platform.Windows) {
            var w = Path.Combine(i.Path, "Celeste.exe");
            return File.Exists(w) ? w : null;
        }
        if (Platform.Mac) {
            var m = Path.GetFullPath(Path.Combine(i.Path, "..", "MacOS", "Celeste"));
            if (File.Exists(m)) return m;
        }
        var l = Path.Combine(i.Path, "Celeste");
        return File.Exists(l) ? l : null;
    }
    // Returns the started game process (Mount Zantas's loading screen follows it).
    public static Process Launch(Install i, string args = null) {
        var exe = FindExecutable(i) ?? throw new Exception("Celeste's launcher wasn't found in " + i.Path);
        Platform.MakeExecutable(exe);
        var psi = new ProcessStartInfo(exe) { WorkingDirectory = i.Path, UseShellExecute = false };
        if (!string.IsNullOrWhiteSpace(args))
            foreach (var a in args.Split(' ', StringSplitOptions.RemoveEmptyEntries)) psi.ArgumentList.Add(a);
        return Process.Start(psi);
    }
}
