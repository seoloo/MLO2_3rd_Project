using QaRunner.Pipeline;

namespace QaRunner.Tests;

/// <summary>
/// 시나리오 폴더를 읽을 때 이벤트와 주입 조건이 빠짐없이 옮겨지는지 확인한다.
/// </summary>
public class PipelineScenarioLoaderTests : IDisposable
{
    private readonly string _tempScenarioDirectory =
        Path.Combine(Path.GetTempPath(), $"qa-scenario-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempScenarioDirectory))
            Directory.Delete(_tempScenarioDirectory, recursive: true);
    }

    [Fact]
    public void Reads_events_and_injection_settings_from_each_json_file()
    {
        Directory.CreateDirectory(_tempScenarioDirectory);
        File.WriteAllText(Path.Combine(_tempScenarioDirectory, "b.json"), """
            { "caseId": "B", "events": [ { "eventId": "b1", "scanId": "s1" } ] }
            """);
        File.WriteAllText(Path.Combine(_tempScenarioDirectory, "a.json"), """
            {
              "caseId": "A",
              "description": "중복 시험",
              "events": [ { "eventId": "a1", "scanId": "s1", "offsetSeconds": 2, "verdict": "주의" } ],
              "injection": { "duplicateCount": 3, "sendInReverseOrder": true, "delaySeconds": 30 }
            }
            """);

        var scenarios = PipelineScenarioLoader.LoadFromDirectory(_tempScenarioDirectory);

        // 파일 이름 순서(a.json → b.json)로 읽는다
        Assert.Equal(new[] { "A", "B" }, scenarios.Select(scenario => scenario.CaseId));

        var scenarioA = scenarios[0];
        Assert.Equal("중복 시험", scenarioA.Description);
        Assert.Equal(2, scenarioA.Events.Single().OffsetSeconds);
        Assert.Equal("주의", scenarioA.Events.Single().Verdict);
        Assert.Equal(3, scenarioA.Injection.DuplicateCount);
        Assert.True(scenarioA.Injection.SendInReverseOrder);
        Assert.Equal(30, scenarioA.Injection.DelaySeconds);

        // 주입 조건을 적지 않으면 중복 1회, 정순, 지연 없음
        Assert.Equal(1, scenarios[1].Injection.DuplicateCount);
        Assert.False(scenarios[1].Injection.SendInReverseOrder);
    }

    [Fact]
    public void Throws_when_folder_does_not_exist()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => PipelineScenarioLoader.LoadFromDirectory(_tempScenarioDirectory));
    }
}
