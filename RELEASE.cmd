@echo off
rem ============================================================================
rem  RELEASE.cmd - publish a GitHub Release in one double-click
rem
rem  Steps:
rem    1. read the version from build.ps1 ($version = "x.y.z")
rem    2. warn if the working tree has uncommitted changes
rem    3. create and push the tag  v<version>
rem    4. open the GitHub Actions page so you can watch the build
rem
rem  The tag push triggers .github/workflows/release.yml, which builds the
rem  Windows artifacts on a hosted runner and publishes the Release itself.
rem  See RELEASE.md for the full story.
rem ============================================================================
setlocal
cd /d "%~dp0"

set "REPO_URL=https://github.com/rdfghjgyuytytrudthgc/jigu-app"

rem ---- locate git -------------------------------------------------------------
set "GIT="
for /f "delims=" %%i in ('where git 2^>nul') do if not defined GIT set "GIT=%%i"
if not defined GIT if exist "%USERPROFILE%\.workbuddy\vendor\PortableGit\cmd\git.exe" set "GIT=%USERPROFILE%\.workbuddy\vendor\PortableGit\cmd\git.exe"
if not defined GIT if exist "C:\Program Files\Git\cmd\git.exe" set "GIT=C:\Program Files\Git\cmd\git.exe"
if not defined GIT if exist "C:\Program Files (x86)\Git\cmd\git.exe" set "GIT=C:\Program Files (x86)\Git\cmd\git.exe"
if not defined GIT (
  echo [ERROR] git not found. Install Git for Windows first.
  pause
  exit /b 1
)
if not exist ".git" (
  echo [ERROR] .git not found here. Run this from the repository root.
  pause
  exit /b 1
)

rem ---- version from build.ps1 -------------------------------------------------
set "VER="
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "$m=[regex]::Match((Get-Content -Raw 'build.ps1'),'^\$version = \"([^\"]+)\"',[System.Text.RegularExpressions.RegexOptions]::Multiline); if($m.Success){$m.Groups[1].Value}"`) do set "VER=%%v"
if not defined VER (
  echo [ERROR] could not read $version from build.ps1
  pause
  exit /b 1
)
set "TAG=v%VER%"

echo.
echo ============================================================
echo   jigu  ==^>  GitHub Release
echo   git      : %GIT%
echo   version  : %VER%
echo   tag      : %TAG%
echo   repo     : %REPO_URL%
echo ============================================================
echo.

rem ---- working tree clean? ----------------------------------------------------
for /f "delims=" %%s in ('"%GIT%" status --porcelain') do (
  echo [!] the working tree has uncommitted changes:
  "%GIT%" status --short
  echo.
  echo     The Release is built from the COMMITTED code, so uncommitted
  echo     changes will NOT be in the build.
  choice /c YN /m "Continue anyway"
  if errorlevel 2 exit /b 1
  goto :tree_ok
)
:tree_ok

rem ---- does the tag already exist on the remote? -------------------------------
set "EXISTING="
for /f "delims=" %%t in ('"%GIT%" ls-remote --tags origin "refs/tags/%TAG%" 2^>nul') do set "EXISTING=%%t"
if defined EXISTING (
  echo [!] tag %TAG% already exists on the remote.
  echo.
  choice /c YN /m "Move the tag to the current commit and re-release"
  if errorlevel 2 exit /b 1
  "%GIT%" tag -d %TAG% >nul 2>&1
  "%GIT%" push --delete origin %TAG% >nul 2>&1
)

rem ---- make sure main is actually pushed first --------------------------------
echo --- pushing main (the workflow must exist on the remote to be triggered) ---
"%GIT%" push -u origin main
if errorlevel 1 (
  echo.
  echo [FAILED] could not push main. If github.com is unreachable from this
  echo         machine, run PUSH.cmd first - it handles the hosts-file block.
  pause
  exit /b 1
)

rem ---- tag it -----------------------------------------------------------------
echo.
echo --- creating and pushing tag %TAG% ---
"%GIT%" tag -a %TAG% -m "jigu %TAG%"
if errorlevel 1 (
  echo [FAILED] could not create tag %TAG%
  pause
  exit /b 1
)
"%GIT%" push origin %TAG%
if errorlevel 1 (
  echo.
  echo [FAILED] could not push the tag. Authentication or network problem -
  echo         run PUSH.cmd once to get the credentials/hosts settled.
  pause
  exit /b 1
)

echo.
echo ============================================================
echo   TAG PUSHED - GitHub Actions is building the Release now
echo ============================================================
echo.
echo   Watch it here:
echo     %REPO_URL%/actions
echo.
echo   When the workflow turns green, the download page is:
echo     %REPO_URL%/releases/tag/%TAG%
echo.
echo   Opening the Actions page in your browser...
start "" "%REPO_URL%/actions"
echo.
pause
exit /b 0
