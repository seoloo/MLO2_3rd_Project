import logging,json
from django.utils import timezone
from django.conf import settings
from .models import Event
log=logging.getLogger('ticketguard.requests')
def record(request,kind,data=None,show=None,source='server'):
 try:Event.objects.create(user=request.user if request.user.is_authenticated else None,session=request.session.session_key or '',source=source,kind=kind,occurred_at=timezone.now(),show=show,data={**(data or {}),'request_id':getattr(request,'request_id',None),'schema_version':1,'run_id':settings.EXPERIMENT_RUN_ID})
 except Exception as exc:log.error(json.dumps({'at':timezone.now().isoformat(),'type':'event_storage_failed','kind':kind,'error':type(exc).__name__},ensure_ascii=False))
