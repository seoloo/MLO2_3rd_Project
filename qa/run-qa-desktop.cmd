@echo off
chcp 65001 > nul
setlocal

rem ============================================================
rem  정보지킴 QA 러너 실행 (팀원용)
rem  더블클릭하면 최신 코드를 받고, 빌드한 뒤, 데스크탑 앱을 켠다.
rem  처음 한 번만 .NET 10 SDK를 설치하면 된다:
rem    winget install Microsoft.DotNet.SDK.10
rem  준비만 하고 앱을 켜지 않으려면: run-qa-desktop.cmd --no-start
rem ============================================================

rem 이 파일이 있는 qa 폴더에서 실행한다 (어디서 더블클릭해도 같은 결과)
cd /d "%~dp0"

where dotnet > nul 2> nul
if errorlevel 1 (
    echo [오류] .NET SDK가 설치되어 있지 않습니다.
    echo        아래 명령으로 설치한 뒤, 이 창을 닫고 다시 실행해 주세요.
    echo        winget install Microsoft.DotNet.SDK.10
    pause
    exit /b 1
)

rem 앱이 켜져 있으면 실행 파일이 잠겨서 빌드가 실패하므로 먼저 닫아 달라고 한다
tasklist /FI "IMAGENAME eq QaRunner.Desktop.exe" 2> nul | find /I "QaRunner.Desktop.exe" > nul
if not errorlevel 1 (
    echo [안내] QA 러너 앱이 이미 켜져 있습니다. 앱 창을 닫은 뒤 다시 실행해 주세요.
    pause
    exit /b 1
)

echo [1/3] 최신 코드를 받는 중...
git pull --ff-only
if errorlevel 1 (
    rem 고치던 파일이 있거나 다른 브랜치라서 받지 못해도, 지금 있는 코드로는 계속 실행한다
    echo [안내] 최신 코드를 받지 못했습니다. 지금 PC에 있는 코드로 실행합니다.
)

echo [2/3] 빌드하는 중... (처음에는 1~2분 걸릴 수 있습니다)
dotnet build src\QaRunner.Desktop --configuration Release --verbosity quiet --nologo
if errorlevel 1 (
    echo [오류] 빌드에 실패했습니다. 위의 오류 내용을 QA 담당에게 알려 주세요.
    pause
    exit /b 1
)

if /i "%~1"=="--no-start" (
    echo [3/3] 준비 완료. --no-start 옵션이라 앱은 켜지 않습니다.
    exit /b 0
)

echo [3/3] QA 러너를 켭니다.
start "" "src\QaRunner.Desktop\bin\Release\net10.0-windows\QaRunner.Desktop.exe"
exit /b 0
