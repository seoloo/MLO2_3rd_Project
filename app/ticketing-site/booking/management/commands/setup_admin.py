import getpass
import sys
from django.contrib.auth import get_user_model
from django.core.management.base import BaseCommand, CommandError
from django.db import transaction
from booking.accounts import ADMIN_USERNAME


class Command(BaseCommand):
    help = '로컬·Docker·AWS의 공통 관리자 admin01을 준비합니다.'

    def add_arguments(self, parser):
        parser.add_argument('--password-stdin', action='store_true')
        parser.add_argument('--reset-password', action='store_true')

    def handle(self, *args, **options):
        User = get_user_model()
        existing = User.objects.filter(username=ADMIN_USERNAME).first()
        if existing and (not existing.is_staff or not existing.is_superuser or not existing.is_active):
            raise CommandError('admin01이 다른 권한 또는 비활성 계정으로 존재합니다. 덮어쓰지 않습니다.')
        if existing and not options['reset_password']:
            self.stdout.write('admin01: 기존 관리자와 비밀번호 유지. 변경하려면 --reset-password를 사용하세요.')
            return
        if options['password_stdin']:
            password = sys.stdin.readline().rstrip('\r\n')
        else:
            password = getpass.getpass('공통 관리자 admin01의 비밀번호: ')
            if password != getpass.getpass('비밀번호 확인: '):
                raise CommandError('비밀번호가 일치하지 않습니다.')
        if not password:
            raise CommandError('빈 비밀번호는 사용할 수 없습니다.')
        with transaction.atomic():
            existing = User.objects.select_for_update().filter(username=ADMIN_USERNAME).first()
            if existing:
                if not existing.is_staff or not existing.is_superuser or not existing.is_active:
                    raise CommandError('기존 계정의 권한이 다릅니다. 덮어쓰지 않습니다.')
                if options['reset_password']:
                    existing.set_password(password)
                    existing.save(update_fields=['password'])
            else:
                User.objects.create_superuser(username=ADMIN_USERNAME, password=password)
        self.stdout.write(self.style.SUCCESS('admin01 관리자 준비 완료. 비밀번호는 출력하지 않습니다.'))
