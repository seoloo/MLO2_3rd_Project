@echo off
chcp 65001 > nul
setlocal
cd /d "%~dp0"

where dotnet > nul 2> nul
if errorlevel 1 (
    echo [오류] 배포 파일을 만들려면 .NET 10 SDK가 필요합니다.
    exit /b 1
)

echo Windows 64비트용 QA 배포 파일을 만드는 중...
dotnet publish src\QaRunner.Desktop\QaRunner.Desktop.csproj --configuration Release --property:PublishProfile=WindowsX64 --verbosity minimal --nologo
if errorlevel 1 exit /b 1

powershell -NoProfile -Command "Compress-Archive -Path 'dist\windows-x64\QaRunner.Desktop.exe','dist\windows-x64\THIRD-PARTY-NOTICES.txt','dist\windows-x64\cases' -DestinationPath 'dist\QA-windows-x64.zip' -Force"
if errorlevel 1 exit /b 1

echo.
echo 완료: %~dp0dist\windows-x64\QaRunner.Desktop.exe
echo 전달 파일: %~dp0dist\QA-windows-x64.zip
echo ZIP을 폴더에 풀어 실행하세요.
echo 실행 PC에는 .NET 10 Desktop Runtime이 필요합니다.
exit /b 0
