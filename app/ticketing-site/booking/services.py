from datetime import timedelta
from django.db import transaction
from django.db.models import Sum,Q
from django.utils import timezone
from .models import Show,Booking,Seat
class ReservationError(Exception):pass
def expire(show):
 ids=list(Booking.objects.filter(show=show,status='held',expires_at__lte=timezone.now()).values_list('id',flat=True))
 Seat.objects.filter(booking_id__in=ids).update(booking=None);Booking.objects.filter(id__in=ids).update(status='expired')
@transaction.atomic
def reserve(user,show_id,labels,key):
 show=Show.objects.select_for_update().get(pk=show_id);expire(show)
 prior=Booking.objects.filter(user=user,key=key).first()
 if prior:
  if prior.show_id!=show.pk or sorted(prior.seats.values_list('label',flat=True))!=sorted(labels):raise ReservationError('동일 요청 번호의 좌석 정보가 다릅니다.')
  if prior.status!='held' or prior.expires_at<=timezone.now():raise ReservationError('이미 처리된 요청입니다. 좌석을 다시 선택해 주세요.')
  return prior
 seats=list(Seat.objects.select_for_update().filter(show=show,label__in=labels))
 if len(labels)<1 or len(labels)>show.limit or len(set(labels))!=len(labels) or len(seats)!=len(labels):raise ReservationError('유효한 좌석을 선택해 주세요.')
 if any(s.booking_id for s in seats):raise ReservationError('다른 예매자가 먼저 선택한 좌석입니다.')
 used=Booking.objects.filter(user=user,show=show).filter(Q(status='confirmed')|Q(status='held',expires_at__gt=timezone.now())).aggregate(n=Sum('quantity'))['n'] or 0
 if used+len(seats)>show.limit:raise ReservationError(f'공연별 최대 {show.limit}매까지 예매할 수 있습니다. 확보 중인 좌석도 포함됩니다.')
 b=Booking.objects.create(user=user,show=show,quantity=len(seats),total=show.price*len(seats),expires_at=timezone.now()+timedelta(minutes=5),key=key)
 Seat.objects.filter(pk__in=[s.pk for s in seats]).update(booking=b);return b
@transaction.atomic
def confirm(user,booking_id):
 initial=Booking.objects.get(pk=booking_id,user=user);show=Show.objects.select_for_update().get(pk=initial.show_id);expire(show);b=Booking.objects.select_for_update().get(pk=initial.pk)
 if b.status=='confirmed':return b
 if b.status!='held' or b.expires_at<=timezone.now():raise ReservationError('좌석 확보 시간이 만료되었습니다.')
 if b.seats.count()!=b.quantity:raise ReservationError('좌석 상태가 변경되었습니다.')
 b.status='confirmed';b.confirmed_at=timezone.now();b.save(update_fields=['status','confirmed_at']);return b
@transaction.atomic
def cancel(user,booking_id):
 initial=Booking.objects.get(pk=booking_id,user=user);show=Show.objects.select_for_update().get(pk=initial.show_id);b=Booking.objects.select_for_update().get(pk=initial.pk)
 if b.status not in ['held','confirmed']:return b
 Seat.objects.filter(booking=b).update(booking=None);b.status='cancelled';b.save(update_fields=['status']);return b
