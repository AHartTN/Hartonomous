# build_and_test.ps1
# Assuming HartonomousNative.dll is already built and present in the root directory.

$dllName = "HartonomousNative.dll"
$sourceDllPath = Join-Path (Get-Location) $dllName # Assumed to be in the root

# Check if the native DLL exists before proceeding
if (-not (Test-Path $sourceDllPath)) {
    Write-Error "ERROR: Native library '$dllName' not found in root. Please build it first."
    exit 1
}

$testProjects = @(
    "Hartonomous.Core.Tests",
    "Hartonomous.Infrastructure.Tests",
    "Hartonomous.Data.Tests",
    "Hartonomous.API.Tests"
)

Write-Host "--- Building Test Projects ---"
foreach ($project in $testProjects) {
    dotnet build "$project\$project.csproj" --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $project" }
}

Write-Host "--- Copying native DLL to artifacts folder ---"
$artifactsDir = "artifacts"
if (-not (Test-Path $artifactsDir)) { New-Item -Path $artifactsDir -ItemType Directory | Out-Null }
Copy-Item $sourceDllPath -Destination $artifactsDir -Force

Write-Host "--- Copying native DLL from artifacts to test output directories ---"

# Adjusted output path based on inspection: artifacts/bin/<ProjectName>/Release/
foreach ($project in $testProjects) {
    $targetDir = Join-Path "artifacts\bin" $project
    $targetDir = Join-Path $targetDir "Release"
    
    if (Test-Path $targetDir) {
        Write-Host "Copying DLL to $targetDir"
        Copy-Item $sourceDllPath -Destination $targetDir -Force
    } else {
        Write-Warning "Target directory not found: $targetDir"
    }
}

Write-Host "--- Running dotnet tests ---"
# Run tests only for the specified projects to avoid running Android tests etc.
foreach ($project in $testProjects) {
    Write-Host "Testing $project..."
    dotnet test "$project\$project.csproj" --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests failed for $project" }
}

Write-Host "--- Build and Test Complete ---"