using QaRunner.Measurement;
using QaRunner.Models;
using QaRunner.Pipeline;
using QaRunner.Reports;
using QaRunner.Verdicts;

namespace QaRunner.SelfCheck;

/// <summary>
/// QA 도구 점검. QA 러너가 다른 시스템을 채점하기 전에, 답을 이미 아는 입력으로
/// 자기 자신(채점기·정답 계산기·측정 계산·보고서)이 맞게 동작하는지 확인한다.
/// 입력은 코드 안에 고정해 두므로 사례 파일을 고쳐도 점검 결과는 바뀌지 않는다.
/// 외부 연결이 없어서 1초 안에 끝나고, CI에서도 이 점검은 항상 통과해야 한다.
/// </summary>
public static class QaToolSelfChecker
{
    /// <summary>보고서에 남기는 시험 대상 이름. Fake로 시작하지 않으므로 연습용 대상 안내가 붙지 않는다</summary>
    public const string TargetName = "QaRunner 자체";

    // 점검 하나: 번호, 무엇을 확인하는지, 무엇을 넣는지, 기대하는 값, 실제 값을 구하는 작업
    private sealed record SelfCheckItem(string CaseId, string CheckName, string Input, string Expected, Func<Task<string>> ReadActual);

    /// <summary>
    /// 모든 점검을 차례로 돌린다. 점검 하나가 예외로 멈춰도 그 항목만 시스템 실패로 남기고 다음 점검을 계속한다.
    /// </summary>
    public static async Task<List<TestResult>> CheckAllAsync()
    {
        var testResults = new List<TestResult>();
        foreach (var item in BuildItems())
        {
            try
            {
                testResults.Add(new TestResult(item.CaseId, item.CheckName, item.Input, item.Expected, await item.ReadActual()));
            }
            catch (Exception exception)
            {
                // 점검 코드에서 난 예외는 QA 도구의 결함이므로 숨기지 않고 이유와 함께 남긴다
                testResults.Add(TestResult.SystemFailure(item.CaseId, item.CheckName, item.Input, item.Expected,
                    $"{exception.GetType().Name}: {exception.Message}"));
            }
        }
        return testResults;
    }

    private static IEnumerable<SelfCheckItem> BuildItems() =>
    [
        new("SC-01", "판정 채점기 분류",
            "맞는 판정 1건 · 틀린 판정 1건 · 응답 오류 1건",
            "통과 1 · 불일치 1 · 응답 없음 1",
            CheckVerdictGradingAsync),
        new("SC-02", "결함 잡아내기 · 판정",
            "‘인증번호’만 보는 연습용 판정기에 앱 설치 요구 문자",
            "불일치로 잡아냄",
            CheckFakeVerdictDefectAsync),
        new("SC-03", "결함 잡아내기 · 파이프라인",
            "격리를 하지 않는 연습용 파이프라인에 6분 늦은 이벤트",
            "격리 건수 불일치로 잡아냄",
            CheckFakePipelineDefectAsync),
        new("SC-04", "정답 계산 · 중복",
            "같은 이벤트를 3번 보냄",
            "Bronze 3 · Silver 1 · 격리 0",
            () => Task.FromResult(DescribeCounts(Calculate(Event("e1", "고위험"), Event("e1", "고위험"), Event("e1", "고위험"))))),
        new("SC-05", "정답 계산 · 늦은 도착",
            "이벤트 시각보다 6분 늦게 도착",
            "Bronze 1 · Silver 0 · 격리 1",
            () => Task.FromResult(DescribeCounts(Calculate(Event("e1", "고위험", arrivalDelay: TimeSpan.FromMinutes(6)))))),
        new("SC-06", "정답 계산 · 같은 ID 상충",
            "같은 event_id가 다른 판정으로 두 번 도착",
            "Bronze 2 · Silver 1 · 격리 1",
            () => Task.FromResult(DescribeCounts(Calculate(Event("e1", "고위험"), Event("e1", "주의"))))),
        new("SC-07", "정답 계산 · Gold 60초 구간",
            "00:10 · 00:50 · 01:10에 일어난 이벤트 3건",
            "2개 구간 · 2건 / 1건",
            CheckGoldWindows),
        new("SC-08", "p95 계산",
            "1초부터 100초까지 100건",
            "95.00초",
            () => Task.FromResult(LatencyStatistics.FormatSeconds(
                LatencyStatistics.Percentile(Enumerable.Range(1, 100).Select(seconds => TimeSpan.FromSeconds(seconds)), 95)))),
        new("SC-09", "판정 코드 변환",
            "HIGH_RISK · CAUTION · NO_SIGNAL · UNDECIDED",
            "고위험 · 주의 · 신호 미발견 · 판단 보류",
            () => Task.FromResult(string.Join(" · ",
                new[] { "HIGH_RISK", "CAUTION", "NO_SIGNAL", "UNDECIDED" }.Select(VerdictCodes.ToKorean)))),
        new("SC-10", "보고서 저장 후 다시 읽기",
            "3건 중 1건 실패한 실행 기록을 임시 폴더에 저장",
            "1건 · 2/3 통과 · QA 도구 영역 · HTML에 사례 번호",
            CheckReportRoundTrip),
        new("SC-11", "실행 번호 겹침 방지",
            "같은 초에 만든 보고서가 이미 있는 폴더",
            "뒤에 -2를 붙임",
            CheckRunIdCollision),
    ];

