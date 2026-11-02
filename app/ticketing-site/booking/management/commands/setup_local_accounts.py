import getpass
import sys
from django.conf import settings
from django.contrib.auth import get_user_model
from django.core.management.base import BaseCommand, CommandError
from django.db import transaction
from booking.accounts import ADMIN_USERNAME


class Command(BaseCommand):
    help = '로컬 개발용 일반·관리자 계정을 생성합니다. 기존 계정은 기본적으로 유지합니다.'

    def add_arguments(self, parser):
        parser.add_argument('--password-stdin', action='store_true', help='비밀번호 한 줄을 표준 입력에서 읽습니다.')
        parser.add_argument('--reset-password', action='store_true', help='일치하는 기존 로컬 계정의 비밀번호도 변경합니다.')

    def handle(self, *args, **options):
        if not settings.DEBUG:
            raise CommandError('이 명령은 DEBUG=True인 로컬 개발 환경에서만 사용할 수 있습니다. 관리자는 setup_admin을 사용하세요.')
        User = get_user_model()
        accounts = [('member01', False), (ADMIN_USERNAME, True)]
        # 사용자 입력 전에 이름이 다른 권한의 계정과 충돌하는지 검사합니다.
        for username, is_admin in accounts:
            user = User.objects.filter(username=username).first()
            if user and (user.is_staff != is_admin or user.is_superuser != is_admin):
                raise CommandError(f'{username}: 기존 계정의 권한이 다르므로 변경하지 않습니다.')
        needed = options['reset_password'] or any(not User.objects.filter(username=name).exists() for name, _ in accounts)
        password = None
        if needed:
            if options['password_stdin']:
                password = sys.stdin.readline().rstrip('\r\n')
            else:
                password = getpass.getpass('두 로컬 계정에 사용할 비밀번호: ')
                if password != getpass.getpass('비밀번호 확인: '):
                    raise CommandError('비밀번호가 일치하지 않습니다.')
            if not password:
                raise CommandError('빈 비밀번호는 사용할 수 없습니다.')
        with transaction.atomic():
            for username, is_admin in accounts:
                user = User.objects.filter(username=username).first()
                if user:
                    if user.is_staff != is_admin or user.is_superuser != is_admin:
                        raise CommandError(f'{username}: 권한 충돌로 취소했습니다.')
                    if options['reset_password']:
                        user.set_password(password)
                        user.save(update_fields=['password'])
                        self.stdout.write(f'{username}: 비밀번호 변경 완료')
                    else:
                        self.stdout.write(f'{username}: 기존 계정 유지')
                else:
                    create = User.objects.create_superuser if is_admin else User.objects.create_user
                    create(username=username, password=password)
                    self.stdout.write(f'{username}: 생성 완료')
        self.stdout.write(self.style.SUCCESS('일반 member01 / 관리자 admin 준비 완료. 비밀번호는 출력·저장 파일에 기록하지 않았습니다.'))
