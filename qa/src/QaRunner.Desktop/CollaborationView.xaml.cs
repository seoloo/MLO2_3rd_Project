using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QaRunner.Collaboration;
using QaRunner.Contracts;
using QaRunner.Verdicts;

namespace QaRunner.Desktop;

/// <summary>생성 사례·로컬 QA·공유 기록의 연결 구조를 살펴보는 준비 화면. 실제 외부 연결은 후속 작업이다.</summary>
public partial class CollaborationView : UserControl
{
    // 같은 앱에서 창을 두 개 열면 실행자를 바꿔 기록 공유를 흉내 낼 수 있다. 앱 종료 시 초기화된다.
    private static readonly DemoTeamRunClient DemoClient = new();
    private GeneratedCaseSet? _caseSet;
    public bool IsBusy { get; private set; }
    public event Action<string>? NavigationRequested;
    public event Action<bool>? BusyChanged;
    public event Action? WorkflowCompleted;
    public event Action? DatasetChanged;
    public string ReportDirectory { get; set; } = CommandLineOptions.DefaultReportDirectory;
    private string _rulesPath = CommandLineOptions.DefaultCasesPathFor(QaRunner.Models.TestSuite.Verdict);
    public string DatasetDisplay => _caseSet?.DatasetId ?? System.IO.Path.GetFileName(_rulesPath);

    public CollaborationView()
    {
        InitializeComponent();
        ActorBox.Text = Environment.UserName;
        LoadRuleCases(CommandLineOptions.DefaultCasesPathFor(QaRunner.Models.TestSuite.Verdict));
        ShowSharingMode();
        ShowPage("cases");
    }

    /// <summary>메인 탐색에서 기존 사례·생성·검수·팀 기록 콘텐츠를 선택한다.</summary>
    public void ShowPage(string page)
    {
        ContentTabs.SelectedIndex = page switch { "team" => 1, "ai" => 2, "review" => 3, _ => 0 };
        PageTitle.Text = page switch { "team" => "공유 기록", "ai" => "AI 문자 만들기", "review" => "예상 정답 검토", _ => "시험 문자" };
        PageSubtitle.Text = page switch
        {
            "team" => "기록을 고르면 누가 무엇을 시험했는지, 결과가 어땠는지 볼 수 있어요.",
            "ai" => "AI는 아직 연결 전이에요. 먼저 예제 문자로 사용 방법을 익혀 보세요.",
            "review" => "문자와 예상 정답을 함께 읽어 보세요. 정답을 고치거나 승인하는 기능은 준비 중이에요.",
            _ => "문자마다 어떤 정답을 예상했는지 볼 수 있어요. 정답은 시험 대상에 보내지 않아요.",
        };
        SettingsExpander.Visibility = page == "team" ? Visibility.Visible : Visibility.Collapsed;
        LoadRulesButton.Visibility = page == "cases" ? Visibility.Visible : Visibility.Collapsed;
        GenerateButton.Visibility = page is "cases" or "ai" ? Visibility.Visible : Visibility.Collapsed;
        ExecuteButton.Visibility = page is "cases" or "team" ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.Visibility = page == "team" ? Visibility.Visible : Visibility.Collapsed;
        // 공유 방식·업로드 결과 안내는 공유 기록 화면에서만 보여 준다. 사례 화면 안내는 DatasetText가 맡는다
        SharingStatus.Visibility = page == "team" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>기본 사례 파일 기준을 표시한다. 기존 JSON 정답을 검수한 AI 라벨로 바꾸지는 않는다.</summary>
    public void LoadRuleCases(string path)
    {
        if (IsBusy) return;
        try
        {
            var cases = TestCaseLoader.LoadFromFile(path);
            if (cases.Any(item => item is null || string.IsNullOrWhiteSpace(item.CaseId)
                || string.IsNullOrWhiteSpace(item.Message) || string.IsNullOrWhiteSpace(item.ExpectedVerdict)))
                throw new ArgumentException("문자 번호, 내용과 예상 정답이 모두 들어 있는지 확인해 주세요.");
            var rows = cases.Select(item => new CaseRow(
                item.CaseId, item.Message, item.ExpectedVerdict, "기본 파일에 적힌 정답", "사례 파일 기준")).ToArray();
            _caseSet = null;
            _rulesPath = path;
            ShowCases(rows);
            DatasetText.Text = $"문자 {rows.Length}건을 불러왔어요. 예상 정답은 사례 파일에 적힌 값이에요.";
            ExecuteButton.IsEnabled = false;
            DatasetChanged?.Invoke();
        }
        catch (Exception exception) when (IsExpectedError(exception))
        {
            DatasetText.Text = $"문자를 불러오지 못했어요. {exception.Message}";
        }
    }

    private void OpenPage_Click(object sender, RoutedEventArgs e) => Navigate((string)((Button)sender).Tag);

    private void LoadRules_Click(object sender, RoutedEventArgs e) =>
        LoadRuleCases(CommandLineOptions.DefaultCasesPathFor(QaRunner.Models.TestSuite.Verdict));

    private void ShowCases(CaseRow[] rows)
    {
        CasesGrid.ItemsSource = rows;
        ReviewCasesList.ItemsSource = rows;
        ReviewCasesList.SelectedIndex = rows.Length > 0 ? 0 : -1;
        if (rows.Length == 0)
        {
            ReviewMessage.Text = "아직 불러온 문자가 없어요. 시험 문자에서 파일을 골라 주세요.";
            ReviewVerdict.Text = ReviewRationale.Text = "—";
        }
    }

    private void Navigate(string page)
    {
        if (NavigationRequested is not null) NavigationRequested(page);
        else ShowPage(page);
    }

    private void ReviewCase_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (ReviewCasesList.SelectedItem is not CaseRow row) return;
        ReviewMessage.Text = row.Message;
        ReviewVerdict.Text = row.ExpectedVerdict;
        ReviewRationale.Text = row.Rationale;
        // 사람이 확인한 정답이면 초록, 아직 확인 전이면 노랑 배지로 구분한다
        var isConfirmed = row.ReviewText is "사람 확인 완료" or "사례 파일 기준";
        ReviewStateText.Text = row.ReviewText;
        ReviewStateText.Foreground = (Brush)FindResource(isConfirmed ? "PassBrush" : "NoticeBrush");
        ReviewStateBadge.Background = (Brush)FindResource(isConfirmed ? "PassBackgroundBrush" : "NoticeBackgroundBrush");
        ReviewStateBadge.Visibility = Visibility.Visible;
    }

