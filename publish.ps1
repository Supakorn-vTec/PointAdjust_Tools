$ErrorActionPreference = "Stop"
$out = "d:\VTEC\vtecPoint\release\vtecPoint"
$zip = "d:\VTEC\vtecPoint\release\vtecPoint-win-x64.zip"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

Write-Host "Publishing server..."
dotnet publish "d:\VTEC\vtecPoint\vtecPoint\vtecPoint.csproj" -c Release -r win-x64 --self-contained true -o $out

Write-Host "Publishing launcher..."
dotnet publish "d:\VTEC\vtecPoint\vtecPoint.Launcher\vtecPoint.Launcher.csproj" -c Release -r win-x64 --self-contained true -o $out

Copy-Item "d:\VTEC\vtecPoint\vtecPoint\deploy\*" $out -Force

Write-Host "Creating zip..."
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$out\*" -DestinationPath $zip -Force

Write-Host "Done: $zip"
