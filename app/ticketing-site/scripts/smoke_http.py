"""Check a running site over HTTP without changing bookings or passwords."""
import http.cookiejar
import os
import urllib.parse
import urllib.request

base = os.environ.get('SMOKE_BASE_URL', 'http://127.0.0.1:8000').rstrip('/')
jar = http.cookiejar.CookieJar()
client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))


def get(path, expected_type=None):
    with client.open(base + path, timeout=15) as response:
        if response.status != 200 or response.geturl() != base + path:
            raise RuntimeError(f'Unexpected response for {path}: {response.status}, {response.geturl()}')
        if expected_type and expected_type not in response.headers.get('Content-Type', ''):
            raise RuntimeError(f'Unexpected content type for {path}')
        print(f'OK {path}: {response.status}')


get('/')
get('/healthz/', 'application/json')
get('/static/booking/site.css', 'text/css')
get('/accounts/login/')
token = next(cookie.value for cookie in jar if cookie.name == 'csrftoken')
username = os.environ.get('SMOKE_USERNAME', 'admin')
password = os.environ.get('SMOKE_PASSWORD', 'admin')
body = urllib.parse.urlencode({'username': username, 'password': password, 'csrfmiddlewaretoken': token}).encode()
request = urllib.request.Request(base + '/accounts/login/', data=body,
                                 headers={'Referer': base + '/accounts/login/'})
with client.open(request, timeout=15) as response:
    if response.geturl() != base + '/':
        raise RuntimeError('Administrator login failed')
get('/admin/')
get('/ops/')
print('HTTP, static files, CSRF login and administrator access passed.')
