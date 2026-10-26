namespace QaRunner.Tests;

/// <summary>
/// 입력값 파일을 읽을 때 문자와 정답이 빠짐없이 옮겨지는지 확인한다.
/// </summary>
public class TestCaseLoaderTests
{
    [Fact]
    public void Reads_message_and_expected_verdict_from_json_file()
    {
        // 시험용 임시 파일을 만들어 읽고, 끝나면 지운다
        var tempFilePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFilePath, """
                [ { "caseId": "R-001", "message": "인증번호를 알려주세요", "expectedVerdict": "고위험" } ]
                """);

            var testCases = TestCaseLoader.LoadFromFile(tempFilePath);

            var testCase = Assert.Single(testCases);
            Assert.Equal("R-001", testCase.CaseId);
            Assert.Equal("인증번호를 알려주세요", testCase.Message);
            Assert.Equal("고위험", testCase.ExpectedVerdict);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }
}
