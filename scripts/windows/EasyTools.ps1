# Helper behind the double-click .bat files in the repository root.
# Written for Windows PowerShell 5.1 (built into Windows), so: ASCII only, no newer syntax.
#
#   -Action Test     copy the mod into your game's mods folder so you can try it
#   -Action Remove   delete that test copy again
#   -Action Upload   upload the mod to the Steam Workshop with MegaCrit's official uploader

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Test", "Remove", "Upload")]
    [string]$Action
)

$ErrorActionPreference = "Stop"
$ModId = "TextSizeSetting"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Workshop = Join-Path $Root "workshop"
$Content = Join-Path $Workshop "content"
$UploaderDir = Join-Path $Root "tools\ModUploader"
$UploaderReleases = "https://github.com/megacrit/sts2-mod-uploader/releases"

function Say([string]$Text, [string]$Color = "Gray") { Write-Host $Text -ForegroundColor $Color }
function Good([string]$Text) { Say $Text "Green" }
function Problem([string]$Text) { Say $Text "Red" }
function Heading([string]$Text) { Write-Host ""; Say $Text "Cyan"; Say ("-" * $Text.Length) "Cyan" }

# ---------------------------------------------------------------- finding the game

function Get-SteamLibraries {
    $steamRoots = @()
    foreach ($key in @("HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam")) {
        try {
            $item = Get-ItemProperty -Path $key -ErrorAction Stop
            if ($item.SteamPath) { $steamRoots += ($item.SteamPath -replace "/", "\") }
            if ($item.InstallPath) { $steamRoots += $item.InstallPath }
        } catch { }
    }
    $steamRoots += "C:\Program Files (x86)\Steam"

    $libraries = @()
    foreach ($steam in ($steamRoots | Select-Object -Unique)) {
        if (-not (Test-Path $steam)) { continue }
        $libraries += $steam
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            foreach ($match in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libraries += ($match.Groups[1].Value -replace "\\\\", "\")
            }
        }
    }
    return $libraries | Select-Object -Unique
}

function Find-GameFolder {
    foreach ($library in Get-SteamLibraries) {
        $candidate = Join-Path $library "steamapps\common\Slay the Spire 2"
        if (Test-Path (Join-Path $candidate "SlayTheSpire2.exe")) { return $candidate }
        if (Test-Path $candidate) { return $candidate }
    }

    Say "I couldn't find Slay the Spire 2 automatically."
    Say "Tip: in Steam, right-click the game > Manage > Browse local files to see where it is."
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.Description = "Select your 'Slay the Spire 2' game folder"
        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return $dialog.SelectedPath }
    } catch {
        $typed = Read-Host "Paste the game folder path here and press Enter"
        if ($typed) { return $typed.Trim('"') }
    }
    return $null
}

# ---------------------------------------------------------------- actions

function Assert-ModBuilt {
    if (-not (Test-Path (Join-Path $Content "$ModId.dll")) -or -not (Test-Path (Join-Path $Content "$ModId.json"))) {
        throw "The mod files are missing from workshop\content. Download the repository again (Code > Download ZIP)."
    }
}

function Install-TestCopy {
    Heading "Install the mod into your game (for testing)"
    Assert-ModBuilt
    $game = Find-GameFolder
    if (-not $game) { throw "No game folder chosen." }

    $target = Join-Path $game "mods\$ModId"
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $Content "*") $target

    Good "Installed to: $target"
    Say ""
    Say "Now:"
    Say "  1. Start Slay the Spire 2. If it asks whether to load mods, choose yes."
    Say "  2. Open Settings > General and look for the 'Text Size' row."
    Say "  3. Use the - and + buttons and watch the text change."
    Say ""
    Say "When you're done testing, double-click 'Remove test copy from my game.bat'" "Yellow"
    Say "(otherwise the game will see the mod twice once you subscribe on the Workshop)." "Yellow"
}

function Remove-TestCopy {
    Heading "Remove the test copy from your game"
    $game = Find-GameFolder
    if (-not $game) { throw "No game folder chosen." }

    $target = Join-Path $game "mods\$ModId"
    if (Test-Path $target) {
        Remove-Item $target -Recurse -Force
        Good "Removed: $target"
    } else {
        Good "Nothing to remove; there's no test copy in $game\mods."
    }
}

