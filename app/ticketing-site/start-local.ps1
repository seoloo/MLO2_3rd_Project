$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$taskPython = Join-Path $PSScriptRoot '.venv-3.13\Scripts\python.exe'
$taskVersionCheck = 'import sys; raise SystemExit(0 if sys.version_info[:3] == (3, 13, 15) else 1)'
if (-not (Test-Path -LiteralPath $taskPython)) {
    if (Get-Command py -ErrorAction SilentlyContinue) {
        & py -3.13 -c $taskVersionCheck
        if ($LASTEXITCODE -ne 0) { throw 'Python 3.13.15를 설치한 뒤 다시 실행하세요.' }
        & py -3.13 -m venv .venv-3.13
    } elseif (Get-Command python -ErrorAction SilentlyContinue) {
        & python -c $taskVersionCheck
        if ($LASTEXITCODE -ne 0) { throw 'Python 3.13.15를 설치한 뒤 다시 실행하세요.' }
        & python -m venv .venv-3.13
    } else { throw 'Python 3.13.15를 설치한 뒤 다시 실행하세요: https://www.python.org/downloads/' }
    if ($LASTEXITCODE -ne 0) { throw '가상환경 생성에 실패했습니다.' }
}
& $taskPython -c $taskVersionCheck
if ($LASTEXITCODE -ne 0) { throw '.venv-3.13의 Python 버전이 다릅니다. Python 3.13.15로 새 환경을 준비하세요.' }
& $taskPython --version
& $taskPython -m pip install -r requirements.txt
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $taskPython -m pip check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $taskPython manage.py migrate
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $taskPython manage.py seed_shows
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $taskPython manage.py runserver 127.0.0.1:8000
