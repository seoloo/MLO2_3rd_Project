#!/bin/sh
set -eu
python manage.py migrate --noinput
python manage.py seed_shows
python manage.py collectstatic --noinput
exec gunicorn config.wsgi:application --bind 0.0.0.0:8000 --workers "${WEB_WORKERS:-2}" --timeout "${WEB_TIMEOUT:-30}" --access-logfile - --error-logfile -
