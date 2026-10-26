using System.Text.Encodings.Web;
using System.Text.Json;
using QaRunner.Models;

namespace QaRunner.Reports;

/// <summary>
/// 실행 기록을 JSON 파일로 저장한다.
/// 사람이 보는 용도보다는 나중에 대시보드나 CI 같은 다른 프로그램이 읽는 용도다.
/// </summary>
public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // 줄바꿈과 들여쓰기를 넣어 파일을 열었을 때 읽기 쉽게 한다
        WriteIndented = true,

        // 한글을 \uXXXX 형태로 바꾸지 않고 그대로 저장한다
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 실행 기록을 "{실행 번호}.json" 파일로 저장하고 저장한 경로를 돌려준다.
    /// </summary>
    /// <param name="testRun">저장할 실행 기록</param>
    /// <param name="reportDirectory">보고서를 저장할 폴더. 없으면 새로 만든다</param>
    public static string Save(TestRun testRun, string reportDirectory)
    {
        Directory.CreateDirectory(reportDirectory);

        var reportFilePath = Path.Combine(reportDirectory, $"{testRun.RunId}.json");
        var reportContent = JsonSerializer.Serialize(testRun, JsonOptions);
        File.WriteAllText(reportFilePath, reportContent);

        return reportFilePath;
    }
}
