using System.Collections.Concurrent;
using System.Diagnostics;
using QaRunner.Models;
using QaRunner.Pipeline;
using QaRunner.Verdicts;

namespace QaRunner.Measurement;

/// <summary>
/// 부하 시험 채점기. 실행안의 "API 동시 5개 / 스트림 5이벤트·초, 5분 1,500개"를 두 갈래로 동시에 돌린다.
/// - API 부하: 가상 사용자 여러 명이 판정 API를 계속 부르며 응답 시간과 오류를 잰다. (목표 p95 3초)
/// - 스트림 부하: 이벤트를 초당 N건 보내며 Gold에 반영되기까지 걸린 시간을 잰다. (목표 p95 10초)
/// </summary>
/// <param name="verdictService">API 부하를 줄 판정 대상</param>
/// <param name="testCases">API에 보낼 문자들 (판정 시험 입력값 파일을 돌려 가며 씀)</param>
/// <param name="eventSender">스트림 부하 이벤트를 넣을 곳</param>
/// <param name="pipelineReader">Gold 반영을 확인할 곳</param>
/// <param name="settings">측정 시간, 초당 건수, 동시 사용자 수</param>
public sealed class LoadChecker(
    IVerdictService verdictService,
    IReadOnlyList<TestCase> testCases,
    IEventSender eventSender,
    IPipelineReader pipelineReader,
    MeasurementSettings settings)
{
    /// <summary>API 응답 시간 p95 목표 (실행안 초기 목표)</summary>
    public static readonly TimeSpan ApiLatencyTarget = TimeSpan.FromSeconds(3);

    /// <summary>Gold 반영 시간 p95 목표 (실행안 초기 목표)</summary>
    public static readonly TimeSpan GoldLatencyTarget = TimeSpan.FromSeconds(10);

    /// <summary>보내기가 끝난 뒤 남은 이벤트가 Gold에 반영되기를 기다리는 최대 시간</summary>
    public static readonly TimeSpan CatchUpTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 가상 사용자가 응답을 받은 뒤 다음 요청까지 쉬는 시간.
    /// 쉬지 않으면 가짜 판정 대상처럼 즉시 답하는 대상에 초당 수십만 번 요청하게 되어 의미 없는 부하가 된다.
    /// </summary>
    public static readonly TimeSpan PauseBetweenRequests = TimeSpan.FromMilliseconds(200);

    public const string ApiCaseId = "LOAD-API";
    public const string StreamCaseId = "LOAD-STREAM";

    // 확인 항목 이름. 대시보드가 이 이름으로 실행 기록에서 측정값을 찾아 추이를 그린다
    public const string ApiLatencyCheckName = "API 응답 시간 p95";
    public const string ApiErrorRateCheckName = "API 오류율";
    public const string GoldLatencyCheckName = "Gold 반영 시간 p95";
    public const string GoldReflectedCountCheckName = "Gold 반영 건수 (적체 없음)";

    // API 부하 결과: 성공한 요청의 응답 시간들, 실패 건수, 첫 실패 이유
    private sealed record ApiLoadResult(IReadOnlyList<TimeSpan> Latencies, int FailedCount, string? FirstError);

    /// <summary>
    /// API 부하와 스트림 부하를 동시에 돌리고 결과를 돌려준다.
    /// </summary>
    /// <param name="runId">이번 QA 실행 번호</param>
    public async Task<List<TestResult>> CheckAllAsync(string runId, CancellationToken cancellationToken = default)
    {
        var duration = TimeSpan.FromSeconds(settings.DurationSeconds);
        var reflectionTracker = new GoldReflectionTracker(pipelineReader);

        var apiLoadTask = RunApiLoadAsync(duration, cancellationToken);
        var streamTask = EventStreamSender.SendAtFixedRateAsync(
            eventSender, reflectionTracker, runId, StreamCaseId, settings.EventsPerSecond, duration, cancellationToken);
        var trackingTask = reflectionTracker.TrackUntilCaughtUpAsync(
            streamTask, CatchUpTimeout, GoldReflectionTracker.DefaultPollInterval, cancellationToken);

        await Task.WhenAll(apiLoadTask, streamTask, trackingTask);

        var testResults = new List<TestResult>();
        testResults.AddRange(BuildApiResults(await apiLoadTask));
        testResults.AddRange(BuildStreamResults(await streamTask, reflectionTracker));
        return testResults;
    }

    // 가상 사용자 수만큼 동시에, 정해진 시간 동안 판정 API를 반복해서 부른다
    private async Task<ApiLoadResult> RunApiLoadAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var latencies = new ConcurrentBag<TimeSpan>();
        var failedCount = 0;
        string? firstError = null;
        var nextCaseNumber = -1;
        var endAt = DateTimeOffset.UtcNow + duration;

        async Task RunVirtualUserAsync()
        {
            while (DateTimeOffset.UtcNow < endAt)
            {
                // 여러 사용자가 입력값 파일의 문자를 차례로 나눠 쓴다
                var caseIndex = (int)((uint)Interlocked.Increment(ref nextCaseNumber) % (uint)testCases.Count);
                var requestStartedAt = Stopwatch.GetTimestamp();

                try
                {
                    await verdictService.GetVerdictAsync(testCases[caseIndex].Message);
                    latencies.Add(Stopwatch.GetElapsedTime(requestStartedAt));
                }
                catch (Exception exception) when (VerdictChecker.IsVerdictServiceFailure(exception))
                {
                    Interlocked.Increment(ref failedCount);
                    Interlocked.CompareExchange(ref firstError, exception.Message, null);
                }

                await Task.Delay(PauseBetweenRequests, cancellationToken);
            }
        }

        var virtualUsers = Enumerable.Range(0, settings.ApiConcurrency).Select(_ => Task.Run(RunVirtualUserAsync, cancellationToken));
        await Task.WhenAll(virtualUsers);

        return new ApiLoadResult(latencies.ToList(), failedCount, firstError);
    }

    // API 부하 결과를 확인 항목으로 바꾼다
    private List<TestResult> BuildApiResults(ApiLoadResult apiLoadResult)
    {
        var requestCount = apiLoadResult.Latencies.Count + apiLoadResult.FailedCount;
        var input = $"가상 사용자 {settings.ApiConcurrency}명 · {settings.DurationSeconds}초 · 요청 {requestCount}건";

        var latencyP95 = LatencyStatistics.Percentile(apiLoadResult.Latencies, 95);
        var errorRate = requestCount == 0 ? 0 : apiLoadResult.FailedCount * 100.0 / requestCount;

        var apiResults = new List<TestResult>
        {
            new(ApiCaseId, ApiLatencyCheckName, input,
                Expected: $"{ApiLatencyTarget.TotalSeconds:0}초 이하",
                Actual: LatencyStatistics.FormatSeconds(latencyP95),
                MeetsTarget: latencyP95 <= ApiLatencyTarget),
            new(ApiCaseId, ApiErrorRateCheckName, input,
                Expected: "0%",
                Actual: $"{errorRate:0.0}%",
                ErrorMessage: null,
                MeetsTarget: apiLoadResult.FailedCount == 0),
        };

        // 요청이 전부 실패했다면(예: API가 꺼져 있음) 측정 자체가 안 된 것이므로 이유를 남긴다
        if (apiLoadResult.Latencies.Count == 0 && apiLoadResult.FirstError is not null)
        {
            apiResults[0] = TestResult.SystemFailure(
                ApiCaseId, ApiLatencyCheckName, input, apiResults[0].Expected, apiLoadResult.FirstError);
        }

        return apiResults;
    }

    // 스트림 부하 결과를 확인 항목으로 바꾼다
    private List<TestResult> BuildStreamResults(StreamSendResult streamSendResult, GoldReflectionTracker reflectionTracker)
    {
        var input = $"초당 {settings.EventsPerSecond:0.##}건 · {settings.DurationSeconds}초 · 이벤트 {streamSendResult.SentCount}건";
        var goldLatencyP95 = LatencyStatistics.Percentile(reflectionTracker.GetReflectionLatencies(), 95);

        var latencyResult = new TestResult(StreamCaseId, GoldLatencyCheckName, input,
            Expected: $"{GoldLatencyTarget.TotalSeconds:0}초 이하",
            Actual: LatencyStatistics.FormatSeconds(goldLatencyP95),
            MeetsTarget: goldLatencyP95 <= GoldLatencyTarget);

        // 끝까지 Gold에 반영되지 않은 건수가 곧 적체다. 보낸 건수와 같아야 한다
        var reflectedCountResult = new TestResult(StreamCaseId, GoldReflectedCountCheckName, input,
            Expected: reflectionTracker.SentCount.ToString(),
            Actual: reflectionTracker.ReflectedCount.ToString());

        if (streamSendResult.SendError is not null)
        {
            return
            [
                TestResult.SystemFailure(StreamCaseId, latencyResult.CheckName, input, latencyResult.Expected, streamSendResult.SendError),
                TestResult.SystemFailure(StreamCaseId, reflectedCountResult.CheckName, input, reflectedCountResult.Expected, streamSendResult.SendError),
            ];
        }

        return [latencyResult, reflectedCountResult];
    }
}
