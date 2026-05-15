namespace GXW3Boost.Core;

/// <summary>
/// ウォームアップ対象の優先度（tier）。
/// 値が小さいほど起動初期に必要で、最優先で読み込む。
/// </summary>
public enum WarmTier
{
    /// <summary>1秒以内に必要な中核モジュール（コアDLL、エントリポイント、ロケール基幹）。</summary>
    Critical = 1,

    /// <summary>1〜5秒で必要なプラグイン/エディタ/パラメータDB。</summary>
    Essential = 2,

    /// <summary>5秒以降に必要な補助モジュールと隣接 MELSOFT コンポーネント。</summary>
    Background = 3,
}

/// <summary>ウォームアップ対象ファイル。</summary>
public sealed record WarmTarget(string Path, WarmTier Tier);

/// <summary>
/// GXWorks3 起動時に読まれるファイル群の所在を、
/// インストール先に依存しないディレクトリパターンとして保持します。
///
/// 設計意図:
///   - 個別ファイル名のハードコードを避け、サブディレクトリ＋拡張子の組合せで列挙する
///   - GXWorks3 のバージョン更新でプラグインが増減しても自然に追従する
///   - インストール先が動的（GxWorks3Locator で検出）でも追従する
///
/// 出典:
///   GXW3.exe 起動時の WPR (FileIO + DiskIO) トレースで観測したアクセスパスを、
///   サブディレクトリ単位の汎用ルールに集約したもの。具体パスは保持しない。
/// </summary>
public static class WarmManifest
{
    /// <summary>
    /// 列挙ルール。
    /// </summary>
    /// <param name="Tier">優先度</param>
    /// <param name="RootIsMelsoft">true=MELSOFT ルートから、false=GPPW3 ルートから</param>
    /// <param name="RelativeDir">ベースからの相対ディレクトリ（"" は直下）</param>
    /// <param name="Recursive">サブディレクトリも再帰列挙するか</param>
    /// <param name="Patterns">列挙する glob パターン（例 "*.dll"）</param>
    private sealed record DirRule(
        WarmTier Tier,
        bool RootIsMelsoft,
        string RelativeDir,
        bool Recursive,
        string[] Patterns);

    // ロケール固有のリソースサブフォルダ名。ja-JP 固定。
    private const string LocaleDir = "ja-JP";

    // 列挙ルールテーブル。
    // - WorkWindowPlugin/DockingWindowPlugin/DialogPlugin/CommandPlugin は
    //   プラグインごとにサブフォルダがあるため Recursive=true でまとめて拾う。
    // - 直下 (GPPW3 ルート) は .dll 数が多いので Recursive=false で .dll/.exe/.config だけに絞る。
    // - 隣接ディレクトリ (Easysocket/MSF) は Critical/Background のどちらかに分割。
    private static readonly DirRule[] Rules =
    {
        // === Tier 1: Critical（起動 1秒以内）===

        // GPPW3 直下のコア DLL / 設定 / EXE
        new(WarmTier.Critical, false, "",                 false, new[] { "*.dll", "*.exe", "*.exe.config", "*.cfg" }),

        // 中核エンティティ層
        new(WarmTier.Critical, false, "EntityComponents", false, new[] { "*.dll" }),

        // プラットフォーム層 (Platform 直下と Platform\Native)
        new(WarmTier.Critical, false, "Platform",         true,  new[] { "*.dll", "*.dat" }),

        // サービスバス Native / Managed
        new(WarmTier.Critical, false, "Service\\Native",  false, new[] { "*.dll", "*.config" }),
        new(WarmTier.Critical, false, "Service\\Managed", false, new[] { "*.dll", "*.config" }),
        new(WarmTier.Critical, false, "Service",          false, new[] { "*.config" }),

        // ユーティリティ
        new(WarmTier.Critical, false, "Utility",          false, new[] { "*.dll" }),

        // レシピ
        new(WarmTier.Critical, false, "Recipe",           false, new[] { "*.txt" }),
        new(WarmTier.Critical, false, "Recipe\\Root",     false, new[] { "*.xml", "*.txc" }),

        // ロケール基幹リソース (ja-JP / ja-jp 両表記が混在)
        new(WarmTier.Critical, false, LocaleDir,          false, new[] { "*.msg", "*.dll" }),
        new(WarmTier.Critical, false, "ja-jp",            false, new[] { "*.dll" }),

        // 隣接: Easysocket ルート (EasysocketW.dll など)
        new(WarmTier.Critical, true,  "Easysocket",       false, new[] { "*.dll" }),

        // === Tier 2: Essential（1〜5秒）===

        // 各種プラグイン (再帰で .dll / .cfg / locale msg を拾う)
        new(WarmTier.Essential, false, "WorkWindowPlugin",    true, new[] { "*.dll", "*.cfg" }),
        new(WarmTier.Essential, false, "DockingWindowPlugin", true, new[] { "*.dll", "*.cfg" }),
        new(WarmTier.Essential, false, "DialogPlugin",        true, new[] { "*.dll", "*.cfg" }),
        new(WarmTier.Essential, false, "CommandPlugin",       true, new[] { "*.dll", "*.cfg" }),

        // プラグイン配下のロケールリソースとパラメータデータ
        new(WarmTier.Essential, false, "WorkWindowPlugin",    true, new[] { "*.msg" }),
        new(WarmTier.Essential, false, "DockingWindowPlugin", true, new[] { "*.msg", "*.def", "*.xml" }),
        new(WarmTier.Essential, false, "DialogPlugin",        true, new[] { "*.msg" }),
        new(WarmTier.Essential, false, "CommandPlugin",       true, new[] { "*.msg" }),

        // Syncfusion 等のコンポーネントローダ
        new(WarmTier.Essential, false, "Components",          false, new[] { "*.dll" }),

        // パラメータDB (大きいので Tier2)
        new(WarmTier.Essential, false, "Service\\Native",     false, new[] { "*.db" }),

        // === Tier 3: Background（5秒以降、常駐ウォーマー時のみ）===

        // プロジェクト雛形DB
        new(WarmTier.Background, false, "Recipe",             false, new[] { "*.db" }),

        // Easysocket のサブディレクトリ (CodeGenerator/Communication/Monitoring/ProjectDataBase3 等)
        new(WarmTier.Background, true,  "Easysocket",         true,  new[] { "*.dll", "*.exe" }),

        // MELSOFT 共通サービス (MSF\SystemLabel, MSF\VersionManager 等)
        new(WarmTier.Background, true,  "MSF",                true,  new[] { "*.dll", "*.def" }),
    };

