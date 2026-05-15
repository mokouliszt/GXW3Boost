using GXW3Boost.Core;
using System.Diagnostics;

// ============================================================
// GXW3Boost.Launcher
//
// .gx3ファイルのダブルクリック時に呼ばれます。
// 処理フロー:
//   1. GXW3.exeのパスを特定
//   2. DLLウォームアップを非同期で開始（待たない）
//   3. GXW3.exeにファイルパスを渡して起動
//   4. このプロセス自体はすぐ終了
//
// 安全設計:
//   - 何か失敗した場合は必ずフォールバックし、GXWorks3を何らかの形で起動する
//   - このランチャーがクラッシュしても .gx3 が開けなくなるだけ（GXWorks3本体は無傷）
// ============================================================

string? gx3FilePath = args.Length > 0 ? args[0] : null;

// GXW3.exeを探す
string? gxPath = GxWorks3Locator.FindGxWorks3Exe();

if (gxPath == null)
{
    // GXWorks3が見つからない場合：関連付けを元に戻してOSに任せる
    FileAssociationManager.Uninstall();

    if (gx3FilePath != null)
    {
        // Windowsのデフォルト処理にフォールバック
        Process.Start(new ProcessStartInfo
        {
            FileName = gx3FilePath,
            UseShellExecute = true, // ShellExecute = OS がデフォルトアプリで開く
        });
    }
    return;
}

// DLLウォームアップを非同期で開始（完了を待たずに GXWorks3 を起動）
// ウォームアップが途中でも GXWorks3 はそのまま動く
// Launcher 経由は時間制約があるため Tier1+2 (Critical+Essential) まで。
// Tier3 (Background) は常駐 Warmer が事前に温めている前提。
_ = Task.Run(async () =>
{
    try
    {
        var warmer = new DllPrewarmer(gxPath);
        await warmer.WarmAsync(maxTier: WarmTier.Essential);
    }
    catch
    {
        // ウォームアップ失敗は無視 - GXWorks3の起動に影響しない
    }
});

// GXWorks3を起動（ファイルパスをそのまま渡す）
try
{
    var args_str = gx3FilePath != null ? $"\"{gx3FilePath}\"" : "";

    var startInfo = new ProcessStartInfo
    {
        FileName = gxPath,
        Arguments = args_str,
        UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(gxPath),
    };

    Process.Start(startInfo);
}
catch (Exception)
{
    // 起動失敗のフォールバック: ShellExecuteで再試行
    if (gx3FilePath != null)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = gx3FilePath,
                UseShellExecute = true,
            });
        }
        catch
        {
            // これ以上できることはない
        }
    }
}

// ランチャーはここで終了（GXWorks3プロセスは独立して動き続ける）
