using System.IO;
using System.Windows;
using System.Windows.Controls;
using QaRunner.Models;

namespace QaRunner.Desktop;

public partial class MainWindow
{
    private TestRun? _displayedRun;
    private TestSuite HomeSuite => HomePipelineChoice.IsChecked == true ? TestSuite.Pipeline
        : HomeLoadChoice.IsChecked == true ? TestSuite.Load
        : HomeRecoveryChoice.IsChecked == true ? TestSuite.Recovery : TestSuite.Verdict;

    private void HomeSuite_Checked(object sender, RoutedEventArgs e) => RefreshHomeRecommendation();
    private void HomeTarget_Checked(object sender, RoutedEventArgs e) => RefreshHomeRecommendation();

    private void RefreshHomeRecommendation()
    {
        if (HomeRecommendationTitle is null || HomeApiTarget is null || HomeApiFields is null) return;
        var suite = HomeSuite;
        var usesApi = suite is TestSuite.Verdict or TestSuite.Load;
        HomeApiTarget.Visibility = usesApi ? Visibility.Visible : Visibility.Collapsed;
        if (!usesApi) HomeDemoTarget.IsChecked = true;
        HomeApiFields.Visibility = usesApi && HomeApiTarget.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HomeTargetHint.Text = !usesApi ? "이 시험은 연습용 파이프라인을 사용해요. 별도 서버는 필요하지 않아요."
            : HomeApiTarget.IsChecked == true ? "시험할 API 주소를 입력하세요. 예상 정답은 QA 안에서만 비교하고 문자만 전송해요."
            : "서버 연결 없이 기본 예제로 실행하고 결과를 확인할 수 있어요.";
        HomeRecommendationTitle.Text = suite switch
        {
            TestSuite.Pipeline => "선택한 시험: 데이터 처리",
            TestSuite.Load => "선택한 시험: 부하와 응답 속도",
            TestSuite.Recovery => "선택한 시험: 중단 후 복구",
            _ => "선택한 시험: 문자 판정",
        };
        HomeRecommendationText.Text = suite switch
        {
            TestSuite.Pipeline => "이벤트를 중복해서 보내거나 순서를 바꿔 보내요. 저장·정제·집계 건수가 맞는지 비교해요.",
            TestSuite.Load => "기본값은 30초 동안 초당 5건이에요. 다음 화면에서 시간과 건수를 바꿀 수 있어요.",
            TestSuite.Recovery => "기본값은 60초 동안 보내며 15초 멈추는 조건이에요. 마지막 집계와 회복 시간을 확인해요.",
            _ => "문자와 예상 정답이 들어 있는 파일을 사용해요. 기본 문자 4건으로 시작하거나 다음 화면에서 파일을 바꿀 수 있어요.",
        };
        HomeErrorText.Visibility = Visibility.Collapsed;
    }

    private void PrepareHomeRun_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning || CollaborationPage.IsBusy) return;
        var api = HomeApiTarget.IsChecked == true ? HomeApiBox.Text.Trim() : "";
        if (HomeApiTarget.IsChecked == true && !IsHttpAddress(api))
        {
            HomeErrorText.Text = "API 주소를 확인해 주세요. http://localhost:8000처럼 http:// 또는 https://로 시작하는 주소가 필요해요.";
            HomeErrorText.Visibility = Visibility.Visible;
            HomeApiBox.Focus();
            return;
        }
        SelectSuite(HomeSuite);
        ApiUrlBox.Text = api;
        ResultsSection.Visibility = Visibility.Collapsed;
        ExecutionError.Visibility = Visibility.Collapsed;
        SelectPage("run");
        ExecutionScroll.ScrollToTop();
        ShowStatus("조건을 확인한 뒤 ‘시험 시작하기’를 눌러 주세요.", false);
    }

    private static bool IsHttpAddress(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrWhiteSpace(uri.Host);

    private void RefreshExecutionTarget()
    {
        var usesApi = SelectedSuite is TestSuite.Verdict or TestSuite.Load;
        var connected = usesApi && ApiUrlBox.Text.Trim().Length > 0;
        ExecutionApiField.Visibility = usesApi && (connected || HomeApiTarget.IsChecked == true)
            ? Visibility.Visible : Visibility.Collapsed;
        ApiUrlHint.Text = connected ? "입력한 주소의 API를 시험해요. 예상 정답은 QA 안에서만 비교해요."
            : usesApi ? "지금은 연습용 판정기를 사용해요. 내 API를 시험하려면 시작하기나 설정에서 주소를 입력해 주세요."
            : "지금은 연습용 파이프라인을 사용해요. 실제 서버에는 연결하지 않아요.";
    }

    private void ShowExecutionError(string message)
    {
        ExecutionErrorText.Text = message;
        ExecutionError.Visibility = Visibility.Visible;
        ResultsSection.Visibility = Visibility.Collapsed;
        SelectPage("run");
        ShowStatus(message, true);
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
        ExecutionError.Visibility = Visibility.Collapsed;
        var unavailable = run.Results.Count(row => row.IsSystemFailure);
        ResultSummaryTitle.Text = run.TotalCount == 0 ? "확인할 항목이 없어요."
            : unavailable > 0 ? $"{unavailable}개 항목의 결과를 받지 못했어요."
            : run.FailedCount > 0 ? $"{run.FailedCount}개 항목을 다시 살펴보면 좋겠어요."
            : $"{run.TotalCount}개 항목이 모두 기준을 통과했어요.";
        ResultSummaryHint.Text = unavailable > 0 ? "연결이나 응답에 문제가 있었어요. 항목을 골라 오류 내용을 먼저 확인해 주세요."
            : run.HasProvisionalLabels ? "아직 사람이 확인하지 않은 임시 정답과 비교한 결과예요. 이 결과만으로 서비스 품질을 판단하지 말아 주세요."
            : run.FailedCount > 0 ? "예상과 다르거나 기준을 넘긴 항목부터 보여드려요. 하나를 골라 어떤 차이가 있는지 확인해 주세요."
            : "각 항목을 골라 입력과 실제 결과를 확인할 수 있어요. 자세한 내용은 아래 보고서에도 담았어요.";
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
