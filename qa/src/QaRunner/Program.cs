using System.Text.Json;
using QaRunner;
using QaRunner.Models;
using QaRunner.Reports;
using QaRunner.Verdicts;

// 사용법 (qa 폴더에서):
//   dotnet run --project src/QaRunner -- [--cases 입력값파일] [--reports 보고서폴더] [--api-url API주소]
// 옵션을 주지 않으면 cases/rules.json을 읽고, 가짜 판정 대상으로 채점하고, reports 폴더에 보고서를 저장한다.
var options = CommandLineOptions.Parse(args);

List<TestCase> testCases;
IVerdictService verdictService;
try
{
    // 1. 입력값 파일 읽기
    testCases = TestCaseLoader.LoadFromFile(options.TestCaseFilePath);

    // 2. 판정 대상 정하기: --api-url을 주면 실제 API, 주지 않으면 가짜 판정 대상
    verdictService = options.ApiBaseUrl is null
        ? new FakeVerdictService()
        : new ApiVerdictService(ApiVerdictService.CreateHttpClient(options.ApiBaseUrl));
}
catch (Exception exception) when (exception is IOException or JsonException or ArgumentException)
{
    // 입력값 파일이 없거나 형식이 틀렸거나, API 주소가 잘못된 경우.
    // 채점을 시작하지 못했으므로 시험 실패(1)와 구분되는 종료 코드 2로 끝낸다.
    Console.Error.WriteLine($"실행 준비 중 오류: {exception.Message}");
    return 2;
}

// 3. 채점 (시작·끝 시각을 함께 기록해 실행 기록을 만든다)
var startedAt = DateTimeOffset.Now;
var verdictChecker = new VerdictChecker(verdictService);
var testResults = await verdictChecker.CheckAllAsync(testCases);
var finishedAt = DateTimeOffset.Now;

var testRun = new TestRun(
    RunId: RunIdGenerator.CreateUnique(startedAt, options.ReportDirectory),
    StartedAt: startedAt,
    FinishedAt: finishedAt,
    TestCaseFilePath: options.TestCaseFilePath,
    VerdictServiceName: verdictService.GetType().Name,
    Results: testResults);

// 4. 결과를 콘솔에 출력
Console.WriteLine($"실행 번호 {testRun.RunId}\n");
foreach (var testResult in testRun.Results)
{
    var resultLabel = testResult.IsPassed ? "통과" : "실패";
    Console.WriteLine($"{resultLabel}  {testResult.CaseId}  정답={testResult.ExpectedVerdict}  실제={testResult.ActualVerdict}");
}
Console.WriteLine($"\n전체 {testRun.TotalCount}건 · 통과 {testRun.PassedCount} · 실패 {testRun.FailedCount}");

var systemFailureCount = testRun.Results.Count(testResult => testResult.IsSystemFailure);
if (systemFailureCount > 0)
{
    // 판정을 아예 못 받은 케이스가 있으면 이유를 하나 보여 준다 (API가 꺼져 있는 경우 등)
    var firstError = testRun.Results.First(testResult => testResult.IsSystemFailure).ErrorMessage;
    Console.WriteLine($"시스템 실패 {systemFailureCount}건 · 예: {firstError}");
}

// 5. 보고서 파일 저장 (JSON은 다른 프로그램용, HTML은 사람이 보는 용도)
var jsonReportPath = JsonReportWriter.Save(testRun, options.ReportDirectory);
var htmlReportPath = HtmlReportWriter.Save(testRun, options.ReportDirectory);
Console.WriteLine($"\nJSON 보고서: {Path.GetFullPath(jsonReportPath)}");
Console.WriteLine($"HTML 보고서: {Path.GetFullPath(htmlReportPath)}");

// 6. 종료 코드: 실패가 하나라도 있으면 1, 모두 통과하면 0 (실행 준비 오류는 위에서 2)
//    CI는 이 값을 보고 시험 성공·실패를 판단한다.
return testRun.FailedCount > 0 ? 1 : 0;
