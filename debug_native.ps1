# debug_native.ps1
$dllName = "HartonomousNative.dll"
$sourceDllPath = Join-Path (Get-Location) $dllName

Write-Host "--- Building NativeDebug App ---"
dotnet build Hartonomous.NativeDebug\Hartonomous.NativeDebug.csproj --configuration Debug

Write-Host "--- Copying Native DLL ---"
$targetDir = "artifacts\bin\Hartonomous.NativeDebug\Debug"
if (-not (Test-Path $targetDir)) { throw "Target dir not found: $targetDir" }
Copy-Item $sourceDllPath -Destination $targetDir -Force

Write-Host "--- Running NativeDebug App ---"
$exePath = Join-Path $targetDir "Hartonomous.NativeDebug.exe"
& $exePath