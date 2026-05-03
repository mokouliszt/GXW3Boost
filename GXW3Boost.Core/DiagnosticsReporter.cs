using System.Net.NetworkInformation;

namespace GXW3Boost.Core;

/// <summary>
/// GXWorks3起動が遅くなりうる環境要因を診断します。
/// GXWorks3本体には一切触れず、読み取り専用の観察のみ行います。
/// </summary>
public class DiagnosticsReporter
{
    public async Task<DiagnosticsReport> RunAsync(CancellationToken ct = default)
    {
        var issues = new List<DiagnosticsIssue>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. GXW3.exe の存在確認
        var gxPath = GxWorks3Locator.FindGxWorks3Exe();
        if (gxPath == null)
        {
            issues.Add(new DiagnosticsIssue(
                Severity.Error,
                "GXW3.exe が見つかりません",
                "レジストリまたは既知のパスに存在しません。GXWorks3が正しくインストールされているか確認してください。"));
        }
        else
        {
            issues.Add(new DiagnosticsIssue(
                Severity.Info,
                "GXW3.exe を検出しました",
                gxPath));
        }

        // 2. ファイル関連付けの確認
        if (FileAssociationManager.IsBoostActive())
        {
            issues.Add(new DiagnosticsIssue(
                Severity.Info,
                "GXW3Boost の関連付けが有効です",
                FileAssociationManager.GetCurrentCommand() ?? ""));
        }
        else
        {
            issues.Add(new DiagnosticsIssue(
                Severity.Warning,
                ".gx3 の関連付けが GXW3Boost を経由していません",
                "GXWorks3の更新やインストーラーにより関連付けが変更された可能性があります。トレイアイコンから再設定できます。"));
        }

        // 3. 依存DLLの解析状況
        if (gxPath != null)
        {
            var dlls = await Task.Run(() => PeImportParser.GetImportedDlls(gxPath), ct);
            if (dlls.Count > 0)
            {
                issues.Add(new DiagnosticsIssue(
                    Severity.Info,
                    $"依存DLL を {dlls.Count} 件検出しました",
                    "ウォームアップ対象として認識されています。"));
            }
            else
            {
                issues.Add(new DiagnosticsIssue(
                    Severity.Warning,
                    "依存DLL を解析できませんでした",
                    "GXW3.exeの読み取りに失敗した可能性があります。ウォームアップ効果が限定的になります。"));
            }
        }

        sw.Stop();
        return new DiagnosticsReport(DateTime.Now, gxPath, issues, sw.Elapsed);
    }
}

public record DiagnosticsIssue(Severity Severity, string Title, string Detail = "");

public record DiagnosticsReport(
    DateTime Timestamp,
    string? GxWorks3Path,
    List<DiagnosticsIssue> Issues,
    TimeSpan ElapsedTime)
{
    /// <summary>人間が読めるテキスト形式でレポートを出力します</summary>
    public string ToText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== GXW3Boost 診断レポート ===");
        sb.AppendLine($"日時    : {Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"診断時間: {ElapsedTime.TotalMilliseconds:F0} ms");
        sb.AppendLine($"GXW3    : {GxWorks3Path ?? "(未検出)"}");
        sb.AppendLine();

        foreach (var issue in Issues)
        {
            var prefix = issue.Severity switch
            {
                Severity.Error   => "[ERROR]",
                Severity.Warning => "[WARN] ",
                _                => "[INFO] ",
            };
            sb.AppendLine($"{prefix} {issue.Title}");
            if (!string.IsNullOrEmpty(issue.Detail))
                sb.AppendLine($"         {issue.Detail}");
        }

        return sb.ToString();
    }
}

public enum Severity { Info, Warning, Error }
