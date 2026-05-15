namespace GXW3Boost.Core;

/// <summary>
/// GXWorks3が依存するDLL/リソース群をOSのファイルキャッシュに事前読み込みします。
///
/// 原理: Windowsはファイルを読み込んだページをメモリ上にキャッシュします。
///       GXWorks3起動前に依存ファイルを読んでおくことで、
///       GXWorks3が LoadLibrary / 設定読込 する際のディスクI/Oを限りなく減らせます。
///
/// 安全性: GXWorks3プロセスには一切触れません。ファイルを読み取るだけです。
///         DLLのビット数（32/64）に関係なく動作します。
///
/// 対象選定:
///   1. GXW3.exe の PE インポートテーブル（直接依存DLL）
///   2. <see cref="WarmManifest"/> が示すサブディレクトリ＋拡張子ルール
///   をマージし、tier 順に並列度を変えて読み込みます。
/// </summary>
public class DllPrewarmer
{
    private readonly string _gxWorks3ExePath;
    private readonly string _gppw3Dir;
    private readonly string? _melsoftDir;

    // PE-import の DLL 名を解決するための検索ディレクトリ（優先順）
    private readonly string[] _peSearchDirs;

    // tier 別の並列度
    private const int ParallelismCritical = 4;
    private const int ParallelismEssential = 3;
    private const int ParallelismBackground = 2;

    public DllPrewarmer(string gxWorks3ExePath)
    {
        _gxWorks3ExePath = gxWorks3ExePath;
        _gppw3Dir = Path.GetDirectoryName(gxWorks3ExePath) ?? "";
        _melsoftDir = string.IsNullOrEmpty(_gppw3Dir) ? null : Path.GetDirectoryName(_gppw3Dir);

        _peSearchDirs = new[]
        {
            _gppw3Dir,
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), // C:\Windows\SysWOW64
            Environment.SystemDirectory,                                     // C:\Windows\System32
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        };
    }

    /// <summary>
    /// 指定された tier までのファイルを順次（tier 内は並列で）キャッシュウォームします。
    /// </summary>
    /// <param name="maxTier">この tier 以下のものだけを処理（既定: Background = 全部）</param>
    /// <param name="progress">進捗通知（nullでも可）</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<WarmResult> WarmAsync(
        WarmTier maxTier = WarmTier.Background,
        IProgress<WarmProgress>? progress = null,
        CancellationToken ct = default)
    {
        var targets = BuildTargets(maxTier);
        int total = targets.Count;
        if (total == 0) return new WarmResult(0, 0, 0);

        int succeeded = 0, skipped = 0, index = 0;
        var indexLock = new object();

        // tier 単位で処理を分割し、並列度を変える
        foreach (var tierGroup in GroupByTier(targets))
        {
            ct.ThrowIfCancellationRequested();

            int parallelism = tierGroup.Key switch
            {
                WarmTier.Critical   => ParallelismCritical,
                WarmTier.Essential  => ParallelismEssential,
                _                   => ParallelismBackground,
            };

            await Parallel.ForEachAsync(
                tierGroup.Value,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = ct,
                },
                async (target, token) =>
                {
                    bool ok;
                    try
                    {
                        await WarmFileAsync(target.Path, token);
                        ok = true;
                        Interlocked.Increment(ref succeeded);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch
                    {
                        ok = false;
                        Interlocked.Increment(ref skipped);
                    }

                    int idx;
                    lock (indexLock) { idx = ++index; }

                    progress?.Report(new WarmProgress(
                        Path.GetFileName(target.Path),
                        idx,
                        total,
                        ok,
                        target.Tier));
                });
        }

        return new WarmResult(succeeded, skipped, total);
    }

    /// <summary>
    /// PE-import 由来 + WarmManifest 由来のターゲットをマージして返します。
    /// 同一パスは PE-import 由来を優先し tier=Critical で扱います。
    /// </summary>
    private List<WarmTarget> BuildTargets(WarmTier maxTier)
    {
        var byPath = new Dictionary<string, WarmTier>(StringComparer.OrdinalIgnoreCase);

        // 1. PE-import 由来 (Tier=Critical 固定)
        foreach (var dllName in PeImportParser.GetImportedDlls(_gxWorks3ExePath))
        {
            var path = ResolvePeImportPath(dllName);
            if (path == null) continue;
            byPath[path] = WarmTier.Critical;
        }

        // 2. WarmManifest 由来
        foreach (var t in WarmManifest.Enumerate(_gppw3Dir, _melsoftDir))
        {
            if (byPath.TryGetValue(t.Path, out var existing))
            {
                if (t.Tier < existing) byPath[t.Path] = t.Tier;
            }
            else
            {
                byPath[t.Path] = t.Tier;
            }
        }

        // maxTier フィルタ → tier 順ソート
        var result = new List<WarmTarget>(byPath.Count);
        foreach (var (p, tier) in byPath)
        {
            if (tier <= maxTier) result.Add(new WarmTarget(p, tier));
        }
        result.Sort((a, b) => a.Tier.CompareTo(b.Tier));
        return result;
    }

    private string? ResolvePeImportPath(string dllName)
    {
        foreach (var dir in _peSearchDirs)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var path = Path.Combine(dir, dllName);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static Dictionary<WarmTier, List<WarmTarget>> GroupByTier(List<WarmTarget> targets)
    {
        var dict = new Dictionary<WarmTier, List<WarmTarget>>();
        foreach (var t in targets)
        {
            if (!dict.TryGetValue(t.Tier, out var list))
            {
                list = new List<WarmTarget>();
                dict[t.Tier] = list;
            }
            list.Add(t);
        }
        return dict;
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
        $"{Succeeded}/{Total} ファイルをキャッシュ済み（{Skipped}件スキップ）";
}

public record WarmProgress(string DllName, int Current, int Total, bool Success, WarmTier Tier)
{
    public int PercentComplete => Total == 0 ? 100 : (int)(Current * 100.0 / Total);
}
