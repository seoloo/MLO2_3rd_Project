namespace QaRunner.Tests;

/// <summary>
/// 세션 사례 파일을 읽을 때 행위자·정답·행동이 빠짐없이 옮겨지고, 형식이 틀리면 바로 알리는지 확인한다.
/// </summary>
public class SessionCaseLoaderTests : IDisposable
{
    private readonly string _tempFilePath = Path.Combine(Path.GetTempPath(), $"qa-sessions-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_tempFilePath)) File.Delete(_tempFilePath);
    }

    [Fact]
    public void Reads_actor_expected_verdict_and_steps_from_json_file()
    {
        File.WriteAllText(_tempFilePath, """
            [ { "caseId": "S-007", "actor": "봇 D", "description": "한 기기 세션 20개", "expectedVerdict": "추가 확인",
                "parallelSessions": 20,
                "steps": [ { "type": "queue_enter", "atMs": 1500 },
                           { "type": "seat_click", "atMs": 9800, "x": 190, "y": 360, "screen": "seat" } ] } ]
            """);

        var sessionCase = Assert.Single(SessionCaseLoader.LoadFromFile(_tempFilePath));

        Assert.Equal("S-007", sessionCase.CaseId);
        Assert.Equal("봇 D", sessionCase.Actor);
        Assert.Equal("추가 확인", sessionCase.ExpectedVerdict);
        Assert.Equal(20, sessionCase.ParallelSessions);
        Assert.False(sessionCase.IsHuman);
        Assert.Equal(2, sessionCase.Steps.Count);
        Assert.Equal(190, sessionCase.Steps[1].X);
        Assert.Equal("1.50초 대기열 진입 → 9.80초 좌석 클릭", sessionCase.DescribeSteps());
    }

    [Theory]
    [InlineData("""[ { "caseId": "S-1", "actor": "사람", "expectedVerdict": "안전", "steps": [ { "type": "queue_enter", "atMs": 0 } ] } ]""")]
    [InlineData("""[ { "caseId": "S-1", "actor": "사람", "expectedVerdict": "정상", "steps": [] } ]""")]
    [InlineData("""[ { "caseId": "S-1", "actor": "사람", "expectedVerdict": "정상", "steps": [ { "type": "jump", "atMs": 0 } ] } ]""")]
    [InlineData("""[ { "caseId": "S-1", "actor": "", "expectedVerdict": "정상", "steps": [ { "type": "queue_enter", "atMs": 0 } ] } ]""")]
    [InlineData("""[ { "caseId": "S-1", "actor": "사람", "expectedVerdict": "정상", "steps": [ { "type": "queue_enter", "atMs": -5 } ] } ]""")]
    [InlineData("""[ { "caseId": "S-1", "actor": "사람", "expectedVerdict": "정상", "steps": [ { "type": "queue_enter", "atMs": 0 } ] }, { "caseId": "S-1", "actor": "사람", "expectedVerdict": "정상", "steps": [ { "type": "queue_enter", "atMs": 0 } ] } ]""")]
    public void Rejects_invalid_session_cases(string json)
    {
        File.WriteAllText(_tempFilePath, json);

        Assert.Throws<ArgumentException>(() => SessionCaseLoader.LoadFromFile(_tempFilePath));
    }

    [Fact]
    public void Default_session_cases_file_is_valid_and_mixes_humans_and_every_bot_type()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "cases", "sessions.json");

        var sessionCases = SessionCaseLoader.LoadFromFile(path);

        Assert.Contains(sessionCases, sessionCase => sessionCase.IsHuman);
        foreach (var bot in new[] { "봇 A", "봇 B", "봇 C", "봇 D" })
            Assert.Contains(sessionCases, sessionCase => sessionCase.Actor == bot);
    }
}
