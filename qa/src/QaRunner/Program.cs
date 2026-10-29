using QaRunner;
using QaRunner.Models;
using QaRunner.Pipeline;

// 사용법 (qa 폴더에서):
//   dotnet run --project src/QaRunner -- [--suite verdict|pipeline|load|recovery|selfcheck] [--cases 경로] [--reports 보고서폴더] [--api-url API주소]
//                                         [--duration 초] [--rate 초당건수] [--stop-seconds 초]   ← 부하·복구 시험용
//                                         [--kafka 주소 --mongo 연결문자열] [--kafka-topic] [--mongo-db] [--spark-container]   ← 실제 파이프라인
// 옵션을 주지 않으면 판정 시험을 cases/rules.json으로, 가짜 판정 대상에 대해 돌리고 reports 폴더에 보고서를 저장한다.
// 실제 절차(준비 → 채점 → 보고서 저장)는 데스크탑 앱과 함께 쓰는 QaRunService에 있다.

CommandLineOptions options;
PreparedRun preparedRun;

try
{
    // 1. 옵션 읽기와 시험 준비 (입력 파일 읽기, 시험 대상 고르기)
    options = CommandLineOptions.Parse(args);
    preparedRun = QaRunService.Prepare(options);
}
catch (Exception exception) when (QaRunService.IsPreparationError(exception))
{
    // 채점을 시작하지 못했으므로 시험 실패(1)와 구분되는 종료 코드 2로 끝낸다.
    Console.Error.WriteLine($"실행 준비 중 오류: {exception.Message}");
    return 2;
}

// 2. 채점 (부하·복구 시험은 정해진 시간 동안 돌기 때문에 미리 알려 준다)
if (options.Measurement is not null)
    Console.WriteLine($"{options.Suite.ToKoreanName()}을 약 {options.Measurement.DurationSeconds}초 동안 실행합니다...\n");

TestRun testRun;
try
{
    testRun = await QaRunService.RunAsync(preparedRun, options.ReportDirectory);
}
catch (PipelineReadException exception)
{
    // 실제 파이프라인(MongoDB) 결과를 읽지 못하면 채점 자체가 안 되므로 종료 코드 2로 끝낸다
    Console.Error.WriteLine($"시험 대상에 연결하지 못했습니다: {exception.Message}");
    return 2;
}

// 3. 결과를 콘솔에 출력
Console.WriteLine($"{testRun.AreaName} › {testRun.SuiteName} · 실행 번호 {testRun.RunId} · 시험 대상 {testRun.TargetName}\n");
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

// 4. 보고서 파일 저장 (JSON은 다른 프로그램용, HTML은 사람이 보는 용도, 대시보드는 지금까지의 실행 기록 추이)
var savedReports = QaRunService.SaveReports(testRun, options.ReportDirectory);
Console.WriteLine($"\nJSON 보고서: {Path.GetFullPath(savedReports.JsonReportPath)}");
Console.WriteLine($"HTML 보고서: {Path.GetFullPath(savedReports.HtmlReportPath)}");
Console.WriteLine($"대시보드:    {Path.GetFullPath(savedReports.DashboardPath)}");

// 5. 종료 코드: 실패가 하나라도 있으면 1, 모두 통과하면 0 (실행 준비 오류는 위에서 2)
//    CI는 이 값을 보고 시험 성공·실패를 판단한다.
return testRun.FailedCount > 0 ? 1 : 0;
