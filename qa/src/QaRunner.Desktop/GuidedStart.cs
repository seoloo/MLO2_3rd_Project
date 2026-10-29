using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using QaRunner.Models;

namespace QaRunner.Desktop;

/// <summary>처음 사용하는 순서에서 완료를 표시할 단계.</summary>
public enum ChecklistStepKind { PickTest, RunTest, OpenReport, ReopenHistory }

/// <summary>시험 화면 오른쪽 '처음 사용하는 순서'의 한 줄. 완료·현재 단계가 바뀌면 화면이 따라 바뀐다.</summary>
public sealed class ChecklistStep(ChecklistStepKind kind, int number, string title, string hint) : INotifyPropertyChanged
{
    private bool _isDone;
    private bool _isCurrent;
    public ChecklistStepKind Kind { get; } = kind;
    public int Number { get; } = number;
    public string Title { get; } = title;
    public string Hint { get; } = hint;
    public bool IsDone { get => _isDone; set => Set(ref _isDone, value); }
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// 시험 화면의 빠른 시작 카드(프리셋), 시험 대상 선택, 처음 사용하는 순서와 말풍선 안내.
/// </summary>
public partial class MainWindow
{
    private TestRun? _displayedRun;
    private readonly ObservableCollection<ChecklistStep> _checklistSteps = [];

    // ---------- 빠른 시작 카드 ----------

    private async void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string preset }) await RunPresetAsync(preset);
    }

    /// <summary>
    /// 카드에 묶어 둔 조건을 적용하고 바로 시험한다. '내 API 판정'은 주소가 필요해서 세부 조건만 열어 준다.
    /// 화면 점검(UI 스모크)도 같은 경로를 쓴다.
    /// </summary>
    public async Task RunPresetAsync(string preset)
    {
        if (_isRunning || CollaborationPage.IsBusy) return;
        CompleteStep(ChecklistStepKind.PickTest);
        switch (preset)
        {
            case "api":
                SelectSuite(TestSuite.Verdict);
                ApiTargetRadio.IsChecked = true;
                AdvancedOptions.IsExpanded = true;
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
                // 같은 시험을 다시 고르면 기본값이 다시 채워지지 않으므로 카드의 시간·건수를 직접 넣는다
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
        await RunSelectedSuiteAsync();
    }

    // ---------- 시험 대상 (연습용 / 내 판정 API) ----------

    private void Target_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isReady || ApiUrlBox is null) return;
        // 연습용으로 바꾸면 이전에 넣은 API 주소를 시험 대상에서 뺀다
        if (PracticeTargetRadio.IsChecked == true && ApiUrlBox.Text.Length > 0) ApiUrlBox.Text = "";
        RefreshExecutionTarget();
        if (ApiTargetRadio.IsChecked == true && IsLoaded) ApiUrlBox.Focus();
    }

    private static bool IsHttpAddress(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrWhiteSpace(uri.Host);

    private void RefreshExecutionTarget()
    {
        if (TargetChoicePanel is null || ApiTargetRadio is null || ExecutionApiField is null || ApiUrlHint is null) return;
        var usesApi = SelectedSuite is TestSuite.Verdict or TestSuite.Load;
        var apiChosen = usesApi && ApiTargetRadio.IsChecked == true;
        TargetChoicePanel.Visibility = usesApi ? Visibility.Visible : Visibility.Collapsed;
        ExecutionApiField.Visibility = apiChosen ? Visibility.Visible : Visibility.Collapsed;
        ApiUrlHint.Text = apiChosen
            ? ApiUrlBox.Text.Trim().Length > 0 ? "입력한 주소의 API를 시험해요. 예상 정답은 QA 안에서만 비교해요." : "시험할 API 주소를 넣어 주세요."
            : usesApi ? "연습용 판정기로 시험해요. 서버가 없어도 돼요." : "연습용 파이프라인으로 시험해요.";
    }

    // ---------- 처음 사용하는 순서 · 말풍선 ----------

    private void InitializeChecklist()
    {
        _checklistSteps.Add(new(ChecklistStepKind.PickTest, 1, "시험 하나 고르기", "‘빠른 점검’ 카드부터 눌러 보세요."));
        _checklistSteps.Add(new(ChecklistStepKind.RunTest, 2, "끝까지 실행해 보기", "결과가 아래에 정리돼요."));
        _checklistSteps.Add(new(ChecklistStepKind.OpenReport, 3, "보고서 열어 보기", "브라우저로 자세한 결과를 봐요."));
        _checklistSteps.Add(new(ChecklistStepKind.ReopenHistory, 4, "기록에서 다시 열기", "기록 메뉴에서 지난 결과를 골라요."));
        ChecklistItems.ItemsSource = _checklistSteps;
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

    private void UpdateChecklist()
    {
        var current = _checklistSteps.FirstOrDefault(item => !item.IsDone);
        foreach (var step in _checklistSteps) step.IsCurrent = ReferenceEquals(step, current);
        var done = _checklistSteps.Count(item => item.IsDone);
        ChecklistProgress.Value = done;
        ChecklistProgressText.Text = $"{done} / {_checklistSteps.Count}";
        ChecklistCollapsedText.Text = current is null ? "순서 완료" : $"순서 {done}/{_checklistSteps.Count}";
        if (current is null) SetChecklistExpanded(false);
    }

    private void SetChecklistExpanded(bool expanded)
    {
        ChecklistPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ChecklistCollapsed.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ToggleChecklist_Click(object sender, RoutedEventArgs e) =>
        SetChecklistExpanded(ChecklistPanel.Visibility != Visibility.Visible);

    private void CloseCoachHint_Click(object sender, RoutedEventArgs e) => CoachHint.Visibility = Visibility.Collapsed;

    /// <summary>설정 화면에서 안내를 처음 상태로 되돌린다.</summary>
    private void ResetChecklist_Click(object sender, RoutedEventArgs e)
    {
        foreach (var step in _checklistSteps) step.IsDone = false;
        UpdateChecklist();
        SetChecklistExpanded(true);
        CoachHint.Visibility = Visibility.Visible;
        SelectPage("run");
    }

    /// <summary>'지금 하기'를 누르면 그 단계를 대신 열어 준다.</summary>
    private async void ChecklistAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChecklistStep step) return;
        switch (step.Kind)
        {
            case ChecklistStepKind.PickTest:
                QuickPreset.Focus();
                ShowStatus("‘빠른 점검’ 카드를 눌러 보세요. 서버 없이 바로 끝나요.", false);
                break;
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
        }
    }

    // ---------- 결과 안내 ----------

    private void ShowExecutionError(string message)
    {
        ExecutionError.Message = message;
        ExecutionError.IsOpen = true;
        ResultsSection.Visibility = Visibility.Collapsed;
        SelectPage("run");
        ShowStatus(message, true);
        // 세부 조건을 펼쳐 둔 상태에서도 오류 안내가 화면 안에 보이게 내려 준다
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => ExecutionError.BringIntoView()));
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

    private static string DescribeTarget(TestRun run) => run.UsedFakeTarget
        ? run.TargetName.Contains("ApiVerdictService", StringComparison.Ordinal)
            ? "연결한 API + 연습용 파이프라인" : "연습용 대상"
        : run.TargetName.Contains("Kafka", StringComparison.Ordinal) ? "연결한 데이터 파이프라인" : "연결한 API";
}
