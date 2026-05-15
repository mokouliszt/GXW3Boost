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

        // 3. 依存DLL（PE-import）の解析状況
        if (gxPath != null)
        {
            var dlls = await Task.Run(() => PeImportParser.GetImportedDlls(gxPath), ct);
            if (dlls.Count > 0)
            {
                issues.Add(new DiagnosticsIssue(
                    Severity.Info,
                    $"PE インポート DLL を {dlls.Count} 件検出しました",
                    "GXW3.exe が直接依存する DLL です。"));
            }
            else
            {
                issues.Add(new DiagnosticsIssue(
                    Severity.Warning,
                    "PE インポート DLL を解析できませんでした",
                    "GXW3.exeの読み取りに失敗した可能性があります。"));
            }

            // 4. WarmManifest によるサブディレクトリ列挙
            var gppw3Dir = Path.GetDirectoryName(gxPath) ?? "";
            var melsoftDir = GxWorks3Locator.FindMelsoftDir();
            var targets = await Task.Run(
                () => WarmManifest.Enumerate(gppw3Dir, melsoftDir), ct);

            int t1 = 0, t2 = 0, t3 = 0;
            long b1 = 0, b2 = 0, b3 = 0;
            foreach (var t in targets)
            {
                long size = 0;
                try { size = new FileInfo(t.Path).Length; } catch { /* 取れなくても無視 */ }

                switch (t.Tier)
                {
                    case WarmTier.Critical:   t1++; b1 += size; break;
                    case WarmTier.Essential:  t2++; b2 += size; break;
                    case WarmTier.Background: t3++; b3 += size; break;
                }
            }

            if (targets.Count > 0)
            {
                issues.Add(new DiagnosticsIssue(
                    Severity.Info,
                    $"ウォーム対象を {targets.Count} 件列挙しました",
                    $"Tier1(Critical)={t1}件 {FormatBytes(b1)} / " +
                    $"Tier2(Essential)={t2}件 {FormatBytes(b2)} / " +
                    $"Tier3(Background)={t3}件 {FormatBytes(b3)}"));
            }
            else
            {
                issues.Add(new DiagnosticsIssue(
                    Severity.Warning,
                    "ウォーム対象が 0 件です",
                    "GXWorks3 のインストールディレクトリ構成が想定外の可能性があります。"));
            }

            if (melsoftDir == null)
            {
                issues.Add(new DiagnosticsIssue(
                    Severity.Warning,
                    "MELSOFT ルートディレクトリが取得できません",
                    "Easysocket / MSF 配下のファイルがウォーム対象になりません。"));
            }
        }

        sw.Stop();
        return new DiagnosticsReport(DateTime.Now, gxPath, issues, sw.Elapsed);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
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
