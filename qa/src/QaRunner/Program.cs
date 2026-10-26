using System.Text.Json;
using QaRunner;
using QaRunner.Models;
using QaRunner.Pipeline;
using QaRunner.Reports;
using QaRunner.Verdicts;

// 사용법 (qa 폴더에서):
//   dotnet run --project src/QaRunner -- [--suite verdict|pipeline] [--cases 경로] [--reports 보고서폴더] [--api-url API주소]
// 옵션을 주지 않으면 판정 시험을 cases/rules.json으로, 가짜 판정 대상에 대해 돌리고 reports 폴더에 보고서를 저장한다.

CommandLineOptions options;
string targetName;                                  // 보고서에 남길 시험 대상 이름
Func<string, Task<List<TestResult>>> runAllChecks;  // 실행 번호를 받아 채점하고 결과를 돌려주는 작업

try
{
    options = CommandLineOptions.Parse(args);

    if (options.Suite == TestSuite.Verdict)
    {
        // 판정 시험: 입력값 파일을 읽고, --api-url을 주면 실제 API, 주지 않으면 가짜 판정 대상으로 채점
        var testCases = TestCaseLoader.LoadFromFile(options.CasesPath);
        IVerdictService verdictService = options.ApiBaseUrl is null
            ? new FakeVerdictService()
            : new ApiVerdictService(ApiVerdictService.CreateHttpClient(options.ApiBaseUrl));

        var verdictChecker = new VerdictChecker(verdictService);
        targetName = verdictService.GetType().Name;
        runAllChecks = _ => verdictChecker.CheckAllAsync(testCases);
    }
    else
    {
        // 파이프라인 시험: 시나리오 폴더를 읽고 가짜 파이프라인으로 채점
        // (Kafka·MongoDB 연결 코드가 생기면 여기서 실제 파이프라인을 고르게 한다)
        var scenarios = PipelineScenarioLoader.LoadFromDirectory(options.CasesPath);
        var fakePipeline = new FakePipeline();

        // 가짜 파이프라인은 보내는 즉시 반영되므로 Gold를 기다릴 필요가 없다
        var pipelineChecker = new PipelineChecker(fakePipeline, fakePipeline, catchUpTimeout: TimeSpan.Zero);
        targetName = nameof(FakePipeline);
        runAllChecks = runId => pipelineChecker.CheckAllAsync(scenarios, runId);
    }
}
catch (Exception exception) when (exception is IOException or JsonException or ArgumentException)
{
    // 옵션이 틀렸거나, 입력값 파일·시나리오 폴더가 없거나 형식이 틀렸거나, API 주소가 잘못된 경우.
    // 채점을 시작하지 못했으므로 시험 실패(1)와 구분되는 종료 코드 2로 끝낸다.
    Console.Error.WriteLine($"실행 준비 중 오류: {exception.Message}");
    return 2;
}

// 1. 채점 (실행 번호를 먼저 만든다. 파이프라인 시험은 보내는 이벤트마다 실행 번호를 붙인다)
var startedAt = DateTimeOffset.Now;
var runId = RunIdGenerator.CreateUnique(startedAt, options.ReportDirectory);
var testResults = await runAllChecks(runId);
var finishedAt = DateTimeOffset.Now;

var testRun = new TestRun(
    RunId: runId,
    SuiteName: options.Suite.ToKoreanName(),
    StartedAt: startedAt,
    FinishedAt: finishedAt,
    CasesPath: options.CasesPath,
    TargetName: targetName,
    Results: testResults);

// 2. 결과를 콘솔에 출력
Console.WriteLine($"{testRun.SuiteName} · 실행 번호 {testRun.RunId} · 시험 대상 {testRun.TargetName}\n");
foreach (var testResult in testRun.Results)
{
    var resultLabel = testResult.IsPassed ? "통과" : "실패";
    Console.WriteLine($"{resultLabel}  {testResult.CaseId}  {testResult.CheckName}  정답={testResult.Expected}  실제={testResult.Actual}");
}
Console.WriteLine($"\n전체 {testRun.TotalCount}건 · 통과 {testRun.PassedCount} · 실패 {testRun.FailedCount}");

if (testRun.SystemFailureCount > 0)
{
    // 값을 아예 못 받은 항목이 있으면 이유를 하나 보여 준다 (API가 꺼져 있는 경우 등)
    var firstError = testRun.Results.First(testResult => testResult.IsSystemFailure).ErrorMessage;
    Console.WriteLine($"시스템 실패 {testRun.SystemFailureCount}건 · 예: {firstError}");
}

// 3. 보고서 파일 저장 (JSON은 다른 프로그램용, HTML은 사람이 보는 용도)
var jsonReportPath = JsonReportWriter.Save(testRun, options.ReportDirectory);
var htmlReportPath = HtmlReportWriter.Save(testRun, options.ReportDirectory);
Console.WriteLine($"\nJSON 보고서: {Path.GetFullPath(jsonReportPath)}");
Console.WriteLine($"HTML 보고서: {Path.GetFullPath(htmlReportPath)}");

// 4. 종료 코드: 실패가 하나라도 있으면 1, 모두 통과하면 0 (실행 준비 오류는 위에서 2)
//    CI는 이 값을 보고 시험 성공·실패를 판단한다.
return testRun.FailedCount > 0 ? 1 : 0;