function Get-Uploader {
    $exe = Join-Path $UploaderDir "ModUploader.exe"
    if (Test-Path $exe) { return $exe }

    Say "Downloading MegaCrit's official Workshop uploader..."
    $platform = "win-x64"
    if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { $platform = "win-arm64" }
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/megacrit/sts2-mod-uploader/releases/latest" -Headers @{ "User-Agent" = "TextSizeSetting" }
        $asset = $release.assets | Where-Object { $_.name -eq "ModUploader-$platform.zip" } | Select-Object -First 1
        if (-not $asset) { throw "No ModUploader-$platform.zip in the latest release." }

        $zip = Join-Path $env:TEMP "ModUploader-$platform.zip"
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing
        New-Item -ItemType Directory -Force -Path $UploaderDir | Out-Null
        Expand-Archive -Path $zip -DestinationPath $UploaderDir -Force
        Remove-Item $zip -Force
    } catch {
        Problem "Automatic download didn't work: $($_.Exception.Message)"
        Say ""
        Say "Please do this instead:"
        Say "  1. A web page will open. Download ModUploader-$platform.zip from the newest release."
        Say "  2. Unzip it into this folder:  $UploaderDir"
        Say "  3. Double-click 'Upload to Steam Workshop.bat' again."
        Start-Process $UploaderReleases
        return $null
    }

    $found = Get-ChildItem -Path $UploaderDir -Recurse -Filter "ModUploader.exe" | Select-Object -First 1
    if (-not $found) { throw "Downloaded the uploader but couldn't find ModUploader.exe inside it." }
    return $found.FullName
}

function Set-Visibility([string]$Visibility) {
    $path = Join-Path $Workshop "workshop.json"
    $json = Get-Content $path -Raw
    $json = [regex]::Replace($json, '"visibility"\s*:\s*"[^"]*"', "`"visibility`": `"$Visibility`"")
    [IO.File]::WriteAllText($path, $json, (New-Object System.Text.UTF8Encoding($false)))
}

function Invoke-Upload {
    Heading "Upload Text Size Setting to the Steam Workshop"
    Assert-ModBuilt

    if (-not (Get-Process -Name "steam" -ErrorAction SilentlyContinue)) {
        Problem "Steam isn't running. Start Steam, log in, then double-click this file again."
        return
    }

    $idFile = Join-Path $Workshop "mod_id.txt"
    $isUpdate = Test-Path $idFile
    if ($isUpdate) {
        Say "This will UPDATE your existing Workshop item ($((Get-Content $idFile -Raw).Trim()))."
    } else {
        Say "This will create a NEW Workshop item on your Steam account."
        Say ""
        Say "Have you uploaded this mod before? Then STOP: copy the file workshop\mod_id.txt from" "Yellow"
        Say "the folder you uploaded from into this folder's workshop folder first. Without it," "Yellow"
        Say "Steam gets a second, duplicate copy of the mod instead of an update." "Yellow"
        $confirm = (Read-Host "Type NEW to create a new Workshop item, or anything else to stop").Trim()
        if ($confirm -ne "NEW") { Say "Stopped. Nothing was uploaded."; return }
    }

    Say ""
    Say "Who should be able to see it?"
    Say "  1 = Only me (recommended the first time, so you can check it works)"
    Say "  2 = Everyone (public, so other players can subscribe)"
    $choice = ""
    while ($choice -ne "1" -and $choice -ne "2") { $choice = (Read-Host "Type 1 or 2 and press Enter").Trim() }
    if ($choice -eq "1") { Set-Visibility "private" } else { Set-Visibility "public" }

    $exe = Get-Uploader
    if (-not $exe) { return }

    Say ""
    Say "Uploading... (Steam may take a minute)"
    # The uploader needs to run from its own folder so Steam finds steam_appid.txt next to it.
    Push-Location (Split-Path -Parent $exe)
    try {
        & $exe upload -w $Workshop
        $code = $LASTEXITCODE
    } finally {
        Pop-Location
    }

    if ($code -ne 0) {
        Problem "The upload failed (exit code $code). The messages above say why."
        Say "A log was saved to: $(Join-Path (Split-Path -Parent $exe) 'mod-uploader.log')"
        return
    }

    $id = ""
    if (Test-Path $idFile) { $id = (Get-Content $idFile -Raw).Trim() }
    $url = "https://steamcommunity.com/sharedfiles/filedetails/?id=$id"
    $legal = "https://steamcommunity.com/sharedfiles/workshoplegalagreement"

    Write-Host ""
    Good "Done! Your mod is on the Steam Workshop:"
    Good "  $url"
    Say ""
    Say "Important:" "Yellow"
    Say "  * Keep this folder. The file workshop\mod_id.txt remembers your Workshop item," "Yellow"
    Say "    so future uploads update it instead of creating a duplicate." "Yellow"
    Say "  * If this is your first Workshop upload ever, Steam hides the item until you accept" "Yellow"
    Say "    the Workshop agreement: $legal" "Yellow"
    Say "  * Private items only show up when you're logged in, so the page opens in the Steam app." "Yellow"
    Say "    If you open the link in a web browser instead, log in to Steam there first." "Yellow"
    if ($choice -eq "1") {
        Say "  * It's private for now. Subscribe to it, try it in game, then run this again and pick 2." "Yellow"
    }
    # Open in the Steam client (already logged in) rather than a browser, where a private
    # item shows "There was a problem accessing the item" unless you're signed in.
    if ($id) { Start-Process "steam://url/CommunityFilePage/$id" }
}

try {
    switch ($Action) {
        "Test" { Install-TestCopy }
        "Remove" { Remove-TestCopy }
        "Upload" { Invoke-Upload }
    }
} catch {
    Problem "Something went wrong: $($_.Exception.Message)"
}
