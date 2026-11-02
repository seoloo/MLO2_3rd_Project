from datetime import datetime
from zoneinfo import ZoneInfo
from django.core.management.base import BaseCommand
from booking.models import Show,Seat
class Command(BaseCommand):
 help='공연과 좌석을 멱등 생성합니다. 기존 예약은 변경하지 않습니다.'
 def handle(self,*args,**kwargs):
  catalog=[('aurora','AURORA','빛과 음악이 만나는 밤','콘서트','THE NIGHT LIVE 2026',12,19,'서울 웨이브홀',88000),('jazz','SUNDAY JAZZ','일요일의 작은 여유','재즈','BLUE NOTE SESSIONS',13,17,'서울 라운지 스테이지',55000),('wave','WAVE FESTIVAL','같은 파도 위, 우리','페스티벌','WINTER SOUND CLUB',19,18,'인천 오션 아레나',77000)]
  for slug,name,sub,cat,artist,day,hour,venue,price in catalog:
   show,_=Show.objects.get_or_create(slug=slug,defaults={'name':name,'subtitle':sub,'category':cat,'artist':artist,'starts_at':datetime(2026,12,day,hour,tzinfo=ZoneInfo('Asia/Seoul')),'venue':venue,'price':price,'tone':slug,'description':'기다려온 음악과 아티스트를 가장 가까운 자리에서 만나보세요. 특별한 라이브의 순간을 함께합니다.'})
   Seat.objects.bulk_create([Seat(show=show,label=f'{chr(65+r)}-{c+1}') for r in range(8) for c in range(12)],ignore_conflicts=True)
  self.stdout.write(self.style.SUCCESS('공연 3개·공연별 96석 준비 완료'))
