# Builds and packs a Mount Zantas release for Windows, Linux and macOS, on Windows.
#
#     python tools/release/package.py 0.8.0b
#
# Output goes to dist/release-<version>/:
#     MountZantas.exe                       Windows self-update downloads exactly this name
#     MountZantas-<v>-win-x64.zip           exe + SHA256SUMS.txt + LICENSE, NOTICE, PRIVACY
#     MountZantas-<v>-linux-x64.tar.gz      MountZantas (marked executable), icon, .desktop file, README
#     MountZantas-<v>-osx-x64.zip           "Mount Zantas.app" (Intel; runs on Apple Silicon through Rosetta 2)
#     SHA256SUMS.txt                        checksum of MountZantas.exe
#     SHA256SUMS-all.txt                    checksums of every download
#     itch/windows, itch/linux, itch/mac    what gets pushed to each itch.io channel with butler
#
# The tar and zip are written by hand so the programs keep their Unix "executable" bit, which Windows can't store on disk.
# The app icons (Linux PNG, macOS .icns) are drawn from the Zantas mark by the freshly built MountZantas.exe.
import hashlib
import os
import re
import shutil
import struct
import subprocess
import sys
import tarfile
import zipfile

if len(sys.argv) != 2:
    sys.exit("usage: python tools/release/package.py <version, e.g. 0.8.0b>")

VERSION = sys.argv[1]
# macOS only accepts numbers in an app's version, so "0.8.0b" becomes "0.8.0" there.
BUNDLE_VERSION = re.match(r"\d+(\.\d+)*", VERSION).group(0)

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEMPLATES = os.path.join(REPO, "tools", "release")
OUT = os.path.join(REPO, "dist", "release-" + VERSION)
RAW = os.path.join(OUT, "raw")


def run(*args):
    print(">", " ".join(args))
    subprocess.run(args, cwd=REPO, check=True)


def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def template(name):
    with open(os.path.join(TEMPLATES, name), encoding="utf-8") as f:
        return f.read()


def write_text(path, text):
    with open(path, "w", newline="\n", encoding="utf-8") as f:
        f.write(text)


def check_version_in_csproj(path):
    with open(path, encoding="utf-8") as f:
        text = f.read()
    if f"<InformationalVersion>{VERSION}</InformationalVersion>" not in text:
        sys.exit(f"{path} does not say <InformationalVersion>{VERSION}</InformationalVersion>; bump it first.")


# ---------- build ----------
check_version_in_csproj(os.path.join(REPO, "MountZantas.csproj"))
check_version_in_csproj(os.path.join(REPO, "desktop", "MountZantas.Desktop.csproj"))
if os.path.exists(OUT):
    shutil.rmtree(OUT)
run("dotnet", "publish", "MountZantas.csproj", "-c", "Release", "-o", os.path.join(RAW, "win"))
for rid in ["linux-x64", "osx-x64"]:
    run("dotnet", "publish", "desktop/MountZantas.Desktop.csproj", "-c", "Release", "-r", rid,
        "-p:DebugType=none", "-o", os.path.join(RAW, rid))

# ---------- icons from the Zantas mark ----------
win_exe_built = os.path.join(RAW, "win", "MountZantas.exe")
icons = os.path.join(RAW, "icons")
os.makedirs(icons)
for size in [128, 256, 512, 1024]:
    run(win_exe_built, "--make-mark", os.path.join(icons, f"mark{size}.png"), str(size))


def make_icns(path):
    # An .icns file is a header plus PNG images tagged with their size (ic07 = 128, ic08 = 256, ic09 = 512, ic10 = 1024).
    parts = b""
    for tag, size in [(b"ic07", 128), (b"ic08", 256), (b"ic09", 512), (b"ic10", 1024)]:
        with open(os.path.join(icons, f"mark{size}.png"), "rb") as f:
            png = f.read()
        parts += tag + struct.pack(">I", 8 + len(png)) + png
    with open(path, "wb") as f:
        f.write(b"icns" + struct.pack(">I", 8 + len(parts)) + parts)


