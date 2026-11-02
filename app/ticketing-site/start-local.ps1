$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$taskPython = Join-Path $PSScriptRoot '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $taskPython)) { python -m venv .venv; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
& $taskPython -m pip install -r requirements.txt
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $taskPython manage.py migrate
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $taskPython manage.py seed_shows
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $taskPython manage.py runserver 127.0.0.1:8000
