using System.Net;
using System.Text;
using QaRunner.Models;

namespace QaRunner.Reports;

/// <summary>
/// 실행 기록을 브라우저로 여는 HTML 보고서로 저장한다.
/// 스타일까지 파일 하나에 들어 있어서 그대로 팀원에게 보내거나 제출 자료에 붙일 수 있다.
/// </summary>
public static class HtmlReportWriter
{
    /// <summary>
    /// 실행 기록을 "{실행 번호}.html" 파일로 저장하고 저장한 경로를 돌려준다.
    /// </summary>
    /// <param name="testRun">저장할 실행 기록</param>
    /// <param name="reportDirectory">보고서를 저장할 폴더. 없으면 새로 만든다</param>
    public static string Save(TestRun testRun, string reportDirectory)
    {
        Directory.CreateDirectory(reportDirectory);

        var reportFilePath = Path.Combine(reportDirectory, $"{testRun.RunId}.html");
        File.WriteAllText(reportFilePath, BuildHtml(testRun));

        return reportFilePath;
    }

    /// <summary>
    /// 실행 기록으로 HTML 문서 내용을 만든다. 파일로 저장하지 않고 내용만 필요할 때(테스트 등) 쓴다.
    /// </summary>
    /// <param name="testRun">보고서로 만들 실행 기록</param>
    public static string BuildHtml(TestRun testRun)
    {
        var resultRows = new StringBuilder();

        // 실패한 항목을 위에 먼저 보여 준다. 같은 결과끼리는 원래 순서를 유지한다
        foreach (var testResult in testRun.Results.OrderBy(testResult => testResult.IsPassed))
        {
            var rowClass = testResult.IsPassed ? "pass" : "fail";
            var resultLabel = testResult.IsPassed ? "통과" : "실패";

            // 시스템 실패면 실제 값 아래에 실패 이유를 작게 붙인다
            var errorDetail = testResult.IsSystemFailure
                ? $"""<div class="error">{Encode(testResult.ErrorMessage!)}</div>"""
                : "";

            resultRows.AppendLine($"""
                <tr class="{rowClass}">
                  <td class="case-id">{Encode(testResult.CaseId)}</td>
                  <td class="check-name">{Encode(testResult.CheckName)}</td>
                  <td class="input">{Encode(testResult.Input)}</td>
                  <td>{Encode(testResult.Expected)}</td>
                  <td>{Encode(testResult.Actual)}{errorDetail}</td>
                  <td><span class="badge">{resultLabel}</span></td>
                </tr>
                """);
        }

        // 가짜 시험 대상으로 돌린 결과라면 실제 성능으로 오해하지 않도록 상단에 알린다
        var fakeNotice = testRun.UsedFakeTarget
            ? """<p class="notice">가짜 시험 대상으로 실행한 결과입니다. 실제 시스템의 성능이 아닙니다.</p>"""
            : "";

        if (testRun.HasProvisionalLabels)
            fakeNotice += """<p class="notice">검수 전 예상 정답을 포함한 탐색 시험입니다. 통과율은 공식 정확도가 아닙니다.</p>""";

        // CSS의 중괄호와 겹치지 않도록 $$ 문자열을 쓴다. 값은 {{ }} 안에 넣는다
        return $$"""
            <!doctype html>
            <html lang="ko">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>QA 실행 결과 {{testRun.RunId}}</title>
            <style>
              body { font-family: 'Noto Sans KR', 'Malgun Gothic', sans-serif; color: #1d3436; background: #fff; margin: 0; padding: 32px 16px; }
              main { max-width: 960px; margin: 0 auto; }
              .eyebrow { color: #0f7a73; font-weight: 700; font-size: 13px; }
              h1 { font-size: 28px; margin: 4px 0 6px; }
              .meta { color: #5f7173; font-size: 14px; margin: 0 0 16px; }
              .notice { background: #fdf4e7; color: #8a5a12; border-radius: 8px; padding: 10px 14px; font-size: 14px; }
              .summary { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; margin: 16px 0 20px; }
              .tile { border: 1px solid #d6e4e1; border-radius: 10px; padding: 10px 14px; }
              .tile .label { color: #5f7173; font-size: 12px; font-weight: 700; }
              .tile .value { font-size: 24px; font-weight: 800; }
              .tile.passed .value { color: #0f7a73; }
              .tile.failed .value { color: #b4232a; }
              table { width: 100%; border-collapse: collapse; font-size: 14px; }
              th { text-align: left; color: #5f7173; font-size: 12px; padding: 8px; border-bottom: 1px solid #d6e4e1; }
              td { padding: 8px; border-bottom: 1px solid #edf2f1; vertical-align: top; }
              td.case-id { white-space: nowrap; font-weight: 700; }
              td.check-name { white-space: nowrap; }
              td.input { color: #33494b; }
              tr.fail { background: #fff8f8; }
              .error { color: #b4232a; font-size: 12px; margin-top: 2px; }
              .badge { font-size: 12px; font-weight: 800; padding: 2px 10px; border-radius: 999px; white-space: nowrap; }
              tr.pass .badge { background: #e2f3ee; color: #0f7a73; }
              tr.fail .badge { background: #fdecec; color: #b4232a; }
              @media (max-width: 640px) { .summary { grid-template-columns: repeat(2, 1fr); } }
            </style>
            </head>
            <body>
            <main>
              <div class="eyebrow">JEONGBO JIKIM / QA RUNNER</div>
              <h1>QA 실행 결과 · {{Encode(testRun.AreaName)}} › {{Encode(testRun.SuiteName)}}</h1>
              <p class="meta">
                실행 번호 {{testRun.RunId}} · 시작 {{testRun.StartedAt:yyyy-MM-dd HH:mm:ss}} · 걸린 시간 {{testRun.Duration.TotalSeconds:0.00}}초<br>
                입력 {{Encode(testRun.CasesPath)}} · 시험 대상 {{Encode(testRun.TargetName)}}
              </p>
              {{fakeNotice}}
              <section class="summary">
                <div class="tile"><div class="label">전체</div><div class="value">{{testRun.TotalCount}}</div></div>
                <div class="tile passed"><div class="label">통과</div><div class="value">{{testRun.PassedCount}}</div></div>
                <div class="tile failed"><div class="label">실패</div><div class="value">{{testRun.FailedCount}}</div></div>
                <div class="tile"><div class="label">통과율</div><div class="value">{{testRun.PassRate:0.0}}%</div></div>
              </section>
              <table>
                <thead><tr><th>케이스</th><th>확인 항목</th><th>입력</th><th>정답</th><th>실제</th><th>결과</th></tr></thead>
                <tbody>
            {{resultRows}}
                </tbody>
              </table>
            </main>
            </body>
            </html>
            """;
    }

    // 문자 안에 <, >, & 같은 기호가 있어도 HTML이 깨지지 않도록 바꿔 준다
    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
