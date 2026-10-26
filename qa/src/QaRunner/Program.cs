using QaRunner;
using QaRunner.Verdicts;

// 사용법 (qa 폴더에서): dotnet run --project src/QaRunner [입력값 파일 경로]
// 경로를 주지 않으면 기본 입력값 파일을 쓴다.
var testCaseFilePath = args.Length > 0 ? args[0] : "cases/rules.json";

// 1. 입력값 파일 읽기
var testCases = TestCaseLoader.LoadFromFile(testCaseFilePath);

// 2. 판정 대상 정하기 (FastAPI가 준비되면 이 줄만 바꾼다)
IVerdictService verdictService = new FakeVerdictService();

// 3. 채점
var verdictChecker = new VerdictChecker(verdictService);
var testResults = await verdictChecker.CheckAllAsync(testCases);

// 4. 결과 출력
foreach (var testResult in testResults)
{
    var resultLabel = testResult.IsPassed ? "통과" : "실패";
    Console.WriteLine($"{resultLabel}  {testResult.CaseId}  정답={testResult.ExpectedVerdict}  실제={testResult.ActualVerdict}");
}

var failedCount = testResults.Count(testResult => !testResult.IsPassed);
var passedCount = testResults.Count - failedCount;
Console.WriteLine($"\n전체 {testResults.Count}건 · 통과 {passedCount} · 실패 {failedCount}");

// 5. 종료 코드: 실패가 하나라도 있으면 1, 모두 통과하면 0
//    CI는 이 값을 보고 시험 성공·실패를 판단한다.
return failedCount > 0 ? 1 : 0;
