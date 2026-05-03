namespace GXW3Boost.Core;

/// <summary>
/// GXWorks3が依存するDLLをOSのファイルキャッシュに事前読み込みします。
///
/// 原理: Windowsはファイルを読み込んだページをメモリ上にキャッシュします。
///       GXWorks3起動前にDLLファイルをすべて読み込んでおくことで、
///       GXWorks3がLoadLibraryを呼ぶ際のディスクI/Oをゼロに近づけます。
///
/// 安全性: GXWorks3プロセスには一切触れません。ファイルを読み取るだけです。
///         DLLのビット数（32/64）に関係なく動作します。
/// </summary>
public class DllPrewarmer
{
    private readonly string _gxWorks3ExePath;

    // 検索するディレクトリ（優先順）
    private readonly string[] _searchDirs;

    public DllPrewarmer(string gxWorks3ExePath)
    {
        _gxWorks3ExePath = gxWorks3ExePath;

        var gxDir = Path.GetDirectoryName(gxWorks3ExePath) ?? "";
        _searchDirs = new[]
        {
            gxDir,
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), // C:\Windows\SysWOW64
            Environment.SystemDirectory,                                      // C:\Windows\System32
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        };
    }

    /// <summary>
    /// 依存DLLを非同期にキャッシュウォームします。
    /// </summary>
    /// <param name="progress">進捗通知（nullでも可）</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<WarmResult> WarmAsync(
        IProgress<WarmProgress>? progress = null,
        CancellationToken ct = default)
    {
        var dlls = PeImportParser.GetImportedDlls(_gxWorks3ExePath);
        int succeeded = 0, skipped = 0;
        int index = 0;

        foreach (var dllName in dlls)
        {
            if (ct.IsCancellationRequested) break;
            index++;

            string? foundPath = FindDll(dllName);
            if (foundPath == null)
            {
                skipped++;
                continue;
            }

            try
            {
                await WarmFileAsync(foundPath, ct);
                succeeded++;
                progress?.Report(new WarmProgress(dllName, index, dlls.Count, true));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                skipped++;
                progress?.Report(new WarmProgress(dllName, index, dlls.Count, false));
            }
        }

        return new WarmResult(succeeded, skipped, dlls.Count);
    }

    private string? FindDll(string dllName)
    {
        foreach (var dir in _searchDirs)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var path = Path.Combine(dir, dllName);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>
    /// ファイルをシーケンシャルに読み込んでOSページキャッシュに乗せます。
    /// 読み込んだデータは捨てるだけでよい（OSキャッシュには残る）。
    /// </summary>
    private static async Task WarmFileAsync(string path, CancellationToken ct)
    {
        const int bufferSize = 1024 * 64; // 64KB
        var buffer = new byte[bufferSize];

        using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize,
            FileOptions.SequentialScan | FileOptions.Asynchronous);

        int bytesRead;
        do
        {
            bytesRead = await fs.ReadAsync(buffer, ct);
        } while (bytesRead > 0);
    }
}

public record WarmResult(int Succeeded, int Skipped, int Total)
{
    public override string ToString() =>
        $"{Succeeded}/{Total} DLLをキャッシュ済み（{Skipped}件スキップ）";
}

public record WarmProgress(string DllName, int Current, int Total, bool Success)
{
    public int PercentComplete => Total == 0 ? 100 : (int)(Current * 100.0 / Total);
}