    // ---- 판정 채점기 ----

    // 정해 둔 답을 돌려주거나 오류를 내는 판정 대상. 채점기가 세 경우를 각각 맞게 분류하는지 본다
    private sealed class ScriptedVerdictService : IVerdictService
    {
        public Task<string> GetVerdictAsync(string message) => message switch
        {
            "맞는 문자" => Task.FromResult("고위험"),
            "틀린 문자" => Task.FromResult("신호 미발견"),
            _ => throw new VerdictServiceException("점검용 응답 오류"),
        };
    }

    private static async Task<string> CheckVerdictGradingAsync()
    {
        var results = await new VerdictChecker(new ScriptedVerdictService()).CheckAllAsync(
        [
            new TestCase("A", "맞는 문자", "고위험"),
            new TestCase("B", "틀린 문자", "고위험"),
            new TestCase("C", "오류 문자", "고위험"),
        ]);
        var mismatched = results.Count(result => !result.IsPassed && !result.IsSystemFailure);
        return $"통과 {results.Count(result => result.IsPassed)} · 불일치 {mismatched} · 응답 없음 {results.Count(result => result.IsSystemFailure)}";
    }

    private static async Task<string> CheckFakeVerdictDefectAsync()
    {
        var results = await new VerdictChecker(new FakeVerdictService()).CheckAllAsync(
            [new TestCase("A", "보안 앱을 설치해야 환불됩니다. 링크: https://refund.test/app", "고위험")]);
        return results is [{ IsPassed: false, IsSystemFailure: false }] ? "불일치로 잡아냄" : "놓침";
    }

    // ---- 파이프라인 채점기 ----

    private static async Task<string> CheckFakePipelineDefectAsync()
    {
        var lateScenario = new PipelineScenario
        {
            CaseId = "LATE-SELF",
            Description = "6분 늦게 도착한 이벤트는 격리돼야 한다",
            Events = [new ScenarioEvent { EventId = "late-self-e1", ScanId = "late-self-s1", OffsetSeconds = 0, Verdict = "고위험" }],
            Injection = new FaultInjection { DelaySeconds = 360 },
        };
        var fakePipeline = new FakePipeline();
        var results = await new PipelineChecker(fakePipeline, fakePipeline, TimeSpan.Zero)
            .CheckAllAsync([lateScenario], $"selfcheck-{Guid.NewGuid():N}");
        var quarantine = results.FirstOrDefault(result => result.CaseId == lateScenario.CaseId && result.CheckName == "격리 건수");
        return quarantine is { IsPassed: false, IsSystemFailure: false } ? "격리 건수 불일치로 잡아냄" : "놓침";
    }

