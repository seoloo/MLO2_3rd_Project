using System.Text.Json;
using QaRunner.Models;
using QaRunner.Pipeline;
using QaRunner.Verdicts;

namespace QaRunner;

/// <summary>
/// 판정 시험 사례 파일(JSON)을 읽어 세션 사례 목록으로 바꾸고, 형식이 맞는지 확인한다.
/// </summary>
public static class SessionCaseLoader
{
    // JSON의 camelCase 키(caseId, expectedVerdict, steps, atMs)를 C# 속성 이름과 맞춰 주는 설정
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 사례 파일 하나를 읽어 그 안의 세션 사례를 모두 돌려준다.
    /// 사례 번호가 겹치거나, 정답 판정이 계약에 없거나, 행동이 비어 있으면 ArgumentException을 던진다.
    /// </summary>
    /// <param name="filePath">사례 파일 경로. 예: cases/sessions.json</param>
    public static List<SessionCase> LoadFromFile(string filePath)
    {
        var fileContent = File.ReadAllText(filePath);
        var sessionCases = JsonSerializer.Deserialize<List<SessionCase>>(fileContent, JsonOptions) ?? [];
        Validate(sessionCases, filePath);
        return sessionCases;
    }

    /// <summary>
    /// 사례 목록이 시험에 쓸 수 있는 모양인지 확인한다. 정답 자체가 맞는지는 사람이 검수한다.
    /// </summary>
    public static void Validate(IReadOnlyList<SessionCase> sessionCases, string sourceName)
    {
        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sessionCase in sessionCases)
        {
            var caseName = sessionCase?.CaseId ?? "(번호 없음)";
            if (sessionCase is null || string.IsNullOrWhiteSpace(sessionCase.CaseId) || !caseIds.Add(sessionCase.CaseId))
                throw new ArgumentException($"사례 번호가 비었거나 겹칩니다: {caseName} ({sourceName})");
            if (string.IsNullOrWhiteSpace(sessionCase.Actor))
                throw new ArgumentException($"{caseName}: actor(사람·봇 A 등)가 필요합니다. ({sourceName})");
            if (!VerdictCodes.IsKnown(sessionCase.ExpectedVerdict))
                throw new ArgumentException($"{caseName}: expectedVerdict는 정상·관찰·추가 확인·보류 중 하나여야 합니다: {sessionCase.ExpectedVerdict} ({sourceName})");
            if (sessionCase.Steps is null || sessionCase.Steps.Count == 0)
                throw new ArgumentException($"{caseName}: steps(행동)가 하나 이상 필요합니다. ({sourceName})");
            if (sessionCase.ParallelSessions is < 1 or > 100)
                throw new ArgumentException($"{caseName}: parallelSessions는 1~100 사이여야 합니다. ({sourceName})");

            foreach (var step in sessionCase.Steps)
            {
                if (step is null || !EventTypes.IsKnown(step.Type))
                    throw new ArgumentException($"{caseName}: 알 수 없는 행동 종류입니다: {step?.Type} ({sourceName})");
                if (step.AtMs < 0)
                    throw new ArgumentException($"{caseName}: atMs는 0 이상이어야 합니다. ({sourceName})");
            }
        }
    }
}
