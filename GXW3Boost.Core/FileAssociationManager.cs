using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace GXW3Boost.Core;

/// <summary>
/// .gx3ファイルの関連付けをHKCU（ユーザースコープ）で管理します。
///
/// HKCU\Software\Classes\.gx3\shell\open\command に書き込むため
/// 管理者権限は不要です。また、ユーザースコープの関連付けは
/// HKLMのシステム全体の設定より優先されます。
/// </summary>
public static class FileAssociationManager
{
    private const string CommandKeyPath = @"Software\Classes\.gx3\shell\open\command";
    private const string HkcuGx3KeyPath = @"Software\Classes\.gx3";
    private const string BoostMarker    = "GXW3Boost.Launcher";

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern void SHChangeNotify(
        int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const int  SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST       = 0x0000;

    /// <summary>GXW3BoostがHKCU経由で有効になっているか確認します。</summary>
    public static bool IsBoostActive()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(CommandKeyPath);
            var value = key?.GetValue(null) as string;
            return value?.Contains(BoostMarker, StringComparison.OrdinalIgnoreCase) == true;
        }
        catch { return false; }
    }

    /// <summary>
    /// .gx3の関連付けをランチャー経由に設定します。
    /// GXWorks3本体の登録（HKLM/HKCR）は変更しません。
    ///
    /// HKCRからProgIDとDefaultIconをHKCUにコピーすることで、
    /// エクスプローラー上の.gx3アイコンが変わらないことを保証します。
    /// </summary>
    public static void Install(string launcherExePath)
    {
        // ① HKCRから既存のProgIDとアイコン設定を読み取る
        string? progId    = null;
        string? iconValue = null;

        using (var hkcrGx3 = Registry.ClassesRoot.OpenSubKey(@".gx3"))
        {
            progId = hkcrGx3?.GetValue(null) as string; // 例: "GXWorks3.Project"
        }

        // ProgIDが取れた場合はそのDefaultIconも読む
        if (!string.IsNullOrEmpty(progId))
        {
            using var progIdKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\DefaultIcon");
            iconValue = progIdKey?.GetValue(null) as string;
        }

        // ② HKCUに最小限のキーを作成する

        // .gx3 キーの既定値にProgIDをセット
        // （これがないとWindowsがアイコン解決でHKCRへフォールスルーできない場合がある）
        using (var hkcuGx3 = Registry.CurrentUser.CreateSubKey(HkcuGx3KeyPath, writable: true))
        {
            if (!string.IsNullOrEmpty(progId))
                hkcuGx3.SetValue(null, progId);
        }

        // DefaultIcon をHKCUのProgIDキー配下にもコピー
        // （HKCU の .gx3 キー作成によってアイコン解決パスが変わる場合への保険）
        if (!string.IsNullOrEmpty(progId) && !string.IsNullOrEmpty(iconValue))
        {
            using var hkcuIconKey = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\{progId}\DefaultIcon", writable: true);
            hkcuIconKey.SetValue(null, iconValue);
        }

        // ③ 起動コマンドをランチャー経由に設定
        using (var cmd = Registry.CurrentUser.CreateSubKey(CommandKeyPath, writable: true))
        {
            cmd.SetValue(null, $"\"{launcherExePath}\" \"%1\"");
        }

        NotifyShell();
    }

    /// <summary>
    /// HKCUの関連付けエントリをすべて削除します。
    /// HKCRのGXWorks3本体登録は元のまま残るので復元不要です。
    /// </summary>
    public static void Uninstall()
    {
        try
        {
            // Install時に作成したProgID配下のDefaultIconキーを削除
            var progId = GetHkcuProgId();
            if (!string.IsNullOrEmpty(progId))
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    $@"Software\Classes\{progId}\DefaultIcon",
                    throwOnMissingSubKey: false);

                // ProgIDキー自体が空になったら削除（他に値がある場合は残す）
                DeleteKeyIfEmpty(Registry.CurrentUser, $@"Software\Classes\{progId}");
            }

            // .gx3 配下を上から順に削除
            Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\Classes\.gx3\shell\open\command",
                throwOnMissingSubKey: false);

            DeleteKeyIfEmpty(Registry.CurrentUser, @"Software\Classes\.gx3\shell\open");
            DeleteKeyIfEmpty(Registry.CurrentUser, @"Software\Classes\.gx3\shell");
            DeleteKeyIfEmpty(Registry.CurrentUser, @"Software\Classes\.gx3");
        }
        catch { /* 削除失敗は無視 */ }
        finally
        {
            NotifyShell();
        }
    }

    /// <summary>現在有効な関連付けコマンドを返します（HKCU優先、次にHKCR）。</summary>
    public static string? GetCurrentCommand()
    {
        using var hkcuKey = Registry.CurrentUser.OpenSubKey(CommandKeyPath);
        var hkcuVal = hkcuKey?.GetValue(null) as string;
        if (hkcuVal != null) return hkcuVal;

        using var hkcrKey = Registry.ClassesRoot.OpenSubKey(@".gx3\shell\open\command");
        return hkcrKey?.GetValue(null) as string;
    }

    // ========== プライベートヘルパー ==========

    /// <summary>HKCUに保存されているProgIDを返します。</summary>
    private static string? GetHkcuProgId()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(HkcuGx3KeyPath);
            return key?.GetValue(null) as string;
        }
        catch { return null; }
    }

    /// <summary>
    /// サブキーも値も持たない空のレジストリキーのみを削除します。
    /// 何か残っている場合は削除しません（GXWorks3が書いた値を守るため）。
    /// </summary>
    private static void DeleteKeyIfEmpty(RegistryKey hive, string keyPath)
    {
        try
        {
            using var key = hive.OpenSubKey(keyPath);
            if (key == null) return;

            if (key.GetSubKeyNames().Length == 0 && key.GetValueNames().Length == 0)
                hive.DeleteSubKey(keyPath, throwOnMissingSubKey: false);
        }
        catch { }
    }

    /// <summary>Windowsシェルに関連付け変更を通知します。</summary>
    private static void NotifyShell()
    {
        try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); }
        catch { }
    }
}