    // TODO: 모델과 생성 정책 확정 후 ICaseGenerator를 RunpodCaseGenerator로 교체한다.
    private async void Generate_Click(object sender, RoutedEventArgs e) => await LoadDemoCasesAsync();

    /// <summary>고정 예제를 불러온다. 실제 AI 요청과 구분해서 표시한다.</summary>
    public async Task LoadDemoCasesAsync()
    {
        if (IsBusy) return;
        SetBusy(true);
        try
        {
            ICaseGenerator generator = new DemoCaseGenerator();
            _caseSet = await generator.GenerateAsync(new CaseGenerationRequest("인증번호 전달·외부 앱 설치"));
            _caseSet.Validate();
            ShowCases(_caseSet.Cases.Select(testCase => new CaseRow(testCase.CaseId, testCase.Message,
                VerdictCodes.ToKorean(testCase.ExpectedVerdict), testCase.Rationale,
                testCase.ReviewState == LabelReviewState.HumanReviewed ? "사람 확인 완료" : "사람 확인 전")).ToArray());
            // 안내는 한 줄로: 무엇을 가져왔는지와 정답 확인이 필요하다는 점을 함께 알린다
            DatasetText.Text = $"미리 만든 예제 문자 {_caseSet.Cases.Count}건을 가져왔어요. AI가 새로 만든 문자는 아니고, 정답을 아직 사람이 확인하지 않았으니 근거도 함께 읽어 주세요.";
            DatasetChanged?.Invoke();
            Navigate("cases");
        }
        finally { SetBusy(false); }
    }

    // 기존 채점기로 가짜 대상을 시험하고, 선택한 공유 방식으로 시작·완료 상태를 전달한다.
    private async void Execute_Click(object sender, RoutedEventArgs e) => await RunDemoAsync();

    /// <summary>화면과 UI 스모크 점검이 같은 비동기 실행 경로를 사용한다.</summary>
    public async Task RunDemoAsync()
    {
        if (_caseSet is null || IsBusy) return;
        SetBusy(true);
        try
        {
            var client = CreateClient();
            using var ownedClient = client as IDisposable;
            var workflow = new TeamQaWorkflow(client);
            var outcome = await workflow.RunAsync(_caseSet, ActorBox.Text.Trim(), EnvironmentBox.Text.Trim(),
                CodeVersionBox.Text.Trim(), new FakeVerdictService(), nameof(FakeVerdictService), ReportDirectory);

            // 조회에 실패해도 저장한 로컬 결과와 업로드 경고가 보이도록 상태를 먼저 표시한다.
            SharingStatus.Text = outcome.SyncError is null
                ? $"예제 시험을 마쳤어요 · {outcome.Run.PassedCount}/{outcome.Run.Results.Count} 일치 · 로컬 보고서: {outcome.Reports.HtmlReportPath}"
                : $"이 PC에 결과를 저장했지만 공유하지 못했어요. {outcome.SyncError}\n다시 전송할 때 사용할 파일: {outcome.SnapshotPath}";
            if (outcome.SyncError is null)
                await RefreshRunsAsync(client);
            Navigate("team");
            WorkflowCompleted?.Invoke();
        }
        catch (Exception exception) when (IsExpectedError(exception))
        {
            SharingStatus.Text = DatasetText.Text = $"예제 시험을 마치지 못했어요. {exception.Message}";
        }
        finally { SetBusy(false); }
    }

