using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QaRunner.Desktop;

/// <summary>기능 설명을 한곳에 두고 마우스·키보드·화면 읽기 도구에서 같은 안내를 제공한다.</summary>
public static class UsageHelp
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(UsageHelp), new PropertyMetadata(null, OnKeyChanged));
    public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);
    public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);

    private static readonly Dictionary<string, (string Title, string Text)> Topics = new()
    {
        ["help"] = ("사용 안내 · F1", "어디서 시작할지 막막하면 여기를 열어 보세요. 시험을 고르는 방법과 결과를 읽는 순서를 정리해 뒀어요."),
        ["workspace"] = ("작업 폴더 열기", "시험 문자와 보고서가 있는 qa 폴더를 열어요."),
        ["cases"] = ("다른 문자 파일 고르기", "문자와 예상 정답이 들어 있는 JSON 파일을 골라 주세요. 처음에는 기본 rules.json을 사용해도 괜찮아요."),
        ["overview"] = ("시작하기", "확인하고 싶은 내용을 골라 주세요. 필요한 시험과 준비할 내용을 안내해 드릴게요."),
        ["home"] = ("홈", "처음 사용하는 순서와 다음 할 일, 최근 결과를 한눈에 봐요. 시험 카드를 누르면 오른쪽에서 조건을 확인해요."),
        ["run-page"] = ("시험 결과", "방금 실행한 시험의 결과를 봐요. ‘조건 바꾸기’를 누르면 오른쪽에서 조건을 바꿔 다시 시험할 수 있어요."),
        ["next-step"] = ("다음 할 일", "지금 단계에서 할 일을 대신 열어 줘요. 처음이라면 이 버튼만 따라가도 첫 점검을 끝낼 수 있어요."),
        ["change-conditions"] = ("조건 바꾸기", "오른쪽에 조건 시트를 열어요. 시험 종류·대상·시간을 바꾸고 ‘시험 시작하기’를 누르면 돼요."),
        ["manage"] = ("관리", "시험 문자, AI 문자, 설정을 한곳에서 관리해요. 위의 탭으로 옮겨 다닐 수 있어요."),
        ["preset-quick"] = ("빠른 점검", "기본 문자 4건을 연습용 판정기로 시험해요. 누르면 오른쪽에서 조건을 확인하고 시작해요. 서버가 없어도 10초 안에 끝나요."),
        ["preset-api"] = ("내 API 판정", "내 판정 API를 시험해요. 누르면 주소 입력칸이 열리고, 주소를 넣은 뒤 ‘시험 시작하기’를 눌러요."),
        ["preset-load-standard"] = ("부하 기준 (5분)", "실행안 기준대로 300초 동안 초당 5건, 총 1,500건을 보내요. 5분 정도 걸려요."),
        ["checklist"] = ("처음 사용하는 순서", "앱을 쓰면서 단계가 저절로 체크돼요. 아래 ‘다음 할 일’ 버튼이 지금 단계를 바로 열어 줘요."),
        ["case-page"] = ("시험 문자", "어떤 문자에 어떤 정답을 예상했는지 읽어 보세요. 파일을 고치려면 작업 폴더에서 JSON 파일을 열어 주세요."),
        ["ai-page"] = ("AI로 문자 만들기", "Runpod은 아직 연결 전이에요. 지금은 미리 만든 예제 문자 4건을 가져와 연습할 수 있어요."),
        ["review-page"] = ("정답과 근거 확인하기", "문자와 예상 정답을 함께 읽어 보세요. 정답을 고치거나 승인하는 기능은 아직 준비 중이에요."),
        ["team"] = ("공유 기록", "이 앱 안이나 같은 PC의 공유 서버에 모인 기록을 볼 수 있어요. 다른 PC와 EC2 연결은 준비 중이에요."),
        ["reports"] = ("내 실행 기록", "이 PC에 저장한 시험 기록이에요. 하나를 고르면 그때의 결과를 다시 볼 수 있어요."),
        ["settings"] = ("설정", "API 주소와 보고서 위치를 바꿀 수 있어요. 바꾼 값은 지금 켜 둔 앱에서만 사용해요."),
        ["verdict"] = ("문자 판정 확인", "문자를 보내고 돌아온 판정이 미리 정한 정답과 같은지 봐요. 예상 정답은 시험 대상에 보내지 않아요."),
        ["pipeline"] = ("데이터 처리 확인", "같은 이벤트를 여러 번 보내거나 순서를 바꿔 보내요. 저장·정제·집계 건수가 맞는지 확인하는 시험이에요."),
        ["load"] = ("부하와 응답 속도 확인", "입력을 계속 보내면서 느린 응답이나 밀린 작업이 있는지 봐요. 보내는 시간과 1초에 보낼 건수를 정해 주세요."),
        ["recovery"] = ("중단 후 회복 확인", "처리를 잠깐 멈췄다가 다시 켜요. 밀린 작업을 따라잡는 데 걸린 시간과 마지막 집계를 확인해요."),
        ["selfcheck"] = ("QA 도구 점검", "답을 이미 아는 입력 11가지로 이 QA 도구를 점검해요. 채점기가 틀린 결과를 잡아내는지, 정답 계산과 보고서가 맞는지 봐요."),
        ["home-area"] = ("점검 영역", "서비스는 판정 API와 데이터 파이프라인, 앱은 가짜 판정 서버에 붙인 앱, QA 도구는 이 프로그램 자체를 점검해요."),
        ["mock-server"] = ("가짜 판정 서버 켜기", "이 PC에 판정 API를 흉내 내는 서버를 켜요. 앱의 API 주소를 아래에 나오는 주소로 바꾸고 판정을 요청해 보세요. 끄면 받은 요청을 계약과 비교한 보고서를 저장해요."),
        ["mock-port"] = ("포트", "서버를 열 번호예요. 기본 8000은 FastAPI와 같아서 앱 설정을 거의 바꾸지 않아도 돼요. 판정 API가 이미 8000을 쓰고 있으면 8010처럼 다른 번호를 넣어 주세요."),
        ["mock-scenario"] = ("응답 방식", "서버가 돌려줄 응답이에요. 켜 둔 채로 바꿀 수 있어요. 문자 끝에 #500처럼 표시를 붙이면 그 요청만 다른 응답을 받아요."),
        ["mock-lan"] = ("같은 Wi-Fi 휴대폰 허용", "Expo Go처럼 실제 휴대폰으로 앱을 띄울 때만 켜 주세요. 같은 네트워크의 다른 기기도 접속할 수 있어서, 처음 켤 때 Windows 방화벽이 허용할지 물어봐요. 공용 Wi-Fi에서는 켜지 마세요."),
        ["case-path"] = ("입력 파일 위치", "qa 폴더를 기준으로 적어 주세요. 문자 판정은 cases/rules.json, 데이터 처리는 cases/pipeline을 사용해요."),
        ["api"] = ("판정 API 주소", "예: http://localhost:8000. 시험할 서버가 켜져 있어야 해요. 주소를 비워 두면 연습용 판정기를 사용해요."),
        ["duration"] = ("보내는 시간 · 초", "몇 초 동안 입력을 보낼지 정해 주세요. 1초 이상이어야 해요. 기본값은 부하 30초, 복구 60초예요."),
        ["rate"] = ("1초에 보낼 건수", "예를 들어 5를 넣으면 1초에 5건씩 보내요. 0보다 큰 숫자를 입력해 주세요."),
        ["stop"] = ("멈추는 시간 · 초", "전체 시간의 1/4 지점에서 처리를 멈춰요. 다시 켠 뒤 따라잡을 시간이 남도록 짧게 정해 주세요."),
        ["report-path"] = ("보고서 저장 위치", "기본 위치는 qa/reports예요. 실행마다 HTML·JSON 보고서를 저장하고 전체 추이도 업데이트해요."),
        ["run"] = ("시험 시작하기", "지금 고른 조건으로 시험해요. 끝나면 항목별 차이와 자세한 보고서를 확인할 수 있어요."),
        ["open-report"] = ("자세한 보고서 보기", "방금 실행했거나 기록에서 고른 시험의 HTML 보고서를 브라우저로 열어요. 먼저 시험하거나 실행 기록을 골라 주세요."),
        ["dashboard"] = ("전체 기록과 추이 보기", "지금 보고서 폴더에 저장된 기록과 변화를 브라우저로 보여드려요. 아직 기록이 없다면 시험을 먼저 실행해 주세요."),
        ["generate-demo"] = ("예제 문자 가져오기", "미리 만든 문자 4건을 가져와요. AI가 새로 만드는 문자는 아니에요. 예상 정답의 근거도 확인해 주세요."),
        ["execute-demo"] = ("예제 시험하고 공유하기", "먼저 예제 문자를 가져와 주세요. 연습용 판정기와 비교한 뒤 보고서를 저장하고 선택한 방법으로 결과를 공유해요."),
        ["refresh-team"] = ("새 공유 기록 불러오기", "새로 올라온 기록을 다시 불러와요. 자동으로 갱신되지는 않아요. 기록을 고르면 아래에 자세한 결과가 나와요."),
        ["sharing-mode"] = ("공유 방식", "이 앱 안에서만 보거나 같은 PC의 공유 서버를 사용할 수 있어요. 공유 서버를 쓰려면 127.0.0.1:5085 서버를 먼저 켜 주세요."),
        ["actor"] = ("실행한 사람", "기록에 남길 이름이에요. 팀원이 알아볼 수 있는 이름을 적어 주세요. 로그인 계정과는 별개예요."),
        ["environment"] = ("시험한 환경", "예: local-demo. 어떤 환경에서 돌린 결과인지 구분할 수 있게 이름을 적어 주세요."),
        ["version"] = ("시험한 코드 버전", "브랜치 이름이나 커밋 번호를 적어 주세요. 지금은 Git에서 자동으로 가져오지 않아요."),
        ["runpod-pending"] = ("AI 연결 준비 중", "아직 Runpod으로 문자를 만들 수는 없어요. 위의 예제 문자 가져오기로 시험과 결과 공유를 먼저 연습해 보세요."),
        ["review-pending"] = ("정답 승인 준비 중", "지금은 정답과 근거를 읽어 볼 수 있어요. AI가 제안한 정답도 사람의 확인을 거친 뒤 사용해야 해요."),
        ["zoom"] = ("창 크기에 맞춰 보여드려요", "본문은 창 크기에 따라 75~100%로 맞춰요. 작은 창에서는 스크롤해 주세요. 창 조작 버튼은 같은 크기로 유지해요."),
    };

    private static void OnKeyChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is not FrameworkElement element || args.NewValue is not string key || !Topics.TryGetValue(key, out var topic)) return;
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = topic.Title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) });
        body.Children.Add(new TextBlock { Text = topic.Text, TextWrapping = TextWrapping.Wrap, LineHeight = 21 });
        element.ToolTip = new ToolTip { Content = body, MaxWidth = 350 };
        ToolTipService.SetInitialShowDelay(element, 350);
        ToolTipService.SetBetweenShowDelay(element, 100);
        ToolTipService.SetShowDuration(element, 20000);
        ToolTipService.SetShowOnDisabled(element, true);
        ToolTipService.SetShowsToolTipOnKeyboardFocus(element, true);
        AutomationProperties.SetHelpText(element, $"{topic.Title} · {topic.Text}");
    }

    public static void Enable(Window window)
    {
        window.CommandBindings.Add(new CommandBinding(ApplicationCommands.Help, (_, _) => Open(window)));
        window.InputBindings.Add(new KeyBinding(ApplicationCommands.Help, Key.F1, ModifierKeys.None));
    }

    public static void Open(Window owner)
    {
        if (owner is HelpWindow) return;
        var existing = owner.OwnedWindows.OfType<HelpWindow>().FirstOrDefault();
        if (existing is not null) { existing.Activate(); return; }
        var main = owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
        new HelpWindow(main) { Owner = owner }.Show();
    }
}