# ---------- Windows ----------
exe = os.path.join(OUT, "MountZantas.exe")
shutil.copy2(win_exe_built, exe)
write_text(os.path.join(OUT, "SHA256SUMS.txt"), f"{sha(exe)}  MountZantas.exe\n")
win_zip = os.path.join(OUT, f"MountZantas-{VERSION}-win-x64.zip")
with zipfile.ZipFile(win_zip, "w", zipfile.ZIP_DEFLATED) as z:
    z.write(exe, "MountZantas.exe")
    z.write(os.path.join(OUT, "SHA256SUMS.txt"), "SHA256SUMS.txt")
    for name in ["LICENSE", "NOTICE.md", "PRIVACY.md"]:
        z.write(os.path.join(REPO, name), name)

# ---------- Linux ----------
lin_dir = os.path.join(OUT, "itch", "linux")
os.makedirs(lin_dir)
shutil.copy2(os.path.join(RAW, "linux-x64", "MountZantas"), os.path.join(lin_dir, "MountZantas"))
shutil.copy2(os.path.join(TEMPLATES, "linux", "mount-zantas.desktop"), lin_dir)
shutil.copy2(os.path.join(icons, "mark256.png"), os.path.join(lin_dir, "mount-zantas.png"))
write_text(os.path.join(lin_dir, "README.txt"), template("linux/README.txt").replace("{VERSION}", VERSION))
lin_tar = os.path.join(OUT, f"MountZantas-{VERSION}-linux-x64.tar.gz")
with tarfile.open(lin_tar, "w:gz") as t:
    for name in sorted(os.listdir(lin_dir)):
        info = t.gettarinfo(os.path.join(lin_dir, name), f"MountZantas-{VERSION}/{name}")
        info.mode = 0o755 if name == "MountZantas" else 0o644
        info.uid = info.gid = 0
        info.uname = info.gname = ""
        with open(os.path.join(lin_dir, name), "rb") as f:
            t.addfile(info, f)

# ---------- macOS (Intel .app) ----------
mac_dir = os.path.join(OUT, "itch", "mac")
app = os.path.join(mac_dir, "Mount Zantas.app")
os.makedirs(os.path.join(app, "Contents", "MacOS"))
os.makedirs(os.path.join(app, "Contents", "Resources"))
shutil.copy2(os.path.join(RAW, "osx-x64", "MountZantas"), os.path.join(app, "Contents", "MacOS", "MountZantas"))
make_icns(os.path.join(app, "Contents", "Resources", "MountZantas.icns"))
write_text(os.path.join(app, "Contents", "Info.plist"), template("mac/Info.plist").replace("{VERSION}", BUNDLE_VERSION))
write_text(os.path.join(mac_dir, "README.txt"), template("mac/README.txt").replace("{VERSION}", VERSION))
mac_zip = os.path.join(OUT, f"MountZantas-{VERSION}-osx-x64.zip")
with zipfile.ZipFile(mac_zip, "w", zipfile.ZIP_DEFLATED) as z:
    for root, _, files in os.walk(mac_dir):
        for name in sorted(files):
            full = os.path.join(root, name)
            rel = os.path.relpath(full, mac_dir).replace("\\", "/")
            info = zipfile.ZipInfo.from_file(full, rel)
            # Mark the entries as made on Unix so macOS honours the permission bits below.
            info.create_system = 3
            mode = 0o755 if rel.endswith("MacOS/MountZantas") else 0o644
            info.external_attr = (0o100000 | mode) << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            with open(full, "rb") as f:
                z.writestr(info, f.read())

# ---------- itch.io Windows channel (pushed as the zip file itself) ----------
os.makedirs(os.path.join(OUT, "itch", "windows"))
shutil.copy2(win_zip, os.path.join(OUT, "itch", "windows"))

# ---------- checksums of every download ----------
lines = [f"{sha(p)}  {os.path.basename(p)}\n" for p in [exe, win_zip, lin_tar, mac_zip]]
write_text(os.path.join(OUT, "SHA256SUMS-all.txt"), "".join(lines))
print("".join(lines))
print("Done:", OUT)
