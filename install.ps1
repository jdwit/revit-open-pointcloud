# Installs Open Point Cloud for the current user, for each Revit version (2025-2027) on this computer.
# Run it when Revit does not run. Use it from the release zip, or build first: dotnet build -c Release src/OpenPointCloud -p:RevitVersion=<year>
# With -Uninstall, the script removes the add-in.
param([int[]]$Versions = @(2025, 2026, 2027), [switch]$Uninstall)

if (Get-Process Revit -ErrorAction SilentlyContinue) { Write-Error "Close Revit first."; exit 1 }
$done = 0
foreach ($v in $Versions) {
  $addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$v"
  if ($Uninstall) {
    if (Test-Path (Join-Path $addins "OpenPointCloud.addin")) {
      Remove-Item (Join-Path $addins "OpenPointCloud.addin")
      Remove-Item (Join-Path $addins "OpenPointCloud") -Recurse -ErrorAction SilentlyContinue
      "Revit $v`: removed"
    }
    continue
  }
  if (-not (Test-Path "C:\Program Files\Autodesk\Revit $v\Revit.exe")) { continue }
  # Release zip: one folder per Revit version next to this script. Source tree: the build output.
  $source = Join-Path $PSScriptRoot "$v"
  if (-not (Test-Path (Join-Path $source "OpenPointCloud.dll"))) { $source = Join-Path $PSScriptRoot "src\OpenPointCloud\bin\Release\$v" }
  if (-not (Test-Path (Join-Path $source "OpenPointCloud.dll"))) { Write-Warning "Revit $v`: no build in $source"; continue }
  $target = Join-Path $addins "OpenPointCloud"
  New-Item -ItemType Directory -Force $target | Out-Null
  Copy-Item (Join-Path $source "OpenPointCloud.dll"), (Join-Path $source "OpenPointCloud.deps.json") $target -Force
  Copy-Item (Join-Path $source "OpenPointCloud.addin") $addins -Force
  # Files from a downloaded zip have a "from the internet" mark. Remove it from the installed files.
  Get-ChildItem $target, (Join-Path $addins "OpenPointCloud.addin") | Unblock-File
  "Revit $v`: installed in $addins"
  $done++
}
if (-not $Uninstall -and $done -eq 0) { Write-Error "No Revit 2025-2027 with a build found."; exit 1 }
