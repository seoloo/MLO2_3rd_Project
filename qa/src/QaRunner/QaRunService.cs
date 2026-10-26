using System.Text.Json;
using QaRunner.Measurement;
using QaRunner.Models;
using QaRunner.Pipeline;
using QaRunner.Reports;
using QaRunner.Verdicts;

namespace QaRunner;

/// <summary>
/// 시험을 준비한 결과: 무엇을 대상으로 어떻게 채점할지까지 정해 둔 상태.
/// </summary>
/// <param name="Suite">시험 종류</param>
/// <param name="CasesPath">입력값 파일 또는 시나리오 폴더 경로</param>
/// <param name="TargetName">시험 대상 이름 (보고서에 남김)</param>
/// <param name="RunAllChecks">실행 번호를 받아 채점하고 결과를 돌려주는 작업</param>
public sealed record PreparedRun(
    TestSuite Suite,
    string CasesPath,
    string TargetName,
    Func<string, Task<List<TestResult>>> RunAllChecks);

/// <summary>
/// 저장한 보고서 파일 경로.
/// </summary>
/// <param name="JsonReportPath">이번 실행의 JSON 보고서</param>
/// <param name="HtmlReportPath">이번 실행의 HTML 보고서</param>
/// <param name="DashboardPath">지금까지의 실행 기록을 모은 대시보드 (실행할 때마다 다시 만든다)</param>
public sealed record SavedReports(string JsonReportPath, string HtmlReportPath, string DashboardPath);

/// <summary>
/// 콘솔 프로그램과 데스크탑 앱이 함께 쓰는 실행 절차: 준비 → 채점 → 보고서 저장.
/// 화면에 무엇을 어떻게 보여 줄지는 각 프로그램이 정한다.
/// </summary>
public static class QaRunService
{
    /// <summary>
    /// 옵션대로 시험을 준비한다. 입력값 파일·시나리오 폴더를 읽고 시험 대상을 고른다.
    /// 준비하다 생긴 문제(파일 없음, 형식 오류, 잘못된 주소)는 예외로 알린다. IsPreparationError로 구분할 수 있다.
    /// </summary>
    /// <param name="options">시험 종류, 입력 경로, API 주소 등</param>
    public static PreparedRun Prepare(CommandLineOptions options) => options.Suite switch
    {
        TestSuite.Verdict => PrepareVerdictSuite(options),
        TestSuite.Pipeline => PreparePipelineSuite(options),
        TestSuite.Load => PrepareLoadSuite(options),
        TestSuite.Recovery => PrepareRecoverySuite(options),
        _ => throw new ArgumentException($"알 수 없는 시험 종류입니다: {options.Suite}"),
    };

    // 판정 시험: 입력값 파일을 읽고, API 주소가 있으면 실제 API, 없으면 가짜 판정 대상으로 채점
    private static PreparedRun PrepareVerdictSuite(CommandLineOptions options)
    {
        var testCases = TestCaseLoader.LoadFromFile(options.CasesPath);
        var verdictService = CreateVerdictService(options);
        var verdictChecker = new VerdictChecker(verdictService);

        return new PreparedRun(
            options.Suite,
            options.CasesPath,
            TargetName: verdictService.GetType().Name,
            RunAllChecks: _ => verdictChecker.CheckAllAsync(testCases));
    }

    // 파이프라인 시험: 시나리오 폴더를 읽고 가짜 파이프라인으로 채점
    // (Kafka·MongoDB 연결 코드가 생기면 여기서 실제 파이프라인을 고르게 한다)
    private static PreparedRun PreparePipelineSuite(CommandLineOptions options)
    {
        var scenarios = PipelineScenarioLoader.LoadFromDirectory(options.CasesPath);
        var fakePipeline = new FakePipeline();

        // 가짜 파이프라인은 보내는 즉시 반영되므로 Gold를 기다릴 필요가 없다
        var pipelineChecker = new PipelineChecker(fakePipeline, fakePipeline, catchUpTimeout: TimeSpan.Zero);
        return new PreparedRun(
            options.Suite,
            options.CasesPath,
            TargetName: nameof(FakePipeline),
            RunAllChecks: runId => pipelineChecker.CheckAllAsync(scenarios, runId));
    }

