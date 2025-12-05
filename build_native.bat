call "C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Auxiliary\Build\vcvars64.bat"

set NATIVE_PROJECT_DIR=Hartonomous.Native
set BUILD_DIR=%NATIVE_PROJECT_DIR%\build
set DLL_NAME=HartonomousNative.dll
set DLL_PATH=%BUILD_DIR%\bin\Release\%DLL_NAME%

echo --- Configuring CMake ---
cmake -S %NATIVE_PROJECT_DIR% -B %BUILD_DIR% -DCMAKE_BUILD_TYPE=Release
if %errorlevel% neq 0 exit /b %errorlevel%

echo --- Building Hartonomous.Native ---
cmake --build %BUILD_DIR% --config Release
if %errorlevel% neq 0 exit /b %errorlevel%

echo --- Copying native DLL to artifacts folder ---
mkdir artifacts 2>nul
copy /y %DLL_PATH% artifacts\
if %errorlevel% neq 0 exit /b %errorlevel%

echo --- Copying native DLL from artifacts to test output directories ---
set TEST_OUTPUT_BASE_DIR=artifacts\bin
copy /y artifacts\%DLL_NAME% %TEST_OUTPUT_BASE_DIR%\Hartonomous.Core.Tests\Debug\
copy /y artifacts\%DLL_NAME% %TEST_OUTPUT_BASE_DIR%\Hartonomous.Infrastructure.Tests\Debug\
copy /y artifacts\%DLL_NAME% %TEST_OUTPUT_BASE_DIR%\Hartonomous.Data.Tests\Debug\
copy /y artifacts\%DLL_NAME% %TEST_OUTPUT_BASE_DIR%\Hartonomous.API.Tests\Debug\

echo --- Native build and copy complete ---