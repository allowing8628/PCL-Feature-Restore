#Requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This build script requires Windows and PowerShell 7.4+.' }
$work = Join-Path $PSScriptRoot '.work'
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $work,$dist -Force | Out-Null

function Get-VerifiedFile([string]$Uri, [string]$Path, [string]$Sha256) {
    if (-not (Test-Path -LiteralPath $Path)) { Invoke-WebRequest -Uri $Uri -OutFile $Path }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Sha256) {
        throw "SHA-256 mismatch: $Path. Remove the mismatched cached file and retry."
    }
}
$archiveName = [Uri]::EscapeDataString('最新正式版.zip')
$upstream = 'https://raw.githubusercontent.com/Meloong-Git/PCL'
$officialZip = Join-Path $work 'official.zip'
$donorZip = Join-Path $work 'donor.zip'
$dnlibZip = Join-Path $work 'dnlib.zip'
Get-VerifiedFile "$upstream/2.99.0.0/$archiveName" $officialZip '01877F06DF84CBAB376C286A633E76EF7FCA0BB469E26DF97F3B09B5ABD503BC'
Get-VerifiedFile "$upstream/2.14.0.0/$archiveName" $donorZip '5321F0754137FBDD2C5C1420DB868B8E516BCD5D4ED94729D00813F1597C4B46'
Get-VerifiedFile 'https://api.nuget.org/v3-flatcontainer/dnlib/4.5.0/dnlib.4.5.0.nupkg' $dnlibZip '63BC2F9579568204CC8B30FA9F6700A231BCF868A8032E09118887AF3EAFEE58'
Expand-Archive -LiteralPath $officialZip -DestinationPath (Join-Path $work 'official') -Force
Expand-Archive -LiteralPath $donorZip -DestinationPath (Join-Path $work 'donor') -Force
Expand-Archive -LiteralPath $dnlibZip -DestinationPath (Join-Path $work 'dnlib') -Force
$original = Join-Path $work 'official/Plain Craft Launcher 2.exe'
$donor = Join-Path $work 'donor/Plain Craft Launcher 2.exe'
if ((Get-FileHash -LiteralPath $original).Hash -ne '6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386') { throw 'Unexpected official EXE' }
if ((Get-FileHash -LiteralPath $donor).Hash -ne '30EB93C62FC51EDE0898648E67455478A3D5A4F279938AE65AEC77B84CB9703F') { throw 'Unexpected reference EXE' }
$dnlib = Join-Path $work 'dnlib/lib/net6.0/dnlib.dll'
& (Join-Path $PSScriptRoot 'scripts/Restore-Download.ps1') -WorkRoot $work -DnlibPath $dnlib
$references = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | Select-Object -ExpandProperty FullName) + @($dnlib)
if (-not ('RestoreModpack' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'src/RestoreModpack.cs') -ReferencedAssemblies $references -CompilerOptions '/nowarn:1701'
}
$downloadOnly = Join-Path $work 'patched/Plain Craft Launcher 2.exe'
$modpack = Join-Path $work 'modpack.exe'
$output = Join-Path $dist 'Plain Craft Launcher (PCL) Feature Restore.exe'
[RestoreModpack]::Run($downloadOnly,$donor,$modpack)
[RestoreModpack]::RestoreRemaining($modpack,$output)
& (Join-Path $PSScriptRoot 'scripts/Verify.ps1') -Path $output -DnlibPath $dnlib
[ordered]@{
    version='2.99.0.0'; sha256=(Get-FileHash -LiteralPath $output).Hash
    staticValidation='PASS'; launcherExecuted=$false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dist 'build-result.json') -Encoding utf8
Write-Host "Build complete. PCL was not launched. Output: $output"
