using System.Text.Json;

namespace QaRunner.Pipeline;

/// <summary>
/// 시나리오 폴더의 JSON 파일들을 읽어 파이프라인 시험 시나리오 목록으로 바꾼다.
/// </summary>
public static class PipelineScenarioLoader
{
    // JSON의 camelCase 키(caseId, events, injection ...)를 C# 속성 이름과 맞춰 주는 설정
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 폴더 안의 모든 .json 파일을 파일 이름 순서대로 읽는다.
    /// </summary>
    /// <param name="scenarioDirectory">시나리오 폴더 경로. 예: cases/pipeline</param>
    public static List<PipelineScenario> LoadFromDirectory(string scenarioDirectory)
    {
        if (!Directory.Exists(scenarioDirectory))
            throw new DirectoryNotFoundException($"시나리오 폴더가 없습니다: {scenarioDirectory}");

        return Directory.GetFiles(scenarioDirectory, "*.json")
            .Order()
            .Select(LoadFromFile)
            .ToList();
    }

    // 시나리오 파일 하나를 읽는다
    private static PipelineScenario LoadFromFile(string scenarioFilePath)
    {
        var fileContent = File.ReadAllText(scenarioFilePath);
        return JsonSerializer.Deserialize<PipelineScenario>(fileContent, JsonOptions)
            ?? throw new JsonException($"시나리오를 읽을 수 없습니다: {scenarioFilePath}");
    }
}
