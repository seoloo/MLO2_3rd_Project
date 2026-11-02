# 프론트 예매 사이트 · 팀원 실행 매뉴얼

> **프론트 브랜치: `codex/frontend` · 임시 관리자: `admin / admin`**
>
> Python 가상환경으로 실행합니다. 아래는 Django 예매 대상 사이트 안내입니다.

## 실행 버전

- **Python 3.13.15** — 팀 공통 기준 버전
- **Django 6.1.2** — Python 3.13 공식 지원
- **Gunicorn 26.2.0** — AWS Linux 전용, Windows 설치 제외
- **psycopg / psycopg-binary 3.3.6**, WhiteNoise 6.12.0
- Windows 가상환경: **`.venv-3.13`**

Python 3.13.15의 새 가상환경에서 설치·의존성 검사와 예매·계정 테스트 19개, 배포 설정 WSGI HTTP 검사를 통과했습니다. 실제 AWS/PostgreSQL 통합·부하 검증은 별도입니다.

공식 기준: [Python 3.13.15](https://www.python.org/downloads/release/python-31315/) · [Django Python 지원](https://docs.djangoproject.com/en/dev/releases/6.1/)

### 1. 코드 받기

처음 받는 팀원은 PowerShell에서 실행합니다.

```powershell
git clone --branch codex/frontend https://github.com/seoloo/MLO2_3rd_Project.git
cd MLO2_3rd_Project
```

이미 저장소가 있으면 저장소 폴더에서 아래 명령을 실행합니다. 작업 중인 파일은 먼저 커밋하거나 별도로 보관하세요.

```powershell
git fetch origin
git switch codex/frontend
git pull --ff-only origin codex/frontend
```

### 2. 사이트 실행하기

Python **3.13.15** · Django **6.1.2** 기준입니다. Python 3.13.15를 먼저 설치하세요. 저장소 폴더에서 다음 순서대로 실행합니다. 가상환경 활성화 없이 실행할 수 있습니다.

```powershell
cd app/ticketing-site
py -3.13 -m venv .venv-3.13
.\.venv-3.13\Scripts\python.exe -m pip install -r requirements.txt
.\.venv-3.13\Scripts\python.exe manage.py migrate
.\.venv-3.13\Scripts\python.exe manage.py seed_shows
.\.venv-3.13\Scripts\python.exe manage.py runserver 127.0.0.1:8000
```

이전 Python으로 만든 `.venv`는 사용하지 않고 `.venv-3.13`를 새로 만듭니다. 기존 DB와 계정·예매 기록은 유지됩니다.

서버를 실행한 창은 열어 둔 채 브라우저로 접속합니다. 실행을 중지하려면 해당 창에서 `Ctrl+C`를 누릅니다. 이후 다시 실행할 때는 사이트 폴더에서 마지막 `runserver` 명령을 사용합니다. 코드를 업데이트한 뒤에는 서버를 중지하고 의존성 설치와 `migrate`를 다시 실행합니다.

### 3. 접속과 로그인

| 하고 싶은 일 | 접속 주소 또는 방법 |
|---|---|
| 공연 확인·티켓 예매 | http://127.0.0.1:8000/ |
| 관리자 로그인 | http://127.0.0.1:8000/admin/ |
| 관리자 운영 기록 | http://127.0.0.1:8000/ops/ |
| 임시 관리자 정보 | 아이디 **admin** / 비밀번호 **admin** |
| 일반 회원 이용 | 사이트에서 회원가입 후 로그인 |

`migrate`에서 임시 관리자를 자동 생성합니다. 이전 `admin01` 관리자는 예매 기록과 계정 ID를 유지하면서 `admin`으로 전환합니다. 이미 존재하는 `admin` 관리자의 비밀번호는 자동 변경하지 않으므로 로그인이 안 되면 사이트 폴더의 **다른 PowerShell 창**에서 아래 명령을 실행합니다.

```powershell
.\.venv-3.13\Scripts\python.exe manage.py setup_admin --reset-password
```

실행 후 아이디와 비밀번호에 각각 `admin`을 입력합니다. 일반 계정이 이미 `admin` 이름을 사용하면 권한을 임의로 변경하지 않고 충돌 오류를 표시합니다. 이 경우 해당 계정을 다른 이름으로 변경한 뒤 다시 진행합니다.

### 4. 팀원끼리 같은 사이트를 보려면

각자 PC에서 실행한 `127.0.0.1`은 각자 다른 사이트와 DB입니다. **같은 예매·계정·로그를 보려면 AWS에 실행한 하나의 공용 사이트 주소로 접속**해야 합니다. AWS 공용 주소는 배포 담당자가 전달합니다. Git으로 DB나 기존 계정 데이터가 복사되지는 않습니다.

관리자 기본값은 프로젝트 임시용으로 코드에 포함되어 있습니다. 공개 운영 전 임시 계정 생성 코드를 제거하고 비밀번호를 변경하세요. 실제 결제는 연결되어 있지 않으며 최종 승인은 시뮬레이션입니다. 매크로 탐지·Kafka·Spark 판정 연동은 아직 연결하지 않았습니다.

**상세 안내:** [사이트 실행·기능](app/ticketing-site/README.md) · [AWS 실행 매뉴얼](app/ticketing-site/AWS_DEPLOY.md) · [행동 이벤트 규격](app/ticketing-site/EVENT_CONTRACT.md)
