using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QaRunner.Models;
using Symbol = Wpf.Ui.Controls.SymbolRegular;

namespace QaRunner.Desktop;

/// <summary>처음 사용하는 순서(홈의 여정 지도)에서 완료를 표시할 단계.</summary>
public enum ChecklistStepKind { PickTest, RunTest, OpenReport, ReopenHistory }

/// <summary>홈 여정 지도의 한 칸. 완료·현재 단계가 바뀌면 화면이 따라 바뀐다.</summary>
public sealed class ChecklistStep(ChecklistStepKind kind, int number, string shortTitle, bool isFirst, bool isLast) : INotifyPropertyChanged
{
    private bool _isDone;
    private bool _isCurrent;
    private bool _leftActive;
    private bool _rightActive;
    public ChecklistStepKind Kind { get; } = kind;
    public int Number { get; } = number;
    /// <summary>여정 지도 원 아래에 쓰는 짧은 이름</summary>
    public string ShortTitle { get; } = shortTitle;
    /// <summary>처음·마지막 칸은 바깥쪽 연결선을 그리지 않는다</summary>
    public bool IsFirst { get; } = isFirst;
    public bool IsLast { get; } = isLast;
    public bool IsDone { get => _isDone; set => Set(ref _isDone, value); }
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
    /// <summary>왼쪽·오른쪽 연결선을 색칠할지. 양쪽 단계에 모두 도달했을 때만 칠한다</summary>
    public bool LeftActive { get => _leftActive; set => Set(ref _leftActive, value); }
    public bool RightActive { get => _rightActive; set => Set(ref _rightActive, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// 홈(여정 지도·다음 할 일·시험 카드)과 오른쪽 조건 시트, 시험 대상 선택.
/// </summary>
public partial class MainWindow
{
    private TestRun? _displayedRun;
    private readonly ObservableCollection<ChecklistStep> _checklistSteps = [];

    // ---------- 오른쪽 조건 시트 ----------

    /// <summary>오른쪽 조건 시트가 열려 있는지</summary>
    public bool IsSheetOpen => SheetLayer.Visibility == Visibility.Visible;

    /// <summary>
    /// 조건 시트를 연다. 시험 종류를 넘기면 그 시험으로 바꿔서 연다. 시험은 시트의 '시험 시작하기'를 눌러야 시작한다.
    /// </summary>
    public void OpenConditionSheet(TestSuite? suite = null)
    {
        if (_isRunning || CollaborationPage.IsBusy) return;
        if (suite is { } chosen) SelectSuite(chosen);
        SheetError.IsOpen = false;
        SheetLayer.Visibility = Visibility.Visible;
        UpdateSheetHeader();
        CompleteStep(ChecklistStepKind.PickTest);
    }

    private void CloseSheet() => SheetLayer.Visibility = Visibility.Collapsed;

    private void CloseSheet_Click(object sender, RoutedEventArgs e) => CloseSheet();
    private void SheetBackdrop_MouseDown(object sender, MouseButtonEventArgs e) => CloseSheet();
    private void OpenSheet_Click(object sender, RoutedEventArgs e) => OpenConditionSheet();
    private void SheetDuration_Changed(object sender, TextChangedEventArgs e) => UpdateSheetHeader();

    /// <summary>시트 위쪽 아이콘과 아래쪽 '약 N초 걸려요', 연습용 안내를 지금 조건에 맞춘다.</summary>
    private void UpdateSheetHeader()
    {
        if (SheetIcon is null || SheetDurationText is null || SheetPracticeNote is null || DurationBox is null) return;
        var suite = SelectedSuite;
        SheetIcon.Symbol = suite switch
        {
            TestSuite.Pipeline => Symbol.DataTrending24,
            TestSuite.Load => Symbol.Gauge24,
            TestSuite.Recovery => Symbol.ArrowClockwise24,
            TestSuite.SelfCheck => Symbol.ShieldCheckmark24,
            _ => Symbol.Flash24,
        };
        SheetDurationText.Text = suite switch
        {
            TestSuite.Verdict => "약 10초 걸려요",
            TestSuite.Pipeline => "약 20초 걸려요",
            TestSuite.SelfCheck => "약 1초 걸려요",
            _ => int.TryParse(DurationBox.Text.Trim(), out var seconds) && seconds > 0
                ? seconds >= 60 ? $"약 {seconds / 60}분 {(seconds % 60 > 0 ? $"{seconds % 60}초 " : "")}걸려요" : $"약 {seconds}초 걸려요"
                : "보내는 시간을 넣어 주세요",
        };
        var usesApi = suite is TestSuite.Verdict or TestSuite.Load;
        // QA 도구 점검은 연습용 대상이 아니라 QA 도구 자신을 점검하므로 연습용 안내를 붙이지 않는다
        SheetPracticeNote.Visibility = suite == TestSuite.SelfCheck || usesApi && ApiTargetRadio.IsChecked == true
            ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- 홈의 시험 카드 ----------

    private void SuiteCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        var suite = tag switch
        {
            "pipeline" => TestSuite.Pipeline,
            "load" => TestSuite.Load,
            "recovery" => TestSuite.Recovery,
            "selfcheck" => TestSuite.SelfCheck,
            _ => TestSuite.Verdict,
        };
        // 홈 카드의 판정 시험은 '빠른 점검': 연습용 대상으로 연다
        if (suite == TestSuite.Verdict && ApiUrlBox.Text.Trim().Length == 0) PracticeTargetRadio.IsChecked = true;
        OpenConditionSheet(suite);
    }

    /// <summary>
    /// 묶어 둔 조건을 적용하고 바로 시험한다. '내 API 판정(api)'은 주소가 필요해서 시트만 열어 준다.
    /// '다음 할 일' 버튼과 화면 점검(UI 스모크)이 같은 경로를 쓴다.
    /// </summary>
    public async Task RunPresetAsync(string preset)
    {
        if (_isRunning || CollaborationPage.IsBusy) return;
        switch (preset)
        {
            case "api":
                OpenConditionSheet(TestSuite.Verdict);
                ApiTargetRadio.IsChecked = true;
                RunInfoText.Text = "판정 API 주소를 넣고 ‘시험 시작하기’를 눌러 주세요.";
                ShowStatus(RunInfoText.Text, false);
                ApiUrlBox.Focus();
                return;
            case "pipeline":
                SelectSuite(TestSuite.Pipeline);
                break;
            case "load":
            case "load-standard":
                SelectSuite(TestSuite.Load);
                PracticeTargetRadio.IsChecked = true;
                // 같은 시험을 다시 고르면 기본값이 다시 채워지지 않으므로 시간·건수를 직접 넣는다
                DurationBox.Text = preset == "load" ? "30" : "300";
                RateBox.Text = "5";
                break;
            case "recovery":
                SelectSuite(TestSuite.Recovery);
                DurationBox.Text = "60";
                RateBox.Text = "5";
                StopSecondsBox.Text = "15";
                break;
            default:
                SelectSuite(TestSuite.Verdict);
                PracticeTargetRadio.IsChecked = true;
                break;
        }
        CompleteStep(ChecklistStepKind.PickTest);
        await RunSelectedSuiteAsync();
    }

    // ---------- 시험 대상 (연습용 / 내 판정 API) ----------

    private void Target_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isReady || ApiUrlBox is null) return;
        // 연습용으로 바꾸면 이전에 넣은 API 주소를 시험 대상에서 뺀다
        if (PracticeTargetRadio.IsChecked == true && ApiUrlBox.Text.Length > 0) ApiUrlBox.Text = "";
        RefreshExecutionTarget();
        UpdateSheetHeader();
        if (ApiTargetRadio.IsChecked == true && IsLoaded) ApiUrlBox.Focus();
    }

    private static bool IsHttpAddress(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrWhiteSpace(uri.Host);

    // QA가 주소 끝에 /checks를 붙여 보내므로, 이미 /checks까지 넣은 주소는 /checks/checks로 잘못 간다
    private static bool EndsWithCheckPath(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.AbsolutePath.TrimEnd('/').EndsWith("/checks", StringComparison.OrdinalIgnoreCase);

    // 입력칸 아래 안내: 비었으면 무엇을 넣는 칸인지, 틀렸으면 고치는 법, 맞으면 실제로 요청을 보낼 주소를 보여 준다
    private static string DescribeApiAddress(string address) =>
        address.Length == 0 ? "B가 만든 판정 API(FastAPI)의 주소예요. 예: http://localhost:8000 · 끝에 /checks는 빼요. 아직 API가 없으면 ‘연습용 대상’을 골라 주세요."
        : !IsHttpAddress(address) ? "http:// 또는 https://로 시작하는 주소를 넣어 주세요. 예: http://localhost:8000"
        : EndsWithCheckPath(address) ? "주소 끝의 /checks는 빼 주세요. QA가 자동으로 붙여서 보내요."
        : $"시험 문자를 {address.TrimEnd('/')}/checks 로 보내요. 예상 정답은 API에 보내지 않고 QA 안에서만 비교해요.";

    private void RefreshExecutionTarget()
    {
        if (TargetChoicePanel is null || ApiTargetRadio is null || ExecutionApiField is null || ApiUrlHint is null) return;
        var usesApi = SelectedSuite is TestSuite.Verdict or TestSuite.Load;
        var apiChosen = usesApi && ApiTargetRadio.IsChecked == true;
        TargetChoicePanel.Visibility = usesApi ? Visibility.Visible : Visibility.Collapsed;
        ExecutionApiField.Visibility = apiChosen ? Visibility.Visible : Visibility.Collapsed;
        ApiUrlHint.Text = apiChosen
            ? DescribeApiAddress(ApiUrlBox.Text.Trim())
            : usesApi ? "연습용 판정기로 시험해요. 서버가 없어도 돼요." : "연습용 파이프라인으로 시험해요.";
    }

    // ---------- 홈: 시험 영역 카드 ----------

    // 홈의 영역 카드(서비스 · 앱 · QA 도구)를 누르면 그 영역의 카드와 설명으로 바꾼다
    private void HomeArea_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
            SelectHomeArea(tag switch { "app" => TestArea.App, "qatool" => TestArea.QaTool, _ => TestArea.Service });
    }

    /// <summary>홈에서 고른 시험 영역의 카드만 보여 준다. 앱 영역은 앱이 나오기 전까지 준비 중 안내를 보여 준다.</summary>
    /// <param name="area">보여 줄 시험 영역</param>
    private void SelectHomeArea(TestArea area)
    {
        if (HomeAreaDescription is null || HomeAppArea is null || HomeQaToolCards is null) return;
        ServiceAreaTab.IsChecked = area == TestArea.Service;
        AppAreaTab.IsChecked = area == TestArea.App;
        QaToolAreaTab.IsChecked = area == TestArea.QaTool;
        HomeAreaDescription.Text = area.Describe();
        HomeAreaHint.Text = area == TestArea.App ? "아래에서 서버를 켜고 앱의 API 주소를 바꿔 주세요" : "아래 시험 카드를 누르면 오른쪽에서 조건을 확인해요";
        HomeSuiteCards.Visibility = area == TestArea.Service ? Visibility.Visible : Visibility.Collapsed;
        HomeAppArea.Visibility = area == TestArea.App ? Visibility.Visible : Visibility.Collapsed;
        HomeQaToolCards.Visibility = area == TestArea.QaTool ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- 홈: 여정 지도 · 다음 할 일 ----------

    private void InitializeChecklist()
    {
        _checklistSteps.Add(new(ChecklistStepKind.PickTest, 1, "시험 고르기", true, false));
        _checklistSteps.Add(new(ChecklistStepKind.RunTest, 2, "실행하기", false, false));
        _checklistSteps.Add(new(ChecklistStepKind.OpenReport, 3, "보고서 열기", false, false));
        _checklistSteps.Add(new(ChecklistStepKind.ReopenHistory, 4, "기록 다시 보기", false, true));
        JourneyItems.ItemsSource = _checklistSteps;
        UpdateChecklist();
    }

    /// <summary>앱을 쓰다가 해당 동작을 하면 그 단계를 완료로 표시한다.</summary>
    private void CompleteStep(ChecklistStepKind kind)
    {
        var step = _checklistSteps.FirstOrDefault(item => item.Kind == kind);
        if (step is null || step.IsDone) return;
        step.IsDone = true;
        UpdateChecklist();
    }

    /// <summary>여정 지도와 홈의 '다음 할 일' 배너를 지금 단계에 맞춘다.</summary>
    private void UpdateChecklist()
    {
        var current = _checklistSteps.FirstOrDefault(item => !item.IsDone);
        foreach (var step in _checklistSteps) step.IsCurrent = ReferenceEquals(step, current);
        for (var i = 0; i < _checklistSteps.Count; i++)
        {
            bool Reached(int index) => _checklistSteps[index].IsDone || _checklistSteps[index].IsCurrent;
            _checklistSteps[i].LeftActive = i > 0 && _checklistSteps[i - 1].IsDone && Reached(i);
            _checklistSteps[i].RightActive = i < _checklistSteps.Count - 1 && _checklistSteps[i].IsDone && Reached(i + 1);
        }
        var done = _checklistSteps.Count(item => item.IsDone);
        JourneyProgressText.Text = $"{done}/{_checklistSteps.Count} 완료";
        (NextStepTitle.Text, NextStepHint.Text, NextStepButtonText.Text) = current?.Kind switch
        {
            ChecklistStepKind.PickTest => ("빠른 점검을 골라 볼까요?", "서버 없이 기본 문자 4건으로 10초면 끝나요.", "빠른 점검 고르기"),
            ChecklistStepKind.RunTest => ("빠른 점검을 실행해 볼까요?", "연습용 대상으로 기본 문자 4건을 보내요. 약 10초 걸려요.", "지금 실행하기"),
            ChecklistStepKind.OpenReport => ("보고서를 열어 볼까요?", "브라우저에서 항목별 결과와 설명을 자세히 볼 수 있어요.", "보고서 열기"),
            ChecklistStepKind.ReopenHistory => ("지난 결과를 다시 열어 볼까요?", "기록에서 실행 하나를 고르면 그때 결과를 다시 보여 줘요.", "기록 보기"),
            _ => ("오늘도 빠른 점검 한 번 해 볼까요?", "처음 사용하는 순서를 모두 마쳤어요. 원하는 시험을 골라 보세요.", "빠른 점검 고르기"),
        };
        HomeSubtitleText.Text = current is null ? "오늘은 무엇을 점검해 볼까요?" : "아래 길을 따라가면 첫 점검을 끝낼 수 있어요.";
    }

    /// <summary>여정 지도와 '다음 할 일'을 처음 상태로 되돌린다.</summary>
    private void ResetJourney()
    {
        foreach (var step in _checklistSteps) step.IsDone = false;
        _coachShownCount = 0;
        UpdateChecklist();
    }

    // 다음 행동 말풍선은 앱을 켠 뒤 처음 몇 번만 보여 준다. 익숙해진 뒤에는 완료 카드와 상태 표시줄만으로 충분하다
    private const int CoachShowLimit = 3;
    private int _coachShownCount;

    /// <summary>
    /// 방금 끝난 시험을 완료 카드·상태 표시줄 색 띠·다음 행동 말풍선으로 알린다.
    /// </summary>
    /// <param name="run">방금 끝난 실행 기록</param>
    private void ShowCompletion(TestRun run)
    {
        var unavailable = run.Results.Count(row => row.IsSystemFailure);
        ResultHeroTitle.Text = "시험이 끝났어요";
        ResultHeroIcon.Symbol = unavailable > 0 ? Symbol.Warning24 : Symbol.Checkmark24;

        var (tone, message) = unavailable > 0
            ? (StatusTone.Fail, $"시험이 끝났지만 {unavailable}개 항목의 결과를 받지 못했어요. 항목을 골라 오류 내용을 확인해 보세요. 보고서도 저장했어요.")
            : run.FailedCount > 0
                ? (StatusTone.Notice, $"시험이 끝났어요. 다시 볼 항목 {run.FailedCount}개를 골라 자세히 확인해 보세요. 보고서도 저장했어요.")
                : (StatusTone.Pass, "시험이 끝났어요. 모든 항목이 통과했어요. 보고서도 저장했어요.");
        ShowStatus(message, tone, showReportActions: true);

        if (_coachShownCount >= CoachShowLimit || ResultsGrid.Items.Count == 0) return;
        _coachShownCount++;
        ResultCoachText.Text = unavailable > 0 ? "이 줄을 눌러 어떤 오류가 났는지 먼저 확인해 보세요."
            : run.FailedCount > 0 ? "이 줄을 눌러 예상과 실제가 어떻게 다른지 비교해 보세요."
            : "항목을 눌러 입력과 실제 결과를 확인해 보세요.";
        ResultCoach.Visibility = Visibility.Visible;
    }

    // ---------- 시험 중 진행 카드 ----------

    // 진행 카드의 지난 시간·남은 시간·단계를 0.5초마다 다시 그린다
    private readonly System.Windows.Threading.DispatcherTimer _runningTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DateTimeOffset _runStartedAt;
    // 부하·복구 시험처럼 보내는 시간이 정해진 시험만 남은 시간을 알 수 있다. 나머지는 null
    private double? _plannedSendSeconds;

    /// <summary>
    /// 시험을 시작하면 결과 자리에 진행 카드를 띄운다. 무엇을 하는 중인지, 얼마나 지났고 남았는지, 지금 단계를 보여 준다.
    /// </summary>
    /// <param name="options">지금 실행하는 시험의 조건 (시험 종류, 보내는 시간·건수)</param>
    private void StartRunningProgress(CommandLineOptions options)
    {
        var measurement = options.Measurement;
        _runStartedAt = DateTimeOffset.Now;
        _plannedSendSeconds = measurement?.DurationSeconds;
        RunningTitleText.Text = $"{options.Suite.ToKoreanName()}을 진행하고 있어요";
        RunningHintText.Text = options.Suite switch
        {
            TestSuite.Pipeline => "중복·순서 변경·지연 조건으로 이벤트를 보내고 Bronze·Silver·Gold 건수를 모으고 있어요.",
            TestSuite.Load when measurement is not null =>
                $"약 {measurement.DurationSeconds}초 동안 초당 {measurement.EventsPerSecond:0.##}건씩 보내면서 응답 시간과 반영 시간을 재고 있어요.",
            TestSuite.Recovery when measurement is not null =>
                $"{measurement.DurationSeconds}초 동안 보내다가 처리를 {measurement.StopSeconds}초 멈춘 뒤 다시 켜고, 따라잡는 시간을 재고 있어요.",
            TestSuite.SelfCheck => "답을 이미 아는 입력으로 QA 도구의 채점기·정답 계산·보고서를 확인하고 있어요.",
            _ => "시험 문자를 하나씩 보내고 돌아온 판정을 받고 있어요.",
        };
        RunningProgressBar.IsIndeterminate = _plannedSendSeconds is null;
        RunningProgressBar.Value = 0;
        RunningCard.Visibility = Visibility.Visible;
        _runningTimer.Tick -= RunningTimer_Tick;
        _runningTimer.Tick += RunningTimer_Tick;
        UpdateRunningProgress();
        _runningTimer.Start();
    }

    private void StopRunningProgress()
    {
        _runningTimer.Stop();
        RunningCard.Visibility = Visibility.Collapsed;
    }

    private void RunningTimer_Tick(object? sender, EventArgs e) => UpdateRunningProgress();

    // 보내는 시간이 정해진 시험은 지난 시간으로 막대를 채우고, 다 보낸 뒤에는 '결과 모으기' 단계로 넘긴다
    private void UpdateRunningProgress()
    {
        var elapsed = (DateTimeOffset.Now - _runStartedAt).TotalSeconds;
        RunningElapsedText.Text = $"{elapsed:0}초 지남";
        if (_plannedSendSeconds is not { } planned)
        {
            RunningRemainingText.Text = "곧 끝나요";
            SetRunningStep(1);
            return;
        }

        var sending = elapsed < planned;
        RunningProgressBar.Value = Math.Min(100, elapsed / planned * 100);
        RunningRemainingText.Text = sending ? $"보내기 약 {Math.Ceiling(planned - elapsed):0}초 남음" : "보내기 끝 · 반영을 기다리는 중";
        SetRunningStep(sending ? 1 : 2);
    }

    /// <summary>진행 카드의 단계 표시에서 지금 단계는 청록, 지난 단계는 체크, 남은 단계는 회색으로 칠한다.</summary>
    /// <param name="current">지금 단계 번호 (1 입력 보내기 · 2 결과 모으기 · 3 채점·보고서 저장)</param>
    private void SetRunningStep(int current)
    {
        var steps = new[] { (RunStepSend, RunStepSendText, "입력 보내기"), (RunStepCollect, RunStepCollectText, "결과 모으기"),
            (RunStepReport, RunStepReportText, "채점·보고서 저장") };
        for (var index = 0; index < steps.Length; index++)
        {
            var (pill, label, name) = steps[index];
            var number = index + 1;
            var isCurrent = number == current;
            var isDone = number < current;
            pill.Background = isCurrent ? (Brush)FindResource("AccentBrush")
                : isDone ? (Brush)FindResource("AccentBackgroundBrush") : (Brush)FindResource("HoverBrush");
            label.Foreground = isCurrent ? Brushes.White : isDone ? (Brush)FindResource("CalloutInkBrush") : (Brush)FindResource("MutedBrush");
            label.FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal;
            label.Text = $"{(isDone ? "✓" : number.ToString())} {name}";
        }
    }

    private void HideCoach() => ResultCoach.Visibility = Visibility.Collapsed;

    private void CloseCoach_Click(object sender, RoutedEventArgs e) => HideCoach();

    // 결과 표를 한 번이라도 누르면 말풍선이 할 일을 다 한 것이므로 닫는다
    private void ResultsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => HideCoach();

    /// <summary>설정 화면에서 안내를 처음 상태로 되돌린다.</summary>
    private void ResetChecklist_Click(object sender, RoutedEventArgs e)
    {
        ResetJourney();
        SelectPage("home");
    }

    /// <summary>'다음 할 일' 버튼: 지금 단계를 대신 열어 준다.</summary>
    private async void NextStep_Click(object sender, RoutedEventArgs e)
    {
        switch (_checklistSteps.FirstOrDefault(item => !item.IsDone)?.Kind)
        {
            case ChecklistStepKind.RunTest:
                await RunPresetAsync("quick");
                break;
            case ChecklistStepKind.OpenReport:
                if (_lastHtmlReportPath is not null) OpenReportButton_Click(OpenReportButton, e);
                else ShowStatus("먼저 시험을 한 번 실행해 주세요. 끝나면 보고서를 열 수 있어요.", false);
                break;
            case ChecklistStepKind.ReopenHistory:
                SelectPage("reports");
                break;
            default:
                SuiteCard_Click(HomeVerdictCard, e);
                break;
        }
    }

    /// <summary>홈 요약 카드: 통과율 원, 이번 주 실행 수, 마지막 실행의 다시 볼 항목 수.</summary>
    private void ShowHomeSummary(IReadOnlyList<TestRun> runs)
    {
        var scored = runs.Where(run => run.TotalCount > 0).ToArray();
        var rate = scored.Length == 0 ? 0 : scored.Average(run => run.PassRate) / 100;
        HomeRateText.Text = scored.Length == 0 ? "—" : $"{rate:P0}".Replace(" ", "");
        HomeRateArc.Data = RingArc(rate, 104, 11);
        var weekAgo = DateTimeOffset.Now.AddDays(-7);
        HomeWeekText.Text = $"{runs.Count(run => run.StartedAt >= weekAgo)}회";
        var latest = runs.OrderByDescending(run => run.StartedAt).FirstOrDefault();
        HomeRecheckText.Text = $"{latest?.FailedCount ?? 0}건";
    }

    /// <summary>위쪽(12시)에서 시계 방향으로 ratio만큼 그린 원호. 0이면 아무것도 그리지 않는다.</summary>
    private static Geometry RingArc(double ratio, double size, double thickness)
    {
        if (ratio <= 0) return Geometry.Empty;
        ratio = Math.Min(ratio, 0.9999);
        var radius = (size - thickness) / 2;
        var center = size / 2;
        var angle = ratio * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Point(center, center - radius) };
        figure.Segments.Add(new ArcSegment(new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle)),
            new Size(radius, radius), 0, ratio > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry([figure]);
    }

    /// <summary>배너 안쪽(그라데이션·장식 원)을 둥근 모서리 모양으로 자른다.</summary>
    private void ClipHomeHero(object sender, SizeChangedEventArgs e) =>
        HomeHeroContent.Clip = new RectangleGeometry(new Rect(e.NewSize), 22, 22);

    // ---------- 결과 안내 ----------

    private void ShowExecutionError(string message)
    {
        ShowStatus(message, true);
        // 시트에서 시작하다 막히면 시트 안에서 바로 고칠 수 있게 시트에 알린다
        if (IsSheetOpen)
        {
            SheetError.Message = message;
            SheetError.IsOpen = true;
            return;
        }
        ExecutionError.Message = message;
        ExecutionError.IsOpen = true;
        ResultsSection.Visibility = Visibility.Collapsed;
        SelectPage("run");
    }

    private static void ValidateMeasurement(Measurement.MeasurementSettings settings, TestSuite suite)
    {
        if (settings.DurationSeconds < 1) throw new ArgumentException("보내는 시간은 1초 이상으로 입력해 주세요.");
        if (!double.IsFinite(settings.EventsPerSecond) || settings.EventsPerSecond <= 0)
            throw new ArgumentException("1초에 보낼 건수는 0보다 큰 숫자로 입력해 주세요.");
        if (suite == TestSuite.Recovery && (settings.StopSeconds < 1
            || settings.StopAfterSeconds + settings.StopSeconds >= settings.DurationSeconds))
            throw new ArgumentException("멈추는 시간을 줄여 주세요. 전체 시간의 1/4 지점에서 멈춘 뒤, 다시 처리할 시간이 남아 있어야 해요.");
        settings.Validate(suite);
    }

    private void ShowResultSummary(TestRun run)
    {
        _displayedRun = run;
        ResultsSection.Visibility = Visibility.Visible;
        ExecutionError.IsOpen = false;
        var unavailable = run.Results.Count(row => row.IsSystemFailure);
        ResultSummaryTitle.Text = run.TotalCount == 0 ? "확인할 항목이 없어요."
            : unavailable > 0 ? $"{unavailable}개 항목의 결과를 받지 못했어요."
            : run.FailedCount > 0 ? $"{run.FailedCount}개 항목을 다시 살펴보면 좋겠어요."
            : $"{run.TotalCount}개 항목이 모두 기준을 통과했어요.";
        ResultSummaryHint.Text = unavailable > 0 ? "연결이나 응답에 문제가 있었어요. 항목을 골라 오류 내용을 먼저 확인해 주세요."
            : run.HasProvisionalLabels ? "아직 사람이 확인하지 않은 임시 정답과 비교한 결과예요. 이 결과만으로 서비스 품질을 판단하지 말아 주세요."
            : run.FailedCount > 0 ? "예상과 다르거나 기준을 넘긴 항목부터 보여드려요. 하나를 골라 어떤 차이가 있는지 확인해 주세요."
            : "각 항목을 골라 입력과 실제 결과를 확인할 수 있어요. 자세한 내용은 보고서에도 담았어요.";
    }

    private void ExplainResult(ResultRow row)
    {
        DetailExplanationText.Text = row.IsSystemFailure ? "판정이 틀린 경우와 달라요. 시험 대상이 결과를 돌려주지 못했어요. 주소와 서버 상태, 응답 시간을 확인해 주세요."
            : _displayedRun?.HasProvisionalLabels == true ? "아직 검토하지 않은 정답과 비교했어요. 먼저 시험 문자의 예상 정답과 근거가 맞는지 확인해 주세요."
            : row.IsPassed ? "미리 정한 정답이나 기준을 통과했어요. 다른 입력과 실행 조건에서도 같은지 함께 살펴보세요."
            : _displayedRun?.UsedFakeTarget == true ? "연습용 대상에는 일부러 틀리는 규칙이 들어 있어요. 미리 정한 값과 실제 값이 어떻게 다른지 비교해 보세요."
            : row.HasThreshold ? "실제로 측정한 값이 정해 둔 기준을 넘었어요. 같은 조건으로 다시 확인하고 응답 속도나 처리 지연을 살펴보세요."
            : "예상한 값과 다른 결과가 나왔어요. 입력과 정답을 먼저 확인하고, 같은 조건으로 다시 시험해 보세요.";
        if (row.CheckDescription.Length > 0) DetailExplanationText.Text += "\n\n" + row.CheckDescription;
    }

    private static string DescribeTarget(TestRun run) => run.TargetName == QaRunner.SelfCheck.QaToolSelfChecker.TargetName
        ? "QA 러너 자체"
        : run.TargetName == QaRunner.AppMock.MockVerdictServer.TargetName ? "앱 → 가짜 판정 서버"
        : run.UsedFakeTarget
        ? run.TargetName.Contains("ApiVerdictService", StringComparison.Ordinal)
            ? "연결한 API + 연습용 파이프라인" : "연습용 대상"
        : run.TargetName.Contains("Kafka", StringComparison.Ordinal) ? "연결한 데이터 파이프라인" : "연결한 API";
}
