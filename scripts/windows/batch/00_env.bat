@echo off
powershell -Command "Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force; & '$0' %*"

REM =======================================================================================================
REM {Project Root}/scripts/windows/batch/00_env.bat
REM =======================================================================================================

@REM echo [SETUP] Looking for Visual Studio
@REM set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"

@REM IF NOT EXIST "%VSWHERE%" (
@REM     echo [ERROR] Could not find vswhere.exe
@REM     exit /b 1
@REM )

@REM for /f "usebackq tokens=*" %%i in ('"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath') do
@REM (
@REM     set "VS_PATH=%%i"
@REM )

@REM if "%VS_PATH%"=="" {
@REM     echo [ERROR] No suitable Visual Studio installation was found (VS_PATH parse failure)
@REM     exit /b 1
@REM }

@REM echo [SETUP] Visual Studio found at: %VS_PATH%

@REM call "%VS_PATH%\VC\Auxiliary\Build\vcvars64.bat"

@REM IF %ERRORLEVEL% NEQ 0 (
@REM     echo [ERROR] Failed to run vcvars64.bat!
@REM     exit /b 1
@REM )

echo %ProgramFiles(x86)%\Intel\oneAPI

set "ONEAPI_ROOT=%ProgramFiles(x86)%\Intel\oneAPI"

echo [Before exist check] %ONEAPI_ROOT%\setvars-vcvarsall.bat

IF NOT EXIST "%ONEAPI_ROOT%\setvars-vcvarsall.bat" (
    echo [ERROR] Intel OneAPI's setvars-vcvarsall.bat was not found at %ONEAPI_ROOT%
    exit /b 1
)

echo [Before call] %ONEAPI_ROOT%\setvars-vcvarsall.bat

call "%ONEAPI_ROOT%\setvars-vcvarsall.bat"

echo [After call] %ONEAPI_ROOT%\setvars-vcvarsall.bat

IF %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Failed to run Intel setvars-vcvarsall.bat!
    exit /b 1
)
