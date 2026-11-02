# AWS에서 Python 가상환경으로 실행

이 사이트를 EC2 한 곳에 실행하고 팀원에게 공인 주소를 공유하면 서버 DB의 관리자·예매·기록을 함께 사용합니다. 팀원 PC에 DB 파일을 배포할 필요가 없습니다.

실행 버전은 `.python-version`의 Python 3.15.2와 `requirements.txt`의 Django 6.1.2입니다. OS 기본 Python과 별도로 Python 3.15를 설치하고, systemd 실행 경로도 새 가상환경으로 바꿉니다.

Gunicorn은 Linux 전용입니다. Windows에서는 `start-local.ps1`의 Django 개발 서버를 사용합니다. 설치 성공과 실제 AWS 실행 검증은 구분하며, Python 3.15 공식 지원 전 Django 제한은 사이트 README의 호환성 항목을 참고하세요.

## EC2 Linux 준비

Python 3.15.2와 venv, Nginx를 준비하고 이 디렉터리를 서버에 배치합니다. 아래 명령은 사이트 디렉터리에서 실행합니다.

```sh
python3.15 -m venv .venv-3.15
.venv-3.15/bin/python -m pip install -r requirements.txt
```

`.env.example`을 참고해 서버 환경변수를 설정합니다. Django는 `.env` 파일을 자동으로 읽지 않습니다. 운영 담당자가 shell 또는 systemd의 EnvironmentFile로 로드해야 합니다. 실제 호스트·도메인과 무작위 비밀키, 팀 공용 PostgreSQL 접속 정보를 입력합니다. PostgreSQL을 지정하지 않으면 서버의 SQLite를 사용합니다. 동시 부하 시험에는 PostgreSQL을 사용하세요.

```sh
.venv-3.15/bin/python manage.py migrate
.venv-3.15/bin/python manage.py seed_shows
.venv-3.15/bin/python manage.py collectstatic --noinput
.venv-3.15/bin/gunicorn config.wsgi:application --bind 127.0.0.1:8000 --workers 2 --access-logfile -
```

SQLite를 사용하는 개발 실행은 worker를 1로 설정합니다. 장기 실행은 같은 명령을 systemd 등으로 관리하고 부팅 시 재시작하도록 구성합니다.

`migrate`가 임시 관리자 **admin / admin**을 준비합니다. 기존 관리자 비밀번호가 다르면 `.venv-3.15/bin/python manage.py setup_admin --reset-password`로 초기화합니다. 이 계정은 프로젝트 임시용이며 공개 운영 전 생성 코드 제거와 비밀번호 변경이 필요합니다.

## 팀원 접속

`deploy/nginx.conf`를 Nginx의 http 컨텍스트에서 포함하도록 설정합니다. 이 예시는 HTTP 80에서 내부 127.0.0.1:8000으로 전달합니다. 8000과 DB 포트는 외부에 열지 않습니다. EC2 보안 그룹의 80번 포트는 팀원·QA 출처를 허용하고 SSH는 관리 출처로 제한합니다.

HTTP만 사용할 때는 `DJANGO_COOKIE_SECURE=0`, `DJANGO_SSL_REDIRECT=0`, CSRF origin은 `http://실제공인주소`로 설정합니다. HTTPS 구성 시 두 값은 1로 바꾸고 origin도 https로 설정합니다. 인증서 설정은 이 예시에 포함하지 않습니다.

Nginx 뒤에서만 `DJANGO_TRUST_PROXY_HEADERS=1`을 사용합니다. 예시 프록시는 방문자의 IP·요청 ID 전달 헤더를 덮어씁니다. ALB 등 다른 프록시 추가 시 전달 경계를 다시 검토합니다.

팀원은 `http://공인주소/`와 `/admin/`, `/ops/`로 접속합니다. `/healthz/`가 200인지 확인합니다. `127.0.0.1` 주소는 각자의 PC이므로 공유 주소가 아닙니다.

## 보관과 현재 검증 범위

DB·비밀키·가상환경·로그·환경파일은 Git에 올리지 않습니다. DB와 로그의 백업·회전·복구는 서버 담당자가 관리합니다. Nginx 로그에는 별도 회전 구성이 필요합니다. 앱 요청 파일은 프로세스별 10MB/5회 회전합니다. 실제 AWS 배포·공인 접속·PostgreSQL 부하 시험은 아직 수행하지 않았습니다.
