using GXW3Boost.Core;
using Microsoft.Win32;
using System.Drawing;
using System.Reflection;

namespace GXW3Boost.Warmer;

/// <summary>
/// システムトレイアイコンと関連するすべての機能を管理します。
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _warmingTimer;
    private readonly System.Windows.Forms.Timer _associationMonitorTimer;
    private CancellationTokenSource? _warmingCts;
    private bool _isWarming = false;

    private const int InitialWarmDelayMs    = 30_000;           // 初回: 30秒後
    private const int WarmIntervalMs        = 30 * 60 * 1000;   // 以降: 30分ごと
    private const int AssocMonitorIntervalMs = 5 * 60 * 1000;   // 関連付け監視: 5分ごと

    public TrayApplicationContext()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "GXW3Boost - GXWorks3高速化",
            Visible = true,
            ContextMenuStrip = CreateContextMenu(),
        };
        _notifyIcon.DoubleClick += (_, _) => ShowDiagnosticsAsync();

        _warmingTimer = new System.Windows.Forms.Timer { Interval = InitialWarmDelayMs };
        _warmingTimer.Tick += OnWarmingTimerTick;
        _warmingTimer.Start();

        _associationMonitorTimer = new System.Windows.Forms.Timer { Interval = AssocMonitorIntervalMs };
        _associationMonitorTimer.Tick += OnAssociationMonitorTick;
        _associationMonitorTimer.Start();

        // Windowsシャットダウン・ログオフ時の安全終了
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    // ========== アイコン読み込み ==========

    /// <summary>
    /// 埋め込みリソースから app.ico を読み込みます。
    /// 失敗時はシステムアイコンにフォールバックします。
    /// </summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            // リソース名: "GXW3Boost.Warmer.Resources.app.ico"
            var resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));

            if (resourceName != null)
            {
                using var stream = asm.GetManifestResourceStream(resourceName)!;
                return new Icon(stream, new Size(16, 16)); // トレイ用は16x16
            }
        }
        catch { /* フォールバックへ */ }

        // フォールバック: SystemIcons
        return SystemIcons.Application;
    }

    // ========== コンテキストメニュー ==========

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        var title = new ToolStripMenuItem("GXW3Boost") { Enabled = false };
        menu.Items.Add(title);
        menu.Items.Add(new ToolStripSeparator());

        var warmNow = new ToolStripMenuItem("今すぐウォームアップ(&W)");
        warmNow.Click += (_, _) => WarmNowAsync();
        menu.Items.Add(warmNow);

        var diag = new ToolStripMenuItem("診断レポートを表示(&D)");
        diag.Click += (_, _) => ShowDiagnosticsAsync();
        menu.Items.Add(diag);

        menu.Items.Add(new ToolStripSeparator());

        var reassoc = new ToolStripMenuItem(".gx3 関連付けを再設定(&R)");
        reassoc.Click += (_, _) => Reassociate();
        menu.Items.Add(reassoc);

        var uninstall = new ToolStripMenuItem("関連付けを削除して終了(&U)");
        uninstall.Click += (_, _) => UninstallAndExit();
        menu.Items.Add(uninstall);

        menu.Items.Add(new ToolStripSeparator());

        var about = new ToolStripMenuItem("バージョン情報(&A)");
        about.Click += (_, _) => ShowAbout();
        menu.Items.Add(about);

        var exit = new ToolStripMenuItem("終了(&X)");
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);

        return menu;
    }

    // ========== ウォームアップ ==========

    private async void OnWarmingTimerTick(object? sender, EventArgs e)
    {
        _warmingTimer.Interval = WarmIntervalMs;
        await RunWarmingAsync(silent: true);
    }

    private async void WarmNowAsync() => await RunWarmingAsync(silent: false);

    private async Task RunWarmingAsync(bool silent)
    {
        if (_isWarming) return;
        _isWarming = true;

        var gxPath = GxWorks3Locator.FindGxWorks3Exe();
        if (gxPath == null)
        {
            if (!silent)
                _notifyIcon.ShowBalloonTip(4000, "GXW3Boost",
                    "GXW3.exeが見つかりません。", ToolTipIcon.Warning);
            _isWarming = false;
            return;
        }

        _warmingCts = new CancellationTokenSource();
        try
        {
            if (!silent)
                _notifyIcon.ShowBalloonTip(2000, "GXW3Boost",
                    "DLLキャッシュのウォームアップを開始しました...", ToolTipIcon.Info);

            // 常駐 Warmer は時間に余裕があるので全 tier をウォームする
            var warmer = new DllPrewarmer(gxPath);
            var result = await warmer.WarmAsync(
                maxTier: WarmTier.Background,
                ct: _warmingCts.Token);

            if (!silent)
                _notifyIcon.ShowBalloonTip(3000, "GXW3Boost",
                    $"ウォームアップ完了: {result}", ToolTipIcon.Info);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!silent)
                _notifyIcon.ShowBalloonTip(4000, "GXW3Boost",
                    $"ウォームアップエラー:\n{ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            _warmingCts?.Dispose();
            _warmingCts = null;
            _isWarming = false;
        }
    }

    // ========== 関連付け監視 ==========

    private void OnAssociationMonitorTick(object? sender, EventArgs e)
    {
        if (!FileAssociationManager.IsBoostActive())
        {
            _notifyIcon.ShowBalloonTip(
                6000,
                "GXW3Boost - 設定が変更されました",
                "GXWorks3の更新により .gx3 の高速化が無効になっている可能性があります。\n" +
                "右クリック →「.gx3 関連付けを再設定」で復元できます。",
                ToolTipIcon.Warning);
        }
    }

    // ========== 関連付け再設定 ==========

    private void Reassociate()
    {
        try
        {
            var launcherPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "launcher", "GXW3Boost.Launcher.exe"));

            if (!File.Exists(launcherPath))
            {
                MessageBox.Show(
                    $"ランチャーが見つかりません:\n{launcherPath}\n\nGXW3Boostを再インストールしてください。",
                    "GXW3Boost", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            FileAssociationManager.Install(launcherPath);
            _notifyIcon.ShowBalloonTip(3000, "GXW3Boost",
                ".gx3 の関連付けを再設定しました。", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"関連付けの設定に失敗しました:\n{ex.Message}",
                "GXW3Boost", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ========== アンインストール補助 ==========

    private void UninstallAndExit()
    {
        var result = MessageBox.Show(
            ".gx3 ファイルの関連付けを元に戻してGXW3Boostを終了します。\n\n続けますか？",
            "GXW3Boost - 関連付けを削除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        FileAssociationManager.Uninstall();
        ExitApp();
    }

    // ========== 診断 ==========

    private async void ShowDiagnosticsAsync()
    {
        try
        {
            var report = await new DiagnosticsReporter().RunAsync();
            var tempFile = Path.Combine(
                Path.GetTempPath(),
                $"GXW3Boost_Diag_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            await File.WriteAllTextAsync(tempFile, report.ToText(), System.Text.Encoding.UTF8);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempFile, UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"診断中にエラーが発生しました:\n{ex.Message}",
                "GXW3Boost 診断", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ========== バージョン情報 ==========

    private void ShowAbout()
    {
        MessageBox.Show(
            "GXW3Boost v1.0.1\n\n" +
            "GXWorks3(32bit)の起動を高速化するOSSツールです。\n" +
            "GXWorks3本体には一切変更を加えません。\n\n" +
            "GitHub: https://github.com/mokouliszt/GXW3Boost",
            "GXW3Boost について",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // ========== 終了処理 ==========

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        // Windowsシャットダウン・ログオフ時：関連付けは触らずプロセスのみ終了
        CleanupResources();
    }

    private void ExitApp()
    {
        CleanupResources();
        Application.Exit();
    }

    private void CleanupResources()
    {
        _warmingCts?.Cancel();
        _warmingTimer.Stop();
        _associationMonitorTimer.Stop();
        _notifyIcon.Visible = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            _warmingCts?.Cancel();
            _warmingCts?.Dispose();
            _warmingTimer.Dispose();
            _associationMonitorTimer.Dispose();
            _notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