    // ---- 정답 계산기 ----

    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    // 이벤트 시각과 도착 시각. 도착 지연을 주지 않으면 1초 뒤에 도착한 것으로 본다
    private sealed record TimedEvent(PipelineEvent Event, DateTimeOffset ArrivedAt);

    private static TimedEvent Event(string eventId, string verdict, TimeSpan? arrivalDelay = null, int offsetSeconds = 0)
    {
        var eventTime = BaseTime.AddSeconds(offsetSeconds);
        return new TimedEvent(new PipelineEvent("selfcheck", "SC", eventId, $"scan-{eventId}", eventTime, verdict),
            eventTime + (arrivalDelay ?? TimeSpan.FromSeconds(1)));
    }

    private static ExpectedResultCalculator Calculate(params TimedEvent[] events)
    {
        var calculator = new ExpectedResultCalculator();
        foreach (var timedEvent in events) calculator.Record(timedEvent.Event, timedEvent.ArrivedAt);
        return calculator;
    }

    private static string DescribeCounts(ExpectedResultCalculator calculator) =>
        $"Bronze {calculator.BronzeCount} · Silver {calculator.SilverCount} · 격리 {calculator.QuarantineCount}";

    private static Task<string> CheckGoldWindows()
    {
        var calculator = Calculate(Event("e1", "고위험", offsetSeconds: 10), Event("e2", "주의", offsetSeconds: 50),
            Event("e3", "고위험", offsetSeconds: 70));
        var counts = calculator.GoldCountsByWindow.OrderBy(window => window.Key).Select(window => $"{window.Value}건");
        return Task.FromResult($"{calculator.GoldCountsByWindow.Count}개 구간 · {string.Join(" / ", counts)}");
    }

    // ---- 보고서 ----

    private static Task<string> CheckReportRoundTrip()
    {
        var folder = Directory.CreateTempSubdirectory("qa-selfcheck-").FullName;
        try
        {
            var sampleRun = new TestRun("2026-01-01-090000", TestSuite.SelfCheck.ToKoreanName(), BaseTime, BaseTime.AddSeconds(1),
                CommandLineOptions.NoCasesPath, TargetName,
                [
                    new TestResult("RT-1", "판정", "문자 1", "고위험", "고위험"),
                    new TestResult("RT-2", "판정", "문자 2", "주의", "주의"),
                    new TestResult("RT-3", "판정", "문자 3", "고위험", "신호 미발견"),
                ])
            {
                AreaName = TestArea.QaTool.ToKoreanName(),
            };
            JsonReportWriter.Save(sampleRun, folder);
            var loaded = ReportHistoryLoader.LoadAll(folder);
            var html = HtmlReportWriter.BuildHtml(sampleRun);
            var reloaded = loaded.FirstOrDefault();
            return Task.FromResult(
                $"{loaded.Count}건 · {reloaded?.PassedCount}/{reloaded?.TotalCount} 통과 · {reloaded?.AreaName} 영역 · "
                + (html.Contains("RT-3", StringComparison.Ordinal) ? "HTML에 사례 번호" : "HTML에 사례 번호 없음"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static Task<string> CheckRunIdCollision()
    {
        var folder = Directory.CreateTempSubdirectory("qa-selfcheck-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, $"{TestRun.CreateRunId(BaseTime)}.json"), "{}");
            var runId = RunIdGenerator.CreateUnique(BaseTime, folder);
            return Task.FromResult(runId == $"{TestRun.CreateRunId(BaseTime)}-2" ? "뒤에 -2를 붙임" : $"{runId}로 만듦");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