    /// <summary>
    /// 与えられた GPPW3 / MELSOFT ディレクトリに対し、
    /// 実在するウォームアップ対象ファイルを tier 付きで列挙します。
    /// 同一ファイルが複数ルールにマッチした場合は、より高い優先度（小さい tier）を採用します。
    /// </summary>
    /// <param name="gppw3Dir">GXW3.exe があるディレクトリ。必須。</param>
    /// <param name="melsoftDir">MELSOFT ルート。null の場合は Easysocket/MSF を含めない。</param>
    public static IReadOnlyList<WarmTarget> Enumerate(string gppw3Dir, string? melsoftDir)
    {
        // 同じパスが複数 tier に現れた場合に最優先 tier を保持する
        var bestTier = new Dictionary<string, WarmTier>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in Rules)
        {
            var baseDir = rule.RootIsMelsoft ? melsoftDir : gppw3Dir;
            if (string.IsNullOrEmpty(baseDir)) continue;

            var targetDir = string.IsNullOrEmpty(rule.RelativeDir)
                ? baseDir
                : Path.Combine(baseDir, rule.RelativeDir);

            if (!Directory.Exists(targetDir)) continue;

            var searchOption = rule.Recursive
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            foreach (var pattern in rule.Patterns)
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(targetDir, pattern, searchOption);
                }
                catch
                {
                    // アクセス拒否等は無視
                    continue;
                }

                foreach (var path in files)
                {
                    // 再帰列挙時、別ロケールのリソースが混ざるのを避ける
                    if (rule.Recursive && IsForeignLocale(path, targetDir))
                        continue;

                    if (bestTier.TryGetValue(path, out var existing))
                    {
                        if (rule.Tier < existing)
                            bestTier[path] = rule.Tier;
                    }
                    else
                    {
                        bestTier[path] = rule.Tier;
                    }
                }
            }
        }

        var result = new List<WarmTarget>(bestTier.Count);
        foreach (var (path, tier) in bestTier)
            result.Add(new WarmTarget(path, tier));

        // tier 順 → サイズ昇順（小さい方が並列度を上げやすい）でソート
        result.Sort((a, b) =>
        {
            int c = a.Tier.CompareTo(b.Tier);
            if (c != 0) return c;
            return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }

    /// <summary>
    /// 再帰列挙時、ja-JP 以外のロケールフォルダ配下を除外する判定。
    /// 例: …\WorkWindowPlugin\Ladder\en-US\Plugin_RC2.msg は除外。
    /// </summary>
    private static bool IsForeignLocale(string filePath, string baseDir)
    {
        var rel = Path.GetRelativePath(baseDir, filePath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var part in parts)
        {
            if (LooksLikeLocale(part) &&
                !part.Equals(LocaleDir, StringComparison.OrdinalIgnoreCase) &&
                !part.Equals("ja-jp", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    // "xx-XX" 形式（en-US, zh-CN 等）の判定
    private static bool LooksLikeLocale(string name)
    {
        if (name.Length != 5) return false;
        if (name[2] != '-') return false;
        for (int i = 0; i < 5; i++)
        {
            if (i == 2) continue;
            if (!char.IsLetter(name[i])) return false;
        }
        return true;
    }
}
