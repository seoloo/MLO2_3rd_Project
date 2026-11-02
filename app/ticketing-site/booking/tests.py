import uuid,json
from datetime import timedelta
from django.test import TestCase,Client
from django.contrib.auth.models import User
from django.core.management import call_command
from django.utils import timezone
from .models import Show,Seat,Booking,Event
from .services import reserve,confirm,cancel,ReservationError
class BookingTests(TestCase):
 @classmethod
 def setUpTestData(cls):
  call_command('seed_shows',verbosity=0);cls.show=Show.objects.get(slug='aurora');cls.user=User.objects.create_user('alice',password='local-only-strong-password');cls.other=User.objects.create_user('bob',password='local-only-strong-password')
 def test_inventory_exclusivity_and_idempotency(self):
  key=uuid.uuid4();b=reserve(self.user,self.show.pk,['A-1'],key);self.assertEqual(reserve(self.user,self.show.pk,['A-1'],key).pk,b.pk)
  with self.assertRaises(ReservationError):reserve(self.other,self.show.pk,['A-1'],uuid.uuid4())
  confirm(self.user,b.pk);confirm(self.user,b.pk);self.assertEqual(Booking.objects.filter(status='confirmed').count(),1)
 def test_limits_validation_and_cancel(self):
  b=reserve(self.user,self.show.pk,['B-1','B-2','B-3','B-4'],uuid.uuid4())
  with self.assertRaises(ReservationError):reserve(self.user,self.show.pk,['B-5'],uuid.uuid4())
  cancel(self.user,b.pk);self.assertFalse(Seat.objects.filter(booking=b).exists());reserve(self.other,self.show.pk,['B-1'],uuid.uuid4())
  with self.assertRaises(ReservationError):reserve(self.user,self.show.pk,['Z-1'],uuid.uuid4())
 def test_expiration(self):
  b=reserve(self.user,self.show.pk,['C-1'],uuid.uuid4());Booking.objects.filter(pk=b.pk).update(expires_at=timezone.now()-timedelta(seconds=1))
  with self.assertRaises(ReservationError):confirm(self.user,b.pk)
  b2=reserve(self.other,self.show.pk,['C-1'],uuid.uuid4());self.assertNotEqual(b.pk,b2.pk)
 def test_queue_and_owner_access(self):
  self.client.force_login(self.user);url=f'/api/shows/{self.show.slug}/reserve/'
  r=self.client.post(url,data=json.dumps({'seats':['D-1'],'request_key':str(uuid.uuid4())}),content_type='application/json');self.assertEqual(r.status_code,409)
  session=self.client.session;session['queue_aurora']=0.1;session.save();r=self.client.post(url,data=json.dumps({'seats':['D-1'],'request_key':str(uuid.uuid4())}),content_type='application/json');self.assertEqual(r.status_code,200)
  b=Booking.objects.get(pk=r.json()['booking_id']);self.client.force_login(self.other);self.assertEqual(self.client.get(f'/bookings/{b.pk}/checkout/').status_code,404)
 def test_csrf_and_staff_log_access(self):
  c=Client(enforce_csrf_checks=True);c.force_login(self.user);self.assertEqual(c.post('/api/events/',data='{"events":[]}',content_type='application/json').status_code,403)
  self.client.force_login(self.user);self.assertEqual(self.client.get('/ops/').status_code,302);self.assertEqual(self.client.get('/ops/export/').status_code,302)
  self.user.is_staff=True;self.user.save();self.assertEqual(self.client.get('/ops/').status_code,200)
 def test_client_event_dedup_and_source(self):
  self.client.force_login(self.user);eid=str(uuid.uuid4());body={'events':[{'id':eid,'type':'click','at':timezone.now().timestamp()*1000,'source':'server','target':'seat-A-1'}]}
  for _ in range(2):self.assertEqual(self.client.post('/api/events/',data=json.dumps(body),content_type='application/json').status_code,200)
  self.assertEqual(Event.objects.filter(pk=eid).count(),1);self.assertEqual(Event.objects.get(pk=eid).source,'client')
 def test_customer_pages_no_log_controls(self):
  for url in ['/',f'/shows/{self.show.slug}/','/accounts/login/','/accounts/signup/']:
   r=self.client.get(url);self.assertEqual(r.status_code,200);self.assertNotContains(r,'테스트 기록');self.assertNotContains(r,'예매 테스트 공간')

 def test_full_customer_flow_with_csrf(self):
  c=Client(enforce_csrf_checks=True);c.get('/accounts/login/');token=c.cookies['csrftoken'].value
  r=c.post('/accounts/login/',{'username':'alice','password':'local-only-strong-password','csrfmiddlewaretoken':token});self.assertEqual(r.status_code,302)
  c.get('/shows/aurora/');token=c.cookies['csrftoken'].value
  self.assertEqual(c.post('/shows/aurora/enter/',{'csrfmiddlewaretoken':token}).status_code,302)
  session=c.session;session['queue_aurora']=0.1;session.save();self.assertEqual(c.get('/shows/aurora/seats/').status_code,200)
  r=c.post('/api/shows/aurora/reserve/',data=json.dumps({'seats':['E-1'],'request_key':str(uuid.uuid4())}),content_type='application/json',HTTP_X_CSRFTOKEN=token);self.assertEqual(r.status_code,200)
  checkout_url=r.json()['url'];self.assertEqual(c.get(checkout_url).status_code,200)
  self.assertEqual(c.post(checkout_url,{'agree':'on','csrfmiddlewaretoken':token}).status_code,302)
  self.assertContains(c.get('/bookings/'),'AURORA');self.assertEqual(Booking.objects.get(pk=r.json()['booking_id']).status,'confirmed')
  self.assertEqual(c.post(f"/bookings/{r.json()['booking_id']}/cancel/",{'csrfmiddlewaretoken':token}).status_code,302)

 def test_proxy_headers_are_ignored_by_default(self):
  from django.test import RequestFactory,override_settings
  from .middleware import client_ip,request_id
  r=RequestFactory().get('/',REMOTE_ADDR='127.0.0.1',HTTP_X_REAL_IP='203.0.113.9',HTTP_X_REQUEST_ID='a'*32)
  with override_settings(TRUST_PROXY_HEADERS=False):
   self.assertEqual(client_ip(r),'127.0.0.1');self.assertNotEqual(request_id(r),'a'*32)
  with override_settings(TRUST_PROXY_HEADERS=True):
   self.assertEqual(client_ip(r),'203.0.113.9');self.assertEqual(request_id(r),'a'*32)
 def test_rejected_csrf_request_still_has_request_id(self):
  c=Client(enforce_csrf_checks=True);r=c.post('/api/events/',data='{}',content_type='application/json');self.assertEqual(r.status_code,403);self.assertEqual(len(r['X-Request-ID']),32)

 def test_health(self):
  r=self.client.get('/healthz/');self.assertEqual(r.status_code,200);self.assertEqual(r.json(),{'status':'ok'})


