import json,uuid,time
from datetime import datetime
from collections import Counter
from django.shortcuts import render,redirect,get_object_or_404
from django.contrib import messages
from django.contrib.auth import login
from django.contrib.auth.decorators import login_required
from django.contrib.admin.views.decorators import staff_member_required
from django.views.decorators.http import require_POST,require_GET
from django.views.decorators.csrf import ensure_csrf_cookie
from django.http import JsonResponse,HttpResponse
from django.db.models import Q
from django.utils import timezone
from django.conf import settings
from .models import Show,Seat,Booking,Event
from .services import reserve,confirm,cancel,ReservationError
from .forms import SignUpForm
from .telemetry import record
@ensure_csrf_cookie
def home(request):
 shows=Show.objects.all().order_by('id');q=request.GET.get('q','')[:100];category=request.GET.get('category','전체')
 if q:shows=shows.filter(Q(name__icontains=q)|Q(subtitle__icontains=q)|Q(artist__icontains=q))
 if category!='전체':shows=shows.filter(category=category)
 return render(request,'booking/home.html',{'shows':shows,'q':q,'category':category,'categories':['전체','콘서트','재즈','페스티벌']})
def detail(request,slug):
 show=get_object_or_404(Show,slug=slug);return render(request,'booking/detail.html',{'show':show})
def signup(request):
 if request.user.is_authenticated:return redirect('home')
 form=SignUpForm(request.POST or None)
 if request.method=='POST' and form.is_valid():
  user=form.save();login(request,user);request.session['tracking_consent']=True;record(request,'signup',{'consent_version':'2026-11-02'});return redirect('home')
 return render(request,'registration/signup.html',{'form':form})
@login_required
@require_POST
def enter(request,slug):
 show=get_object_or_404(Show,slug=slug);key='queue_'+show.slug
 if key not in request.session:request.session[key]=time.time()+3
 record(request,'queue_enter',show=show);return redirect('queue',slug=slug)
@login_required
def queue(request,slug):
 show=get_object_or_404(Show,slug=slug);ready=request.session.get('queue_'+slug)
 if not ready:return redirect('detail',slug=slug)
 if time.time()>=ready:return redirect('seats',slug=slug)
 return render(request,'booking/queue.html',{'show':show,'ready_at':int(ready*1000)})
@login_required
def seats(request,slug):
 show=get_object_or_404(Show,slug=slug);ready=request.session.get('queue_'+slug)
 if not ready or ready>time.time():return redirect('detail',slug=slug)
 return render(request,'booking/seats.html',{'show':show,'rows':[{'letter':chr(65+r),'seats':[f'{chr(65+r)}-{c+1}' for c in range(12)]} for r in range(8)],'request_key':str(uuid.uuid4())})
@login_required
@require_GET
def inventory(request,slug):
 show=get_object_or_404(Show,slug=slug)
 taken=Seat.objects.filter(show=show).filter(Q(booking__status='confirmed')|Q(booking__status='held',booking__expires_at__gt=timezone.now()))
 return JsonResponse({'occupied':list(taken.values_list('label',flat=True)),'server_time':timezone.now().isoformat()})
@login_required
@require_POST
def reserve_view(request,slug):
 show=get_object_or_404(Show,slug=slug);ready=request.session.get('queue_'+slug)
 if not ready or ready>time.time():record(request,'queue_bypass_rejected',show=show);return JsonResponse({'error':'먼저 예매 입장을 진행해 주세요.'},status=409)
 try:
  body=json.loads(request.body);labels=body['seats'];key=uuid.UUID(body['request_key'])
  if not isinstance(labels,list) or any(not isinstance(x,str) for x in labels):raise ValueError()
  booking=reserve(request.user,show.pk,labels,key)
 except (ValueError,KeyError,TypeError):return JsonResponse({'error':'요청 형식을 확인해 주세요.'},status=400)
 except ReservationError as e:record(request,'reservation_rejected',{'reason':str(e)},show);return JsonResponse({'error':str(e)},status=409)
 record(request,'seat_hold',{'booking_id':str(booking.pk),'seats':labels},show);return JsonResponse({'url':f'/bookings/{booking.pk}/checkout/','booking_id':str(booking.pk)})
