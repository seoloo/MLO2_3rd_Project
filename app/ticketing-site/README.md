# 티켓가드 · Django 예매 사이트

## 실행 버전

- **Python 3.15.2** — 2026-11-02 확인한 최신 정식 버전
- **Django 6.1.2** / **Gunicorn 26.2.0은 Linux 전용** (Windows에서는 설치 제외)
- Windows 가상환경: `.venv-3.15` (기존 `.venv`와 별도 생성)

이 조합에서 예매·계정 테스트 19개와 `pip check`를 통과했습니다. Django 문서는 Python 3.15 공식 지원을 6.1.3부터로 안내하지만, 현재 배포된 최신 정식 Django는 6.1.2입니다. 개발판은 사용하지 않았으며 실제 AWS/PostgreSQL 통합·부하 검증은 별도입니다.

공식 버전: [Python 3.15.2](https://www.python.org/downloads/release/python-3152/) · [Django 정식 릴리스](https://www.djangoproject.com/download/) · [Django 6.1 Python 지원](https://docs.djangoproject.com/en/dev/releases/6.1/)

## Windows에서 실행

이 폴더에서 `./start-local.ps1`을 실행합니다. Python 가상환경 생성 → 의존성 설치 → DB 준비 → 임시 관리자 생성 → 공연·좌석 준비 → 서버 실행 순서입니다.

Python 3.15.2를 설치한 뒤 이 폴더에서 아래 명령으로 준비합니다. 기존 `.venv`와 DB 파일은 삭제하지 않습니다.

```powershell
py -3.15 -m venv .venv-3.15
.\.venv-3.15\Scripts\python.exe -m pip install -r requirements.txt
.\.venv-3.15\Scripts\python.exe manage.py migrate
.\.venv-3.15\Scripts\python.exe manage.py seed_shows
.\.venv-3.15\Scripts\python.exe manage.py runserver 127.0.0.1:8000
```

## 임시 공통 관리자

- 아이디: **admin**
- 비밀번호: **admin**
- 고객 사이트: http://127.0.0.1:8000/
- 관리 화면: http://127.0.0.1:8000/admin/
- 운영 기록: http://127.0.0.1:8000/ops/

`migrate`가 처음 적용될 때 자동 생성합니다. 기존 `admin01` 관리자는 ID와 연결된 예매 기록을 보존하면서 `admin / admin`으로 전환합니다. 이미 `admin` 관리자가 있으면 비밀번호를 보존하며, 아래 명령으로 임시 비밀번호로 초기화할 수 있습니다.

```powershell
.\.venv-3.15\Scripts\python.exe manage.py setup_admin --reset-password
```

임시 자격 증명은 `booking/accounts.py`와 초기 생성 마이그레이션에 명시되어 있습니다. 실제 DB에는 Django 비밀번호 해시를 저장하며 일반 계정을 자동 승격하지 않습니다. 공개 운영 전 임시 계정 생성 코드를 제거하고 비밀번호를 변경해야 합니다.

Git은 DB 자체를 공유하지 않습니다. 팀원이 각자 실행하면 각각의 DB에 같은 계정을 준비합니다. AWS 주소로 접속하면 서버의 공통 DB를 사용하므로 계정·예매·기록이 함께 보입니다. 일반 사용자는 회원가입으로 생성합니다. 기존 `member01` 계정은 삭제하지 않습니다.

## 예매와 기록

회원가입/로그인 → 공연 → 3초 입장 준비 → 좌석 → 5분 확보 → 승인 시뮬레이션 → 완료/취소. 공연당 96석, 회원당 공연별 최대 4매이며 확보 중 수량도 포함합니다. 실제 PG 결제와 입장권 발급은 연결하지 않았습니다. 대기는 고정 3초이며 실제 대기 순위가 아닙니다.

관리자만 `/ops/`와 `/ops/export/`에 접근합니다. 요청 파일은 `logs/requests-프로세스ID.jsonl`에 10MB/5회 회전으로 보관합니다. 로그인 후 행동 이벤트를 1초마다 전송하고 중복 ID는 한 번만 저장합니다. 요청 본문·비밀번호·쿼리는 요청 로그에 기록하지 않고 IP는 HMAC 가명값으로 저장합니다.

매크로 탐지·추가 인증·Kafka·Spark·Redis 분석은 미연결입니다. 이벤트 연결은 `EVENT_CONTRACT.md`, 서버 실행은 `AWS_DEPLOY.md`를 참고하세요.

## 검증

```powershell
.\.venv-3.15\Scripts\python.exe manage.py test booking
```

계정 초기 생성·전환·로그인·권한, 좌석 충돌·한도·만료·취소, 이벤트 중복, CSRF와 타인 주문 접근을 확인합니다. SQLite는 개발용이며 동시 부하 검증에는 PostgreSQL을 권장합니다. AWS 실배포는 별도 인프라 작업입니다.

## 호환성 확인과 운영체제 구분

| 환경 | 실행 방식 | 확인 범위 |
|---|---|---|
| Windows / Python 3.15.2 | Django 개발 서버 | 새 가상환경 설치, 의존성 검사, 예매·계정 테스트, 배포 설정 WSGI HTTP 확인 |
| AWS Linux / Python 3.15.2 | Gunicorn 26.2.0 + Nginx | Linux 실행이 필요함. Windows의 설치 성공으로 Linux 서버 검증을 대신하지 않음 |
| PostgreSQL | psycopg 3.3.6 바이너리 | Windows Python 3.15용 바이너리 로드 확인. 실제 DB 시험은 별도 필요 |

Gunicorn은 Unix 전용이며 Windows에서 실행하면 `fcntl` 오류가 납니다. `requirements.txt`의 운영체제 조건으로 Windows 설치에서 제외했습니다. 개발 서버 실행에 Gunicorn은 필요하지 않습니다.

Python 3.15 공식 지원은 Django 6.1.3부터이며, 2026-11-02 현재 설치 가능한 정식 버전은 6.1.2입니다. 이 사이트에서 시험한 기능은 통과했지만 Django 전체 기능의 호환성을 보장하는 뜻은 아닙니다. 특히 Windows의 한글 파일명 번역 추출(`makemessages`) 문제는 6.1.3 수정 예정이므로 이 버전에서는 사용하지 않습니다. 현재 사이트는 번역 추출을 사용하지 않습니다. 6.1.3 정식 배포 후 버전을 고정 갱신하고 동일 검증을 다시 수행해야 합니다.

저장소의 `티켓팅 사이트 호환성 검사` Actions는 Windows/SQLite와 Linux/PostgreSQL에서 설치·의존성·Django 테스트를 실행하고, Linux에서는 Gunicorn을 실제 기동해 HTTP를 검사합니다. 해당 브랜치에 워크플로가 푸시되면 실행되며 실제 AWS 인프라 검증과는 별도입니다.
