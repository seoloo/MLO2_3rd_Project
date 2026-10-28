using System.Net;
using System.Text;
using QaRunner.Measurement;
using QaRunner.Models;

namespace QaRunner.Reports;

/// <summary>
/// 보고서 폴더에 쌓인 실행 기록을 모아 한 화면에서 추이를 보는 HTML 대시보드(dashboard.html)를 만든다.
/// 새 데이터를 만들지 않고 저장된 JSON 보고서만 읽으므로, 실행할 때마다 다시 만들어도 된다.
/// </summary>
public static class DashboardWriter
{
    /// <summary>대시보드 파일 이름. 보고서 폴더 안에 만든다</summary>
    public const string FileName = "dashboard.html";

    // 추이 차트에 그릴 최근 실행 수, 표에 보여 줄 최근 실행 수
    private const int MaxRunsInChart = 20;
    private const int MaxRunsInTable = 10;

    /// <summary>
    /// 부하·복구 시험에서 따로 추이를 그릴 측정 항목.
    /// </summary>
    /// <param name="CheckName">실행 기록에서 찾을 확인 항목 이름</param>
    /// <param name="ShortName">표의 측정값 칸에 쓸 짧은 이름</param>
    /// <param name="Target">목표 시간 (점선으로 표시)</param>
    private sealed record MetricChart(string CheckName, string ShortName, TimeSpan Target);

    /// <summary>
    /// 보고서 폴더의 실행 기록으로 대시보드를 다시 만들고 경로를 돌려준다.
    /// </summary>
    /// <param name="reportDirectory">보고서 폴더</param>
    public static string Save(string reportDirectory)
    {
        Directory.CreateDirectory(reportDirectory);

        var dashboardPath = Path.Combine(reportDirectory, FileName);
        var testRuns = ReportHistoryLoader.LoadAll(reportDirectory);
        File.WriteAllText(dashboardPath, BuildHtml(testRuns, DateTimeOffset.Now));

        return dashboardPath;
    }

