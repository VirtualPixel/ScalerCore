param(
    [string]$Version,
    [string]$DllPath,
    [string]$RepoRoot
)

$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
$buildZipDir = Join-Path $RepoRoot "BuildZip\ScalerCore"
$enc = New-Object System.Text.UTF8Encoding $false

function Update-ManifestVersion([string]$path, [string]$version) {
    $content = [System.IO.File]::ReadAllText($path, $enc)
    $content = $content -replace '"version_number":\s*"[^"]*"', "`"version_number`": `"$version`""
    [System.IO.File]::WriteAllText($path, $content, $enc)
}

# Ensure build dir exists
New-Item -ItemType Directory -Path $buildZipDir -Force | Out-Null

# Update manifest version from csproj Version (single source of truth)
Update-ManifestVersion (Join-Path $RepoRoot "manifest.json") $Version

# Copy DLL and release assets into build zip folder
Copy-Item -LiteralPath $DllPath -Destination $buildZipDir -Force
Copy-Item -LiteralPath (Join-Path $RepoRoot "README.md") -Destination $buildZipDir -Force
Copy-Item -LiteralPath (Join-Path $RepoRoot "CHANGELOG.md") -Destination $buildZipDir -Force
Copy-Item -LiteralPath (Join-Path $RepoRoot "manifest.json") -Destination $buildZipDir -Force

# Icon: copy if exists, skip if not (allows building before art is ready)
$iconSrc = Join-Path $RepoRoot "icon.png"
if (Test-Path $iconSrc) {
    Copy-Item -LiteralPath $iconSrc -Destination (Join-Path $buildZipDir "icon.png") -Force
} else {
    Write-Host "SKIP: icon.png not found at $iconSrc - zip will be missing icon (add it before uploading)"
}

# Wiki: copy directory if it exists (Thunderstore wiki pages)
$wikiSrc = Join-Path $RepoRoot "wiki"
if (Test-Path $wikiSrc) {
    $wikiDst = Join-Path $buildZipDir "wiki"
    if (Test-Path $wikiDst) { Remove-Item -Recurse -Force $wikiDst }
    Copy-Item -Recurse -LiteralPath $wikiSrc -Destination $wikiDst -Force
    Write-Host "Copied wiki/ into build zip"
}

# Create zip (exclude any existing zip, include wiki subdirectory)
$zipPath = Join-Path $buildZipDir "ScalerCore.zip"
$filesToZip = @()
$filesToZip += Get-ChildItem -LiteralPath $buildZipDir -File | Where-Object { $_.Extension -ne ".zip" }
$wikiDstDir = Join-Path $buildZipDir "wiki"
if (Test-Path $wikiDstDir) {
    $filesToZip += Get-ChildItem -LiteralPath $wikiDstDir -File -Recurse
}
if ($filesToZip.Count -gt 0) {
    Compress-Archive -Path $filesToZip.FullName -DestinationPath $zipPath -Force
    Write-Host "Packaged v$Version -> $zipPath"
} else {
    Write-Host "WARNING: No files to package"
}

# Clear BepInEx log for a clean test run (silently skip if locked)
try { [System.IO.File]::WriteAllText("$env:APPDATA\com.kesomannen.gale\repo\profiles\Development\BepInEx\LogOutput.log", "", $enc) }
catch { }
