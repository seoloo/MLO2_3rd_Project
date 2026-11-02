from django.contrib.auth.forms import UserCreationForm
from django.contrib.auth.models import User
from django import forms
class SignUpForm(UserCreationForm):
 tracking_consent=forms.BooleanField(label='예매 서비스 이용 중 행동·요청 기록 수집 안내를 확인했습니다.')
 class Meta:model=User;fields=('username','password1','password2')
