from django.contrib import admin
from .models import Show,Seat,Booking,Event
@admin.register(Show)
class ShowAdmin(admin.ModelAdmin):list_display=('name','starts_at','venue','price','limit')
@admin.register(Seat)
class SeatAdmin(admin.ModelAdmin):list_display=('show','label','booking');list_filter=('show',);search_fields=('label',)
@admin.register(Booking)
class BookingAdmin(admin.ModelAdmin):list_display=('id','user','show','status','quantity','total','created_at');list_filter=('status','show');readonly_fields=('id','user','show','status','quantity','total','expires_at','created_at','confirmed_at','key');search_fields=('user__username',)
@admin.register(Event)
class EventAdmin(admin.ModelAdmin):
 list_display=('received_at','source','kind','user','show');list_filter=('source','kind','show');search_fields=('session','user__username');readonly_fields=('id','user','session','source','kind','occurred_at','received_at','show','data')
 def has_add_permission(self,request):return False
 def has_change_permission(self,request,obj=None):return False
admin.site.site_header='티켓가드 운영 관리';admin.site.site_title='티켓가드 관리'