    /// <summary>
    /// 실행 기록 목록으로 대시보드 HTML을 만든다.
    /// </summary>
    /// <param name="testRuns">시작 시각 순서의 실행 기록</param>
    /// <param name="generatedAt">대시보드를 만든 시각</param>
    public static string BuildHtml(IReadOnlyList<TestRun> testRuns, DateTimeOffset generatedAt)
    {
        var suiteSections = new StringBuilder();

        // 시험 종류마다 섹션 하나. 한 번도 돌리지 않은 시험은 건너뛴다
        foreach (var testSuite in Enum.GetValues<TestSuite>())
        {
            var suiteName = testSuite.ToKoreanName();
            var suiteRuns = testRuns.Where(testRun => testRun.SuiteName == suiteName).ToList();
            if (suiteRuns.Count > 0)
                suiteSections.Append(BuildSuiteSection(suiteName, suiteRuns, MetricChartsFor(testSuite)));
        }

        var latestRun = testRuns.Count > 0 ? testRuns[^1] : null;
        var summaryTiles = latestRun is null
            ? ""
            : $"""
              <section class="summary">
                <div class="tile"><div class="label">저장된 실행</div><div class="value">{testRuns.Count}회</div></div>
                <div class="tile"><div class="label">마지막 실행</div><div class="value small">{latestRun.StartedAt:MM/dd HH:mm}</div><div class="sub">{Encode(latestRun.SuiteName)}</div></div>
                <div class="tile"><div class="label">마지막 실행 통과율</div><div class="value">{latestRun.PassRate:0.0}%</div><div class="sub">{latestRun.PassedCount} / {latestRun.TotalCount} 통과</div></div>
              </section>
              """;

        var bodyContent = latestRun is null
            ? """<p class="empty">아직 저장된 보고서가 없습니다. QA 러너를 한 번 실행하면 여기에 기록이 쌓입니다.</p>"""
            : suiteSections.ToString();

        // CSS의 중괄호와 겹치지 않도록 $$ 문자열을 쓴다. 값은 {{ }} 안에 넣는다
        return $$"""
            <!doctype html>
            <html lang="ko">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>QA 실행 기록 대시보드</title>
            <style>
              body { font-family: 'Noto Sans KR', 'Malgun Gothic', sans-serif; color: #1d3436; background: #fff; margin: 0; padding: 32px 16px; }
              main { max-width: 980px; margin: 0 auto; }
              .eyebrow { color: #0f7a73; font-weight: 700; font-size: 13px; }
              h1 { font-size: 28px; margin: 4px 0 6px; }
              h2 { font-size: 20px; margin: 36px 0 4px; }
              h3 { font-size: 14px; margin: 0 0 6px; color: #33494b; }
              .meta { color: #5f7173; font-size: 14px; margin: 0 0 16px; }
              .summary { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; margin: 16px 0 8px; }
              .tile { border: 1px solid #d6e4e1; border-radius: 10px; padding: 10px 14px; }
              .tile .label { color: #5f7173; font-size: 12px; font-weight: 700; }
              .tile .value { font-size: 24px; font-weight: 800; }
              .tile .value.small { font-size: 20px; }
              .tile .sub { color: #5f7173; font-size: 12px; }
              .charts { display: grid; grid-template-columns: repeat(2, 1fr); gap: 12px; margin: 10px 0 14px; }
              td.nowrap, th { white-space: nowrap; }
              @media (max-width: 760px) { .charts { grid-template-columns: 1fr; } }
              .chart-card { border: 1px solid #d6e4e1; border-radius: 10px; padding: 12px 14px 6px; }
              svg.chart { width: 100%; height: auto; display: block; }
              svg .grid { stroke: #edf2f1; stroke-width: 1; }
              svg .baseline { stroke: #b9c6c4; stroke-width: 1; }
              svg .axis { fill: #5f7173; font-size: 11px; }
              svg .value { fill: #1d3436; font-size: 12px; font-weight: 700; }
              svg .bar { fill: #0f7a73; }
              svg .hit { fill: transparent; }
              svg .bar-slot:hover .bar { fill: #0a5a55; }
              svg .bar-slot:hover .hit { fill: #f3f8f7; }
              svg .target { stroke: #5f7173; stroke-width: 1.5; stroke-dasharray: 5 4; }
              svg .target-label { fill: #5f7173; font-size: 11px; font-weight: 700; }
              table { width: 100%; border-collapse: collapse; font-size: 13.5px; }
              th { text-align: left; color: #5f7173; font-size: 12px; padding: 7px 8px; border-bottom: 1px solid #d6e4e1; }
              td { padding: 7px 8px; border-bottom: 1px solid #edf2f1; vertical-align: top; }
              td a { color: #0f7a73; font-weight: 700; text-decoration: none; }
              td a:hover { text-decoration: underline; }
              .fake { font-size: 11px; font-weight: 700; color: #8a5a12; background: #fdf4e7; border-radius: 999px; padding: 1px 7px; margin-left: 4px; white-space: nowrap; }
              .empty { color: #5f7173; padding: 24px 0; }
              @media (max-width: 640px) { .summary { grid-template-columns: 1fr; } }
            </style>
            </head>
            <body>
            <main>
              <div class="eyebrow">JEONGBO JIKIM / QA RUNNER</div>
              <h1>QA 실행 기록 대시보드</h1>
              <p class="meta">보고서 폴더에 저장된 실행 기록 {{testRuns.Count}}개로 만들었습니다 · {{generatedAt:yyyy-MM-dd HH:mm:ss}} 기준 · 막대에 마우스를 올리면 실행 정보가 나옵니다.</p>
              {{summaryTiles}}
              {{bodyContent}}
            </main>
            </body>
            </html>
            """;
    }

    // 부하·복구 시험만 측정 항목 차트가 따로 있다
    private static IReadOnlyList<MetricChart> MetricChartsFor(TestSuite testSuite) => testSuite switch
    {
        TestSuite.Load =>
        [
            new MetricChart(LoadChecker.ApiLatencyCheckName, "API p95", LoadChecker.ApiLatencyTarget),
            new MetricChart(LoadChecker.GoldLatencyCheckName, "Gold p95", LoadChecker.GoldLatencyTarget),
        ],
        TestSuite.Recovery => [new MetricChart(RecoveryChecker.RecoveryTimeCheckName, "회복", RecoveryChecker.RecoveryTimeTarget)],
        _ => [],
    };

