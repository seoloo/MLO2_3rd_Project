import os,secrets
from pathlib import Path
from django.core.exceptions import ImproperlyConfigured
BASE_DIR=Path(__file__).resolve().parent.parent
DEBUG=os.getenv('DJANGO_DEBUG','1')=='1'
SECRET_KEY=os.getenv('DJANGO_SECRET_KEY','')
if not SECRET_KEY:
 if not DEBUG: raise ImproperlyConfigured('DJANGO_SECRET_KEY is required')
 keyfile=BASE_DIR/'.local-secret'
 if not keyfile.exists(): keyfile.write_text(secrets.token_urlsafe(64))
 SECRET_KEY=keyfile.read_text().strip()
ALLOWED_HOSTS=os.getenv('DJANGO_ALLOWED_HOSTS','localhost,127.0.0.1,[::1]').split(',')
CSRF_TRUSTED_ORIGINS=[x for x in os.getenv('DJANGO_CSRF_TRUSTED_ORIGINS','').split(',') if x]
INSTALLED_APPS=['django.contrib.admin','django.contrib.auth','django.contrib.contenttypes','django.contrib.sessions','django.contrib.messages','django.contrib.staticfiles','django.contrib.humanize','booking']
MIDDLEWARE=['booking.middleware.RequestLogMiddleware','django.middleware.security.SecurityMiddleware','whitenoise.middleware.WhiteNoiseMiddleware','django.contrib.sessions.middleware.SessionMiddleware','django.middleware.common.CommonMiddleware','django.middleware.csrf.CsrfViewMiddleware','django.contrib.auth.middleware.AuthenticationMiddleware','django.contrib.messages.middleware.MessageMiddleware','django.middleware.clickjacking.XFrameOptionsMiddleware']
ROOT_URLCONF='config.urls'
TEMPLATES=[{'BACKEND':'django.template.backends.django.DjangoTemplates','DIRS':[BASE_DIR/'templates'],'APP_DIRS':True,'OPTIONS':{'context_processors':['django.template.context_processors.request','django.contrib.auth.context_processors.auth','django.contrib.messages.context_processors.messages']}}]
WSGI_APPLICATION='config.wsgi.application'
if os.getenv('POSTGRES_HOST'):
 DATABASES={'default':{'ENGINE':'django.db.backends.postgresql','NAME':os.getenv('POSTGRES_DB','ticketguard'),'USER':os.getenv('POSTGRES_USER','ticketguard'),'PASSWORD':os.environ['POSTGRES_PASSWORD'],'HOST':os.environ['POSTGRES_HOST'],'PORT':os.getenv('POSTGRES_PORT','5432'),'CONN_MAX_AGE':60}}
else:DATABASES={'default':{'ENGINE':'django.db.backends.sqlite3','NAME':BASE_DIR/'db.sqlite3','OPTIONS':{'timeout':20,'transaction_mode':'IMMEDIATE'}}}
AUTH_PASSWORD_VALIDATORS=[{'NAME':'django.contrib.auth.password_validation.MinimumLengthValidator'},{'NAME':'django.contrib.auth.password_validation.CommonPasswordValidator'},{'NAME':'django.contrib.auth.password_validation.NumericPasswordValidator'}]
LANGUAGE_CODE='ko-kr';TIME_ZONE='Asia/Seoul';USE_I18N=True;USE_TZ=True
STATIC_URL='/static/';STATIC_ROOT=BASE_DIR/'staticfiles';STATICFILES_DIRS=[BASE_DIR/'static']
DEFAULT_AUTO_FIELD='django.db.models.BigAutoField';LOGIN_URL='/accounts/login/';LOGIN_REDIRECT_URL='/';LOGOUT_REDIRECT_URL='/'
SESSION_COOKIE_HTTPONLY=True;SESSION_COOKIE_SAMESITE='Lax';CSRF_COOKIE_SAMESITE='Lax'
SESSION_COOKIE_SECURE=os.getenv('DJANGO_COOKIE_SECURE','0' if DEBUG else '1')=='1';CSRF_COOKIE_SECURE=SESSION_COOKIE_SECURE
SECURE_SSL_REDIRECT=os.getenv('DJANGO_SSL_REDIRECT','0' if DEBUG else '1')=='1'
SECURE_CONTENT_TYPE_NOSNIFF=True;X_FRAME_OPTIONS='DENY';SECURE_HSTS_SECONDS=int(os.getenv('DJANGO_HSTS_SECONDS','0'))
DATA_UPLOAD_MAX_MEMORY_SIZE=65536
TRUST_PROXY_HEADERS=os.getenv('DJANGO_TRUST_PROXY_HEADERS','0')=='1'
if TRUST_PROXY_HEADERS:SECURE_PROXY_SSL_HEADER=('HTTP_X_FORWARDED_PROTO','https')
LOG_DIR=BASE_DIR/'logs';LOG_DIR.mkdir(exist_ok=True)
LOGGING={'version':1,'disable_existing_loggers':False,'formatters':{'plain':{'format':'{message}','style':'{'}},'handlers':{'events':{'class':'logging.handlers.RotatingFileHandler','filename':str(LOG_DIR/f'requests-{os.getpid()}.jsonl'),'maxBytes':10000000,'backupCount':5,'formatter':'plain'},'console':{'class':'logging.StreamHandler'}},'loggers':{'ticketguard.requests':{'handlers':['events'],'level':'INFO','propagate':False},'django':{'handlers':['console'],'level':'INFO'}}}

EXPERIMENT_RUN_ID=os.getenv('TG_RUN_ID','baseline')[:80]
