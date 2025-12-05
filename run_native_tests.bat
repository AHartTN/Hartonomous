call "C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Auxiliary\Build\vcvars64.bat"

set NATIVE_PROJECT_DIR=Hartonomous.Native
set BUILD_DIR=%NATIVE_PROJECT_DIR%\build

echo --- Configuring CMake ---
cmake -S %NATIVE_PROJECT_DIR% -B %BUILD_DIR% -DCMAKE_BUILD_TYPE=Release
if %errorlevel% neq 0 exit /b %errorlevel%

echo --- Building Native Test ---
cmake --build %BUILD_DIR% --config Release --target TestNative
if %errorlevel% neq 0 exit /b %errorlevel%

echo --- Running Native Test ---
%BUILD_DIR%\bin\Release\TestNative.exe
if %errorlevel% neq 0 exit /b %errorlevel%

echo --- Native Tests Passed ---