class LocalAccountCommandTests(TestCase):
 def setUp(self):
  User.objects.filter(username="admin").delete()
 def run_setup(self, password='Local-only-sample-47!', **kwargs):
  from io import StringIO
  from unittest.mock import patch
  with patch('sys.stdin', StringIO(password+'\n')):
   call_command('setup_local_accounts', password_stdin=True, stdout=StringIO(), **kwargs)

 def test_creates_roles_and_preserves_existing_password(self):
  from django.test import override_settings
  with override_settings(DEBUG=True):
   self.run_setup()
   member=User.objects.get(username='member01');admin=User.objects.get(username='admin')
   self.assertFalse(member.is_staff);self.assertFalse(member.is_superuser)
   self.assertTrue(admin.is_staff);self.assertTrue(admin.is_superuser)
   self.assertTrue(member.check_password('Local-only-sample-47!'))
   self.run_setup(password='Another-local-sample-47!')
   member.refresh_from_db();self.assertTrue(member.check_password('Local-only-sample-47!'))
   self.run_setup(password='Another-local-sample-47!', reset_password=True)
   member.refresh_from_db();self.assertTrue(member.check_password('Another-local-sample-47!'))

 def test_refuses_deployment_environment(self):
  from django.test import override_settings
  from django.core.management.base import CommandError
  with override_settings(DEBUG=False), self.assertRaises(CommandError):self.run_setup()
  self.assertFalse(User.objects.filter(username='admin').exists())

 def test_does_not_promote_conflicting_existing_account(self):
  from django.test import override_settings
  from django.core.management.base import CommandError
  User.objects.create_user('admin',password='Existing-sample-47!')
  with override_settings(DEBUG=True), self.assertRaises(CommandError):self.run_setup()
  self.assertFalse(User.objects.get(username='admin').is_staff)
  self.assertFalse(User.objects.filter(username='member01').exists())


