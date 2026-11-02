import time,json,uuid,logging,ipaddress
from django.conf import settings
from django.utils import timezone
from django.utils.crypto import salted_hmac
logger=logging.getLogger('ticketguard.requests')
def client_ip(request):
 value=request.META.get('REMOTE_ADDR','')
 if settings.TRUST_PROXY_HEADERS:
  forwarded=request.META.get('HTTP_X_REAL_IP','')
  try:value=str(ipaddress.ip_address(forwarded))
  except ValueError:pass
 return value
def request_id(request):
 if settings.TRUST_PROXY_HEADERS:
  try:return uuid.UUID(request.META.get('HTTP_X_REQUEST_ID','')).hex
  except (ValueError,TypeError):pass
 return uuid.uuid4().hex
class RequestLogMiddleware:
 def __init__(self,get_response):self.get_response=get_response
 def __call__(self,request):
  start=time.perf_counter();request.request_id=request_id(request)
  try:
   response=self.get_response(request);status=response.status_code;response['X-Request-ID']=request.request_id;return response
  finally:
   status=locals().get('status',500)
   if not request.path.startswith('/static/'):
    payload={'schema_version':1,'run_id':settings.EXPERIMENT_RUN_ID,'at':timezone.now().isoformat(),'request_id':request.request_id,'path':request.path,'method':request.method,'status':status,'duration_ms':round((time.perf_counter()-start)*1000,2),'user_id':request.user.pk if hasattr(request,'user') and request.user.is_authenticated else None,'session':request.session.session_key if hasattr(request,'session') else None,'ip_tag':salted_hmac('ticketguard.ip',client_ip(request)).hexdigest()[:24]}
    try:logger.info(json.dumps(payload,ensure_ascii=False))
    except Exception:pass
