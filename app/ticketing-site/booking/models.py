import uuid
from django.conf import settings
from django.db import models
class Show(models.Model):
 slug=models.SlugField(unique=True);name=models.CharField(max_length=100);subtitle=models.CharField(max_length=100);category=models.CharField(max_length=30);artist=models.CharField(max_length=100);starts_at=models.DateTimeField();venue=models.CharField(max_length=100);price=models.PositiveIntegerField();tone=models.CharField(max_length=20);description=models.TextField();limit=models.PositiveIntegerField(default=4)
 def __str__(self):return self.name
class Booking(models.Model):
 id=models.UUIDField(primary_key=True,default=uuid.uuid4,editable=False);user=models.ForeignKey(settings.AUTH_USER_MODEL,on_delete=models.PROTECT);show=models.ForeignKey(Show,on_delete=models.PROTECT);status=models.CharField(max_length=15,default='held',choices=[('held','좌석 확보'),('confirmed','예매 완료'),('expired','만료'),('cancelled','취소')]);quantity=models.PositiveIntegerField();total=models.PositiveIntegerField();expires_at=models.DateTimeField();created_at=models.DateTimeField(auto_now_add=True);confirmed_at=models.DateTimeField(null=True,blank=True);key=models.UUIDField()
 class Meta:constraints=[models.UniqueConstraint(fields=['user','key'],name='unique_user_reservation_key')]
class Seat(models.Model):
 show=models.ForeignKey(Show,on_delete=models.CASCADE);label=models.CharField(max_length=10);booking=models.ForeignKey(Booking,on_delete=models.SET_NULL,null=True,blank=True,related_name='seats')
 class Meta:constraints=[models.UniqueConstraint(fields=['show','label'],name='unique_show_seat')];ordering=['id']
class Event(models.Model):
 id=models.UUIDField(primary_key=True,default=uuid.uuid4,editable=False);user=models.ForeignKey(settings.AUTH_USER_MODEL,on_delete=models.SET_NULL,null=True,blank=True);session=models.CharField(max_length=64,blank=True);source=models.CharField(max_length=10);kind=models.CharField(max_length=60);occurred_at=models.DateTimeField();received_at=models.DateTimeField(auto_now_add=True);show=models.ForeignKey(Show,on_delete=models.SET_NULL,null=True,blank=True);data=models.JSONField(default=dict)
 class Meta:indexes=[models.Index(fields=['received_at']),models.Index(fields=['user','kind'])];ordering=['-received_at']
