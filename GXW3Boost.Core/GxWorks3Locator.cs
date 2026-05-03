using Microsoft.Win32;

namespace GXW3Boost.Core;

/// <summary>
/// GXW3.exeのインストールパスをレジストリまたは既知のパスから検索します。
/// </summary>
public static class GxWorks3Locator
{
    private const string ExeName = "GXW3.exe";

    // ✅ 実機確認済みのフォールバックパス
    private static readonly string FallbackPath =
        @"C:\Program Files (x86)\MELSOFT\GPPW3\GXW3.exe";

    /// <summary>
    /// GXW3.exeのフルパスを返します。見つからない場合はnull。
    /// </summary>
    public static string? FindGxWorks3Exe()
    {
        // Windowsのインストール済みアプリ登録から探す
        var viaApps = FindViaUninstallRegistry();
        if (viaApps != null) return viaApps;

        // フォールバック
        return File.Exists(FallbackPath) ? FallbackPath : null;
    }

    private static string? FindViaUninstallRegistry()
    {
        string[] uninstallBases = {
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        };

        foreach (var basePath in uninstallBases)
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(basePath);
            if (baseKey == null) continue;

            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                using var subKey = baseKey.OpenSubKey(subKeyName);
                if (subKey == null) continue;

                var displayName = subKey.GetValue("DisplayName") as string ?? string.Empty;
                if (!displayName.Contains("GX Works3", StringComparison.OrdinalIgnoreCase))
                    continue;

                // InstallLocation がexeと同じフォルダを指す場合
                var installLocation = subKey.GetValue("InstallLocation") as string;
                if (!string.IsNullOrEmpty(installLocation))
                {
                    var exePath = Path.Combine(installLocation.TrimEnd('\\', '/'), ExeName);
                    if (File.Exists(exePath)) return exePath;
                }

                // InstallLocation が上位フォルダの場合は DisplayIcon のディレクトリを使う
                // 例: InstallLocation=…\MELSOFT, DisplayIcon=…\MELSOFT\GPPW3\GXWorks3.ico,0
                var displayIcon = subKey.GetValue("DisplayIcon") as string;
                if (!string.IsNullOrEmpty(displayIcon))
                {
                    var iconPath = displayIcon.Trim();
                    var comma = iconPath.LastIndexOf(',');
                    if (comma > 0 && int.TryParse(iconPath.AsSpan(comma + 1), out _))
                        iconPath = iconPath[..comma];
                    var iconDir = Path.GetDirectoryName(iconPath);
                    if (!string.IsNullOrEmpty(iconDir))
                    {
                        var exePath = Path.Combine(iconDir, ExeName);
                        if (File.Exists(exePath)) return exePath;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// GXW3.exeが存在するディレクトリを返します。
    /// </summary>
    public static string? FindGxWorks3Dir()
    {
        var exe = FindGxWorks3Exe();
        return exe == null ? null : Path.GetDirectoryName(exe);
    }
}