    // TODO: 착수 후 자동 갱신·중단 시 취소·서버 인증을 추가한다. 지금은 명시적으로 새로고침한다.
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            var client = CreateClient();
            using var ownedClient = client as IDisposable;
            await RefreshRunsAsync(client);
            SharingStatus.Text = "공유 기록을 불러왔어요. 하나를 골라 결과를 확인해 보세요.";
            Navigate("team");
        }
        catch (Exception exception) when (IsExpectedError(exception))
        {
            SharingStatus.Text = $"기록을 불러오지 못했어요. {exception.Message}";
        }
        finally { SetBusy(false); }
    }

    private ITeamRunClient CreateClient() => SharingModeBox.SelectedIndex == 1
        ? new HttpTeamRunClient(new Uri("http://127.0.0.1:5085/"))
        : DemoClient;

    private async Task RefreshRunsAsync(ITeamRunClient client)
    {
        TeamRunsGrid.ItemsSource = (await client.ListAsync()).Select(run => new TeamRunRow(run)).ToArray();
        SharedResultsGrid.ItemsSource = null;
        SelectedRunText.Text = "기록을 고르면 아래에서 예상 정답과 실제 판정을 비교할 수 있어요.";
    }

    // 현재는 목록의 스냅샷을 표시한다. FindAsync는 추후 페이지 조회와 상세 조회에 쓴다.
    private void TeamRun_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (TeamRunsGrid.SelectedItem is not TeamRunRow row) return;
        SelectedRunText.Text = $"{row.ActorName} · {row.EnvironmentName} · {row.CodeVersion} · {row.StateText}"
            + (row.Run.Error is null ? "" : $" · {row.Run.Error}");
        SharedResultsGrid.ItemsSource = row.Run.Results.Select(result => new
        {
            result.CaseId, result.Expected, result.Actual, result.Error,
            ResultText = result.IsPassed ? "일치" : "불일치",
        });
    }

    private void SharingMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TeamRunsGrid is null || SharingStatus is null) return;
        TeamRunsGrid.ItemsSource = null;
        SharedResultsGrid.ItemsSource = null;
        SelectedRunText.Text = "기록을 고르면 아래에서 예상 정답과 실제 판정을 비교할 수 있어요.";
        ShowSharingMode();
    }

    private void ShowSharingMode() => SharingStatus.Text = SharingModeBox.SelectedIndex == 1
        ? "같은 PC의 공유 서버를 먼저 켜 주세요. 여러 앱에서 결과를 볼 수 있지만 서버를 끄면 공유 기록은 사라져요."
        : "이 앱 안에서만 결과를 모아 보여드려요. 다른 PC로 보내지 않으며, 앱을 끄면 이 공유 목록은 사라져요.";

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        SettingsPanel.IsEnabled = !busy;
        ContentTabs.IsEnabled = !busy;
        LoadRulesButton.IsEnabled = !busy;
        GenerateButton.IsEnabled = !busy;
        ExecuteButton.IsEnabled = !busy && _caseSet is not null;
        RefreshButton.IsEnabled = !busy;
        BusyChanged?.Invoke(busy);
    }

    private sealed record CaseRow(string CaseId, string Message, string ExpectedVerdict, string Rationale, string ReviewText);

    private static bool IsExpectedError(Exception exception) => exception is
        ArgumentException or HttpRequestException or TaskCanceledException or System.IO.IOException or System.Text.Json.JsonException;

    /// <summary>공유 실행을 화면 표시용 이름으로 바꾼다. 판정 불일치는 실행 오류와 구분한다.</summary>
    private sealed record TeamRunRow(SharedQaRun Run)
    {
        public string ActorName => Run.ActorName;
        public string EnvironmentName => Run.EnvironmentName;
        public string CodeVersion => Run.CodeVersion;
        public string StateText => Run.State switch
        {
            SharedRunState.Running => "실행 중", SharedRunState.Completed => "완료", _ => "실행 오류",
        };
        public string ReviewText => Run.IsProvisional ? "정답 확인 전" : "사람 확인 완료";
        public string CountText => Run.State == SharedRunState.Completed ? $"{Run.PassedCount} / {Run.Results.Count}" : "-";
        public string StartedAtText => Run.StartedAt.ToLocalTime().ToString("MM-dd HH:mm:ss");
    }
}
