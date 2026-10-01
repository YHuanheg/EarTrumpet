@echo off
rem ---------------------------------------------------------------------------
rem Minimal-path build of the C# EarTrumpet: x86 Release exe only, no MSIX.
rem
rem Already prepared on this machine:
rem   * MSBuild from Visual Studio Build Tools (vswhere fallback if not hardcoded)
rem   * NuGet packages restored into .\packages
rem   * .NET Framework 4.6.2 reference assemblies under %USERPROFILE%\.nuget\refasm
rem
rem If al.exe (the assembly linker from the .NET Framework SDK tools) is missing,
rem this falls back to building without the 34 satellite resource assemblies:
rem you still get a working exe, but its UI is English only.
rem See BUILD-CSHARP.md in this folder for the details, in Chinese.
rem
rem Double-click the file, or run it from a shell. Extra arguments are passed to MSBuild.
rem ---------------------------------------------------------------------------

setlocal
set "REPO=%~dp0"
set "REFASM=%USERPROFILE%\.nuget\refasm\build/"
set "REFASM_ARG="
set "PROJ=%REPO%EarTrumpet\EarTrumpet.csproj"
set "NOSAT=%REPO%build-without-localization.targets"
set "PF86=%ProgramFiles(x86)%"

set "MSBUILD=%PF86%\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
if exist "%MSBUILD%" goto located

set "VSWHERE=%PF86%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto nomatch
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
if exist "%MSBUILD%" goto located

:nomatch
echo [X] MSBuild not found.
echo     Install "Visual Studio Build Tools" with the .NET desktop build tools.
exit /b 1

:located
rem The official targeting pack wins: only fall back to the NuGet stand-in when
rem %PF86%\Reference Assemblies\...\.NETFramework\v4.6.2 is really missing.
if exist "%PF86%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.6.2\mscorlib.dll" goto refs_ready
set "REFASM_ARG=/p:TargetFrameworkRootPath=%REFASM%"
:refs_ready
set "ALDIR="
for /d %%V in ("%PF86%\Microsoft SDKs\Windows\v10.0A") do (
  for /d %%T in ("%%~V\bin\NETFX*Tools*") do (
    if exist "%%~T\x86\al.exe" set "ALDIR=%%~T\x86"
    if exist "%%~T\al.exe" set "ALDIR=%%~T"
  )
)

echo MSBuild : %MSBUILD%
echo Project : %PROJ%
echo RefAsm  : %REFASM_ARG%
if defined ALDIR goto full
echo AL      : not found -^> building without satellite resource assemblies
echo.
"%MSBUILD%" "%PROJ%" /nologo /v:m /p:Configuration=Release /p:Platform=x86 %REFASM_ARG% "/p:CustomAfterMicrosoftCommonTargets=%NOSAT%" %*
goto done

:full
echo AL      : %ALDIR%
echo.
rem The runtime here is 4.8.1 while the Dev Pack installed the 4.6.2 tools, and the
rem registry key lives in the 32 bit view. Rather than trusting MSBuild to walk down to
rem 4.6.2, hand it the directory explicitly. ALDIR contains spaces - keep it quoted.
"%MSBUILD%" "%PROJ%" /nologo /v:m /p:Configuration=Release /p:Platform=x86 %REFASM_ARG% "/p:TargetFrameworkSDKToolsDirectory=%ALDIR%" "/p:_ALExeToolPath=%ALDIR%" %*

:done
set "CODE=%ERRORLEVEL%"
echo.
if not "%CODE%"=="0" goto failed
rem Fold the per-culture satellite folders into Language\ - the runtime will not look there,
rem so LanguageFolderResourceManager takes over resource loading. See relocate-satellites.ps1.
if exist "%REPO%relocate-satellites.ps1" powershell -NoProfile -ExecutionPolicy Bypass -File "%REPO%relocate-satellites.ps1"
echo [OK] Build finished. Output: %REPO%Build\Release\EarTrumpet.exe
if not defined ALDIR (
  echo.
  echo [i] Reduced build: the exe carries neutral resources only, so the UI is English.
  echo     For the localized build, install the official component below and re-run this
  echo     script - it will detect al.exe automatically. The .resx files were never touched.
  echo       .NET Framework 4.6.2 Developer Pack  ^(~70 MB^)
  echo       https://dotnet.microsoft.com/download/dotnet-framework/net462
  echo     Details ^(Chinese^): BUILD-CSHARP.md
)
goto end

:failed
echo [X] Build failed with exit code %CODE%
echo     Details ^(Chinese^): BUILD-CSHARP.md

:end
exit /b %CODE%
