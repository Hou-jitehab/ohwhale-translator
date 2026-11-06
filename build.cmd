@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found: %CSC%
  pause
  exit /b 1
)

set "LIB=%~dp0lib"
set "OUT=%~dp0out"
if not exist "%OUT%" mkdir "%OUT%"

echo Building WordTranslator...
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /out:"%OUT%\WordTranslator.exe" ^
 /reference:System.dll ^
 /reference:System.Core.dll ^
 /reference:System.Drawing.dll ^
 /reference:System.Windows.Forms.dll ^
 /reference:System.Web.Extensions.dll ^
 /reference:"%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll" ^
 /reference:"%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll" ^
 /reference:"%LIB%\Microsoft.Web.WebView2.Core.dll" ^
 /reference:"%LIB%\Microsoft.Web.WebView2.WinForms.dll" ^
 /resource:"%LIB%\Microsoft.Web.WebView2.Core.dll",Microsoft.Web.WebView2.Core.dll ^
 /resource:"%LIB%\Microsoft.Web.WebView2.WinForms.dll",Microsoft.Web.WebView2.WinForms.dll ^
 /resource:"%LIB%\WebView2Loader.dll",WebView2Loader.dll ^
 "%~dp0src\Native.cs" "%~dp0src\Log.cs" "%~dp0src\Settings.cs" ^
 "%~dp0src\SelectionCapture.cs" "%~dp0src\TextGrabber.cs" "%~dp0src\WebViewEngine.cs" ^
 "%~dp0src\OverlayForm.cs" "%~dp0src\SettingsForm.cs" "%~dp0src\Program.cs"

if errorlevel 1 (
  echo.
  echo [FAILED] build failed
  pause
  exit /b 1
)

rem IMPORTANT: if the compiler runs inside a low-integrity sandbox, the produced
rem exe inherits a "Low" mandatory integrity label. Windows then starts it as a
rem restricted process: global hooks fail, user dirs are read-only, WebView2
rem init returns 0x8000FFFF. Force the label back to Medium.
icacls "%OUT%\WordTranslator.exe" /setintegritylevel Medium >nul 2>&1
echo [OK] integrity label reset to Medium

echo.
echo [OK] %OUT%\WordTranslator.exe
exit /b 0