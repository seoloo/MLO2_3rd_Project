from django.contrib.auth.hashers import make_password
from django.db import migrations


def seed_admin(apps, schema_editor):
    User = apps.get_model('auth', 'User')
    users = User.objects.using(schema_editor.connection.alias)
    current = users.filter(username='admin').first()
    if current:
        if not (current.is_staff and current.is_superuser and current.is_active):
            raise RuntimeError('admin 이름의 일반/비활성 계정이 있습니다. 이름 충돌을 먼저 해결하세요.')
        # Existing administrators retain their password. Explicit reset uses setup_admin.
        return
    legacy = users.filter(username='admin01', is_staff=True,
                          is_superuser=True, is_active=True).first()
    if legacy:
        # Preserve the primary key and all related booking/history records.
        legacy.username = 'admin'
        legacy.password = make_password('admin')
        legacy.save(using=schema_editor.connection.alias, update_fields=['username', 'password'])
    else:
        users.create(username='admin', password=make_password('admin'),
                     is_staff=True, is_superuser=True, is_active=True)


class Migration(migrations.Migration):
    dependencies = [('booking', '0001_initial'), ('auth', '0012_alter_user_first_name_max_length')]
    operations = [migrations.RunPython(seed_admin, migrations.RunPython.noop)]