    // 부하 시험: API 부하는 판정 대상(가짜 또는 실제 API)에, 스트림 부하는 처리 시간이 있는 가짜 파이프라인에 준다
    private static PreparedRun PrepareLoadSuite(CommandLineOptions options)
    {
        var testCases = TestCaseLoader.LoadFromFile(options.CasesPath);
        if (testCases.Count == 0)
            throw new ArgumentException($"부하 시험에 쓸 문자가 입력값 파일에 없습니다: {options.CasesPath}");

        var settings = GetMeasurementSettings(options);
        var verdictService = CreateVerdictService(options);
        var streamingPipeline = new FakeStreamingPipeline();
        var loadChecker = new LoadChecker(verdictService, testCases, streamingPipeline, streamingPipeline, settings);

        return new PreparedRun(
            options.Suite,
            options.CasesPath,
            TargetName: $"{verdictService.GetType().Name} + {nameof(FakeStreamingPipeline)}",
            RunAllChecks: runId => loadChecker.CheckAllAsync(runId));
    }

    // 복구 시험: 처리 시간이 있는 가짜 파이프라인을 멈췄다 다시 켠다 (입력 파일은 쓰지 않음)
    private static PreparedRun PrepareRecoverySuite(CommandLineOptions options)
    {
        var settings = GetMeasurementSettings(options);
        var streamingPipeline = new FakeStreamingPipeline();
        var recoveryChecker = new RecoveryChecker(streamingPipeline, streamingPipeline, streamingPipeline, settings);

        return new PreparedRun(
            options.Suite,
            CommandLineOptions.NoCasesPath,
            TargetName: nameof(FakeStreamingPipeline),
            RunAllChecks: runId => recoveryChecker.CheckAllAsync(runId));
    }

    // API 주소가 있으면 실제 API, 없으면 가짜 판정 대상
    private static IVerdictService CreateVerdictService(CommandLineOptions options) => options.ApiBaseUrl is null
        ? new FakeVerdictService()
        : new ApiVerdictService(ApiVerdictService.CreateHttpClient(options.ApiBaseUrl));

    // 측정 조건이 없으면 실행안 기준 기본값을 쓰고, 값이 맞는지 확인한다
    private static MeasurementSettings GetMeasurementSettings(CommandLineOptions options)
    {
        var settings = options.Measurement ?? MeasurementSettings.DefaultFor(options.Suite);
        settings.Validate(options.Suite);
        return settings;
    }

    /// <summary>
    /// 준비된 시험을 돌리고 실행 기록을 만든다.
    /// 파이프라인 시험은 보내는 이벤트마다 실행 번호를 붙이므로 실행 번호를 먼저 만든다.
    /// </summary>
    /// <param name="preparedRun">Prepare로 준비한 시험</param>
    /// <param name="reportDirectory">보고서 폴더. 이미 있는 보고서와 실행 번호가 겹치지 않게 확인하는 데 쓴다</param>
    public static async Task<TestRun> RunAsync(PreparedRun preparedRun, string reportDirectory)
    {
        var startedAt = DateTimeOffset.Now;
        var runId = RunIdGenerator.CreateUnique(startedAt, reportDirectory);
        var testResults = await preparedRun.RunAllChecks(runId);
        var finishedAt = DateTimeOffset.Now;

        return new TestRun(
            RunId: runId,
            SuiteName: preparedRun.Suite.ToKoreanName(),
            StartedAt: startedAt,
            FinishedAt: finishedAt,
            CasesPath: preparedRun.CasesPath,
            TargetName: preparedRun.TargetName,
            Results: testResults);
    }

    /// <summary>
    /// 실행 기록을 JSON·HTML 보고서로 저장하고, 이번 실행까지 포함해 대시보드를 다시 만든다.
    /// (대시보드는 저장된 JSON 보고서를 읽으므로 JSON을 먼저 저장해야 한다)
    /// </summary>
    public static SavedReports SaveReports(TestRun testRun, string reportDirectory)
    {
        var jsonReportPath = JsonReportWriter.Save(testRun, reportDirectory);
        var htmlReportPath = HtmlReportWriter.Save(testRun, reportDirectory);
        var dashboardPath = DashboardWriter.Save(reportDirectory);

        return new SavedReports(jsonReportPath, htmlReportPath, dashboardPath);
    }

    /// <summary>
    /// 채점을 시작하기도 전에 난 문제인지 판단한다.
    /// 옵션이 틀렸거나, 입력값 파일·시나리오 폴더가 없거나 형식이 틀렸거나, API 주소가 잘못된 경우다.
    /// </summary>
    public static bool IsPreparationError(Exception exception) =>
        exception is IOException or JsonException or ArgumentException;
}
