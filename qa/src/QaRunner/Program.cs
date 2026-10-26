using QaRunner;
using QaRunner.Models;
using QaRunner.Reports;
using QaRunner.Verdicts;

// 사용법 (qa 폴더에서):
//   dotnet run --project src/QaRunner -- [--cases 입력값파일] [--reports 보고서폴더]
// 옵션을 주지 않으면 cases/rules.json을 읽고 reports 폴더에 보고서를 저장한다.
var options = CommandLineOptions.Parse(args);

// 1. 입력값 파일 읽기
var testCases = TestCaseLoader.LoadFromFile(options.TestCaseFilePath);

// 2. 판정 대상 정하기 (FastAPI가 준비되면 이 줄만 바꾼다)
IVerdictService verdictService = new FakeVerdictService();

// 3. 채점 (시작·끝 시각을 함께 기록해 실행 기록을 만든다)
var startedAt = DateTimeOffset.Now;
var verdictChecker = new VerdictChecker(verdictService);
var testResults = await verdictChecker.CheckAllAsync(testCases);
var finishedAt = DateTimeOffset.Now;

var testRun = new TestRun(
    RunId: TestRun.CreateRunId(startedAt),
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

// 5. 보고서 파일 저장 (JSON은 다른 프로그램용, HTML은 사람이 보는 용도)
var jsonReportPath = JsonReportWriter.Save(testRun, options.ReportDirectory);
var htmlReportPath = HtmlReportWriter.Save(testRun, options.ReportDirectory);
Console.WriteLine($"\nJSON 보고서: {Path.GetFullPath(jsonReportPath)}");
Console.WriteLine($"HTML 보고서: {Path.GetFullPath(htmlReportPath)}");

// 6. 종료 코드: 실패가 하나라도 있으면 1, 모두 통과하면 0
//    CI는 이 값을 보고 시험 성공·실패를 판단한다.
return testRun.FailedCount > 0 ? 1 : 0;
