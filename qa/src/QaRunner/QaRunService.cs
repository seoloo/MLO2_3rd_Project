using System.Text.Json;
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
public sealed record SavedReports(string JsonReportPath, string HtmlReportPath);

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
    public static PreparedRun Prepare(CommandLineOptions options)
    {
        if (options.Suite == TestSuite.Verdict)
        {
            // 판정 시험: 입력값 파일을 읽고, API 주소가 있으면 실제 API, 없으면 가짜 판정 대상으로 채점
            var testCases = TestCaseLoader.LoadFromFile(options.CasesPath);
            IVerdictService verdictService = options.ApiBaseUrl is null
                ? new FakeVerdictService()
                : new ApiVerdictService(ApiVerdictService.CreateHttpClient(options.ApiBaseUrl));

            var verdictChecker = new VerdictChecker(verdictService);
            return new PreparedRun(
                options.Suite,
                options.CasesPath,
                TargetName: verdictService.GetType().Name,
                RunAllChecks: _ => verdictChecker.CheckAllAsync(testCases));
        }

        // 파이프라인 시험: 시나리오 폴더를 읽고 가짜 파이프라인으로 채점
        // (Kafka·MongoDB 연결 코드가 생기면 여기서 실제 파이프라인을 고르게 한다)
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
    /// 실행 기록을 JSON·HTML 보고서로 저장한다.
    /// </summary>
    public static SavedReports SaveReports(TestRun testRun, string reportDirectory) => new(
        JsonReportPath: JsonReportWriter.Save(testRun, reportDirectory),
        HtmlReportPath: HtmlReportWriter.Save(testRun, reportDirectory));

    /// <summary>
    /// 채점을 시작하기도 전에 난 문제인지 판단한다.
    /// 옵션이 틀렸거나, 입력값 파일·시나리오 폴더가 없거나 형식이 틀렸거나, API 주소가 잘못된 경우다.
    /// </summary>
    public static bool IsPreparationError(Exception exception) =>
        exception is IOException or JsonException or ArgumentException;
}