    // 시험 종류 하나의 섹션: 통과율 추이, 측정 항목 추이, 최근 실행 표
    private static string BuildSuiteSection(string suiteName, List<TestRun> suiteRuns, IReadOnlyList<MetricChart> metricCharts)
    {
        var chartRuns = suiteRuns.TakeLast(MaxRunsInChart).ToList();
        var latestRun = suiteRuns[^1];

        var passRateBars = chartRuns
            .Select(testRun => new ChartBar(
                AxisLabel: $"{testRun.StartedAt:MM/dd HH:mm}",
                Tooltip: $"{testRun.StartedAt:yyyy-MM-dd HH:mm} · 통과율 {testRun.PassRate:0.0}% ({testRun.PassedCount}/{testRun.TotalCount}) · {testRun.TargetName}{(testRun.HasProvisionalLabels ? " · 검수 전 탐색 시험" : "")}",
                Value: testRun.PassRate,
                ValueLabel: $"{testRun.PassRate:0.0}%"))
            .ToList();

        var charts = new StringBuilder();
        charts.Append(ChartCard($"통과율 (최근 {chartRuns.Count}회)",
            SvgBarChart.Build($"{suiteName} 통과율 추이", passRateBars, axisMaximum: 100, unitSuffix: "%")));

        foreach (var metricChart in metricCharts)
        {
            var metricBars = chartRuns
                .Select(testRun =>
                {
                    var seconds = ReportHistoryLoader.FindSeconds(testRun, metricChart.CheckName);
                    var valueText = seconds is null ? "측정값 없음" : $"{seconds:0.00}초";
                    return new ChartBar(
                        AxisLabel: $"{testRun.StartedAt:MM/dd HH:mm}",
                        Tooltip: $"{testRun.StartedAt:yyyy-MM-dd HH:mm} · {metricChart.CheckName} {valueText}",
                        Value: seconds,
                        ValueLabel: valueText);
                })
                .ToList();

            // 세로축은 목표와 가장 큰 값이 모두 들어가게, 정수로 올려 잡는다
            var largestSeconds = metricBars.Max(bar => bar.Value) ?? 0;
            var axisMaximum = Math.Ceiling(Math.Max(metricChart.Target.TotalSeconds * 1.2, largestSeconds * 1.15));

            charts.Append(ChartCard($"{metricChart.CheckName} (목표 {metricChart.Target.TotalSeconds:0}초 이하)",
                SvgBarChart.Build($"{suiteName} {metricChart.CheckName} 추이", metricBars, axisMaximum, "초", metricChart.Target.TotalSeconds)));
        }

        var tableRows = new StringBuilder();
        foreach (var testRun in suiteRuns.TakeLast(MaxRunsInTable).Reverse())
        {
            var fakeTag = testRun.UsedFakeTarget ? """<span class="fake">가짜</span>""" : "";
            if (testRun.HasProvisionalLabels)
                fakeTag += """<span class="fake">검수 전 탐색 시험</span>""";
            var measurementSummary = metricCharts.Count == 0
                ? "-"
                : string.Join(" · ", metricCharts.Select(metricChart =>
                    $"{metricChart.ShortName} {LatencyStatistics.FormatSeconds(ToTimeSpan(ReportHistoryLoader.FindSeconds(testRun, metricChart.CheckName)))}"));

            tableRows.Append($"""
                <tr>
                  <td class="nowrap"><a href="{Encode(testRun.RunId)}.html">{Encode(testRun.RunId)}</a></td>
                  <td class="nowrap">{testRun.StartedAt:MM/dd HH:mm:ss}</td>
                  <td>{Encode(testRun.TargetName)}{fakeTag}</td>
                  <td class="nowrap">{testRun.PassedCount} / {testRun.TotalCount}</td>
                  <td class="nowrap">{testRun.PassRate:0.0}%</td>
                  <td class="nowrap">{Encode(measurementSummary)}</td>
                </tr>
                """);
        }

        return $"""
            <h2>{Encode(suiteName)}</h2>
            <p class="meta">실행 {suiteRuns.Count}회 · 최근 통과율 {latestRun.PassRate:0.0}% · 최근 시험 대상 {Encode(latestRun.TargetName)}</p>
            <div class="charts">{charts}</div>
            <table>
              <thead><tr><th>실행 번호 (보고서)</th><th>시작</th><th>시험 대상</th><th>통과 / 전체</th><th>통과율</th><th>측정값</th></tr></thead>
              <tbody>{tableRows}</tbody>
            </table>
            """;
    }

    private static string ChartCard(string title, string svg) =>
        $"""<div class="chart-card"><h3>{Encode(title)}</h3>{svg}</div>""";

    private static TimeSpan? ToTimeSpan(double? seconds) => seconds is null ? null : TimeSpan.FromSeconds(seconds.Value);

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