@login_required
def checkout(request,pk):
 b=get_object_or_404(Booking,pk=pk,user=request.user)
 if b.status=='confirmed':return redirect('success',pk=pk)
 if request.method=='POST':
  if request.POST.get('agree')!='on':messages.error(request,'결제 방식을 확인하고 동의해 주세요.')
  else:
   try:b=confirm(request.user,pk)
   except ReservationError as e:record(request,'checkout_rejected',{'reason':str(e)},b.show);messages.error(request,str(e))
   else:record(request,'booking_confirmed',{'booking_id':str(b.pk),'quantity':b.quantity,'total':b.total},b.show);return redirect('success',pk=pk)
 return render(request,'booking/checkout.html',{'booking':b,'seat_labels':list(b.seats.values_list('label',flat=True)),'expires_ms':int(b.expires_at.timestamp()*1000),'expired':b.status!='held' or b.expires_at<=timezone.now()})
@login_required
def success(request,pk):
 b=get_object_or_404(Booking,pk=pk,user=request.user,status='confirmed');return render(request,'booking/success.html',{'booking':b})
@login_required
def my_bookings(request):return render(request,'booking/orders.html',{'bookings':Booking.objects.filter(user=request.user).select_related('show').prefetch_related('seats').order_by('-created_at')})
@login_required
@require_POST
def cancel_view(request,pk):
 get_object_or_404(Booking,pk=pk,user=request.user);b=cancel(request.user,pk);record(request,'booking_cancelled',{'booking_id':str(pk)},b.show);messages.success(request,'예매가 취소되어 좌석이 해제되었습니다.');return redirect('orders')
@login_required
@require_POST
def collect(request):
 try:
  data=json.loads(request.body);items=data['events']
  if not isinstance(items,list) or len(items)>50:raise ValueError()
  events=[]
  for e in items:
   kind=e.get('type','');at=datetime.fromtimestamp(float(e['at'])/1000,tz=timezone.get_current_timezone())
   if kind not in ['click','page_view','visibility','seat_select'] or abs((at-timezone.now()).total_seconds())>86400:raise ValueError()
   events.append(Event(id=uuid.UUID(e['id']),user=request.user,session=request.session.session_key or '',source='client',kind=kind,occurred_at=at,data={'target':str(e.get('target',''))[:100],'path':str(e.get('path',''))[:200],'x':int(e.get('x',0)),'y':int(e.get('y',0)),'sequence':int(e.get('sequence',0)),'schema_version':1,'run_id':settings.EXPERIMENT_RUN_ID,'ingest_request_id':getattr(request,'request_id',None)}))
  Event.objects.bulk_create(events,ignore_conflicts=True)
 except (KeyError,ValueError,TypeError,OverflowError):return JsonResponse({'error':'이벤트 형식 오류'},status=400)
 return JsonResponse({'accepted':len(events)})
@staff_member_required
@require_GET
def ops(request):
 kind=request.GET.get('kind','')[:60];source=request.GET.get('source','')[:10];events=Event.objects.select_related('user','show').all()
 if kind:events=events.filter(kind=kind)
 if source:events=events.filter(source=source)
 request_rows=[]
 from collections import deque
 for logfile in settings.LOG_DIR.glob('requests*.jsonl'):
  try:
   with logfile.open(encoding='utf-8') as f:lines=deque(f,maxlen=200)
   for line in lines:
    try:request_rows.append(json.loads(line))
    except ValueError:continue
  except OSError:continue
 request_rows=sorted(request_rows,key=lambda x:x.get('at',''),reverse=True)[:200]
 return render(request,'booking/ops.html',{'events':events[:200],'requests':request_rows,'kind':kind,'source':source,'event_count':Event.objects.count(),'booking_count':Booking.objects.filter(status='confirmed').count()})
@staff_member_required
@require_GET
def export_events(request):
 qs=Event.objects.select_related('user').order_by('received_at')[:10000]
 lines=[json.dumps({'id':str(e.pk),'user_id':e.user_id,'session':e.session,'source':e.source,'type':e.kind,'occurred_at':e.occurred_at.isoformat(),'received_at':e.received_at.isoformat(),'show_id':e.show_id,'data':e.data},ensure_ascii=False) for e in qs]
 r=HttpResponse('\n'.join(lines),content_type='application/x-ndjson');r['Content-Disposition']='attachment; filename="ticketguard-events.jsonl"';r['X-Export-Limit']='10000';return r

@require_GET
def health(request):
 from django.db import connection
 try:
  with connection.cursor() as cursor:cursor.execute('SELECT 1');cursor.fetchone()
 except Exception:return JsonResponse({'status':'unavailable'},status=503)
 return JsonResponse({'status':'ok'})
