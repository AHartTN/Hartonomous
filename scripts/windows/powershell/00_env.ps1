Write-Host "C:\Program Files (x86)\Intel\oneAPI"

$ONEAPI_ROOT = "C:\Program Files (x86)\Intel\oneAPI"

Write-Host "[Before exist check] $ONEAPI_ROOT\setvars-vcvarsall.bat"

if (-Not (Test-Path "$ONEAPI_ROOT\setvars-vcvarsall.bat")) {
    Write-Host "[ERROR] Intel OneAPI's setvars-vcvarsall.bat was not found at $ONEAPI_ROOT"
    exit 1
}

Write-Host "[Before call] $ONEAPI_ROOT\setvars-vcvarsall.bat"

& "$ONEAPI_ROOT\setvars-vcvarsall.bat"

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Failed to run Intel setvars-vcvarsall.bat!"
    exit 1
}
