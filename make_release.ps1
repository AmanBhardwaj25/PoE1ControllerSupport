# Builds the Nexus release zip: release\PoEController-<version>.zip
#
#   powershell -ExecutionPolicy Bypass -File make_release.ps1
#
# Contents (extract into the game folder): the BepInEx 5 loader and core from the official zip in tools\bepinex
# (without its changelog and .xml docs), the mod built WITHOUT logging (NoModLogging=true), SDL2.dll, the glyphs and
# "How to install.txt". Nothing from the game folder is used, so local configs and logs never end up in the zip.
# The development build in the game folder is not touched.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = $PSScriptRoot
$project = Join-Path $root 'src\PoEController\PoEController.csproj'
$bepinexZip = Join-Path $root 'tools\bepinex\BepInEx_win_x64_5.4.23.2.zip'
$sdl = Join-Path $root 'tools\sdl2\SDL2.dll'
$glyphs = Join-Path $root 'assets\glyphs'
$releaseDir = Join-Path $root 'release'
$stage = Join-Path $releaseDir 'stage'
$buildOut = Join-Path $releaseDir 'build'

foreach ($p in @($project, $bepinexZip, $sdl, $glyphs)) {
    if (-not (Test-Path $p)) { throw "Missing: $p" }
}

# Version from Plugin.cs
$pluginCs = Get-Content (Join-Path $root 'src\PoEController\Plugin.cs') -Raw
if ($pluginCs -notmatch 'const string Version = "([^"]+)"') { throw 'Version not found in Plugin.cs' }
$version = $Matches[1]
Write-Host "PoE Controller $version"

# Clean
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $buildOut) { Remove-Item $buildOut -Recurse -Force }
New-Item -ItemType Directory -Force $stage, $buildOut | Out-Null

# 1. Build the mod without logging (also skips deploying into the game folder)
& dotnet build $project -c Release --configfile (Join-Path $root 'nuget.config') -p:NoModLogging=true "-p:OutputPath=$buildOut/" -nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$dll = Join-Path $buildOut 'PoEController.dll'
if (-not (Test-Path $dll)) { throw "Build output missing: $dll" }

# 2. BepInEx loader + core from the official zip
$zip = [System.IO.Compression.ZipFile]::OpenRead($bepinexZip)
try {
    foreach ($e in $zip.Entries) {
        $name = $e.FullName
        if ($name.EndsWith('/') -or $name -eq 'changelog.txt' -or $name.EndsWith('.xml')) { continue }
        $target = Join-Path $stage ($name -replace '/', '\')
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $target, $true)
    }
}
finally { $zip.Dispose() }

# 3. The mod
$pluginDir = Join-Path $stage 'BepInEx\plugins\PoEController'
New-Item -ItemType Directory -Force $pluginDir | Out-Null
Copy-Item $dll $pluginDir
Copy-Item $sdl $pluginDir
Get-ChildItem $glyphs -Directory | ForEach-Object {
    $dest = Join-Path $pluginDir ('glyphs\' + $_.Name)
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $_.FullName '*.png') $dest
}

# 4. How to install.txt (plain ASCII, no long dashes)
$install = @"
PoE Controller $version
Controller support for Pillars of Eternity on PC (Steam and GOG) and Steam Deck.


INSTALL ON WINDOWS (STEAM OR GOG)

1. Open the game folder, the one that contains PillarsOfEternity.exe.
   Steam: right-click the game, Manage, Browse local files.
   GOG Galaxy: open the game, the settings button, Manage installation, Show folder.
2. Copy everything from this zip into that folder.
   winhttp.dll must end up right next to PillarsOfEternity.exe.
3. Start the game and pick up your controller.


INSTALL ON STEAM DECK

1. In Desktop Mode, open the game folder (Steam, right-click the game, Manage, Browse local files).
   Usually: /home/deck/.local/share/Steam/steamapps/common/Pillars of Eternity
2. Copy everything from this zip into that folder.
3. Back in Game Mode, open the game's page, the settings button, Properties, General, Launch Options, and enter:
   WINEDLLOVERRIDES="winhttp=n,b" %command%
4. On the game's page, open the controller settings, the current layout, Community layouts,
   and pick "PoE Controller mod (Deck)". Any Gamepad layout works too.
   Keyboard and mouse layouts hide the controller from the mod.
5. Start the game.


SETTINGS

Press View and Menu together (Back and Start) in the game to open the controller settings.
There you can switch between Console style (the default) and PC layout, and change everything else.


UNINSTALL

Delete the folder BepInEx\plugins\PoEController.
To remove BepInEx as well, delete winhttp.dll, doorstop_config.ini, .doorstop_version and the BepInEx folder.


CREDITS

BepInEx (LGPL 2.1), SDL2 (zlib license), button glyphs by Kenney (www.kenney.nl, CC0).
"@
if ($install.IndexOf([char]0x2013) -ge 0 -or $install.IndexOf([char]0x2014) -ge 0) { throw 'Long dash found in How to install.txt' }
[System.IO.File]::WriteAllText((Join-Path $stage 'How to install.txt'), ($install -replace "`r?`n", "`r`n"), [System.Text.Encoding]::ASCII)

# 5. Zip with forward slashes (extracts the same on Windows, 7-Zip and Linux / Steam Deck)
$out = Join-Path $releaseDir "PoEController-$version.zip"
if (Test-Path $out) { Remove-Item $out -Force }
$archive = [System.IO.Compression.ZipFile]::Open($out, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $prefix = $stage.TrimEnd('\') + '\'
    Get-ChildItem $stage -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
        $entry = $_.FullName.Substring($prefix.Length) -replace '\\', '/'
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $archive.Dispose() }

Remove-Item $stage -Recurse -Force
Remove-Item $buildOut -Recurse -Force
$size = [math]::Round((Get-Item $out).Length / 1MB, 2)
Write-Host "Done: $out ($size MB)"
