# Builds Zantas Loader and packs loader/ZantasLoader.zip (everest.yaml + ZantasLoader.dll + Dialog).
# Mount Zantas embeds that zip and installs it into Mods/ next to Everest, so run this before publishing the app
# whenever the loader changes, and commit the new zip.
#
# Needs an Everest (core) install of Celeste to compile against:
#     ./loader/build.ps1                                  # uses ../celeste next to the repository
#     ./loader/build.ps1 -Celeste "D:\Games\Celeste"      # or point it somewhere else
param(
    [string]$Celeste = ""
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot

$buildArgs = @("build", "$here/ZantasLoader.csproj", "-c", "Release", "-o", "$here/out/bin")
if ($Celeste -ne "") {
    $buildArgs += "-p:CelestePrefix=$Celeste"
}
dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "Build failed"
}

# Lay the mod out exactly as Everest expects it inside the zip.
$stage = "$here/out/stage"
if (Test-Path $stage) {
    Remove-Item $stage -Recurse -Force
}
New-Item -ItemType Directory -Force "$stage/Dialog" | Out-Null
Copy-Item "$here/everest.yaml" $stage
Copy-Item "$here/out/bin/ZantasLoader.dll" $stage
Copy-Item "$here/Dialog/English.txt" "$stage/Dialog"

$zip = "$here/ZantasLoader.zip"
if (Test-Path $zip) {
    Remove-Item $zip
}
# Not Compress-Archive: on Windows PowerShell it stores "Dialog\English.txt" with a backslash, which Everest can't find.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $stage -Recurse -File) {
        $entryName = $file.FullName.Substring((Resolve-Path $stage).Path.Length + 1).Replace("\", "/")
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName) | Out-Null
    }
} finally {
    $archive.Dispose()
}
Write-Host "Packed $zip"