class SharedAdminCommandTests(TestCase):
 def setUp(self):
  User.objects.filter(username="admin").delete()
 def setup_admin(self,password='Shared-sample-47!',**options):
  from io import StringIO
  from unittest.mock import patch
  with patch('sys.stdin',StringIO(password+'\n')):
   call_command('setup_admin',password_stdin=True,stdout=StringIO(),**options)

 def test_same_administrator_in_deployment_and_local(self):
  from django.test import override_settings
  with override_settings(DEBUG=False):self.setup_admin()
  u=User.objects.get(username='admin');self.assertTrue(u.is_staff and u.is_superuser and u.is_active)
  self.assertTrue(u.check_password('Shared-sample-47!'))
  self.setup_admin(password='Different-sample-47!');u.refresh_from_db();self.assertTrue(u.check_password('Shared-sample-47!'))
  self.setup_admin(password='Different-sample-47!',reset_password=True);u.refresh_from_db();self.assertTrue(u.check_password('Different-sample-47!'))
  self.assertEqual(User.objects.filter(username='admin').count(),1)

 def test_refuses_to_promote_existing_member(self):
  from django.core.management.base import CommandError
  User.objects.create_user('admin',password='Existing-sample-47!')
  with self.assertRaises(CommandError):self.setup_admin(reset_password=True)
  self.assertFalse(User.objects.get(username='admin').is_staff)


class TemporaryAdminSeedTests(TestCase):
 def seed(self):
  from importlib import import_module
  from django.apps import apps
  from django.db import connection
  from types import SimpleNamespace
  import_module('booking.migrations.0002_temporary_shared_admin').seed_admin(apps, SimpleNamespace(connection=connection))

 def test_fresh_database_can_login_and_password_is_hashed(self):
  User.objects.filter(username='admin').delete()
  self.seed()
  u=User.objects.get(username='admin')
  self.assertNotEqual(u.password,'admin')
  self.assertTrue(self.client.login(username='admin',password='admin'))
  self.assertEqual(self.client.get('/admin/').status_code,200)
  self.assertEqual(self.client.get('/ops/').status_code,200)
  u.set_password('Changed-password-47!');u.save()
  self.seed();u.refresh_from_db()
  self.assertTrue(u.check_password('Changed-password-47!'))
  call_command('setup_admin',reset_password=True,verbosity=0)
  u.refresh_from_db();self.assertTrue(u.check_password('admin'))

 def test_legacy_administrator_keeps_identity(self):
  User.objects.filter(username='admin').delete()
  old=User.objects.create_superuser('admin01',password='Old-password-47!')
  self.seed()
  u=User.objects.get(username='admin')
  self.assertEqual(u.pk,old.pk)
  self.assertTrue(u.check_password('admin'))
  self.assertFalse(User.objects.filter(username='admin01').exists())

 def test_member_collision_is_not_promoted(self):
  User.objects.filter(username='admin').delete()
  u=User.objects.create_user('admin',password='Member-password-47!')
  with self.assertRaises(RuntimeError):self.seed()
  u.refresh_from_db();self.assertFalse(u.is_staff)
