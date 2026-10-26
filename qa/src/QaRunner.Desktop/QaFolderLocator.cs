using System.IO;

namespace QaRunner.Desktop;

/// <summary>
/// 데스크탑 앱이 어디서 실행되든 qa 폴더를 찾아 기준 폴더로 쓰게 한다.
/// 입력값 파일(cases/rules.json)과 보고서 폴더(reports)를 콘솔 프로그램과 같은 곳에서 쓰기 위해서다.
/// </summary>
public static class QaFolderLocator
{
    /// <summary>
    /// qa 폴더를 찾으면 현재 폴더로 바꾼다. 못 찾으면 그대로 둔다.
    /// </summary>
    public static void UseQaFolderAsCurrentDirectory()
    {
        var qaFolder = FindQaFolder(AppContext.BaseDirectory) ?? FindQaFolder(Environment.CurrentDirectory);
        if (qaFolder is not null)
            Directory.SetCurrentDirectory(qaFolder);
    }

    /// <summary>
    /// 시작 폴더에서 한 단계씩 위로 올라가며 cases/rules.json이 있는 폴더를 찾는다.
    /// 예: qa/src/QaRunner.Desktop/bin/Debug/net10.0-windows → qa
    /// </summary>
    /// <param name="startFolder">찾기 시작할 폴더</param>
    public static string? FindQaFolder(string startFolder)
    {
        for (var folder = new DirectoryInfo(startFolder); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "cases", "rules.json")))
                return folder.FullName;
        }

        return null;
    }
}
