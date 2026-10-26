using System.Text.Json;
using QaRunner.Models;

namespace QaRunner;

/// <summary>
/// 입력값 파일(JSON)을 읽어 시험 케이스 목록으로 바꾼다.
/// </summary>
public static class TestCaseLoader
{
    // JSON의 camelCase 키(caseId, message, expectedVerdict)를
    // C# 속성 이름(CaseId, Message, ExpectedVerdict)과 맞춰 주는 설정
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 입력값 파일 하나를 읽어 그 안의 시험 케이스를 모두 돌려준다.
    /// </summary>
    /// <param name="filePath">입력값 파일 경로. 예: cases/rules.json</param>
    public static List<TestCase> LoadFromFile(string filePath)
    {
        var fileContent = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<TestCase>>(fileContent, JsonOptions) ?? [];
    }
}
