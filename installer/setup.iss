; ============================================================
; GXW3Boost - Inno Setup インストーラースクリプト
; ============================================================

#define AppName      "GXW3Boost"
#define AppVersion   "1.0.0"
#define AppPublisher "mokouliszt"
#define AppURL       "https://github.com/mokouliszt/GXW3Boost"
#define LauncherExe  "GXW3Boost.Launcher.exe"
#define WarmerExe    "GXW3Boost.Warmer.exe"

[Setup]
AppId={{24253853-0D89-41B6-ACFD-FE55D47AA80D}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases

; ユーザースコープインストール（管理者権限不要）
PrivilegesRequired=lowest
DefaultDirName={localappdata}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; 出力設定
OutputDir=..\dist
OutputBaseFilename=GXW3Boost_Setup_{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes

MinVersion=10.0.17763
RestartIfNeededByRun=no
SetupIconFile=..\assets\gxw3boost.ico
UninstallDisplayIcon={app}\gxw3boost.ico

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Messages]
WelcomeLabel1=GXWorks3 高速化ツール [name/ver] へようこそ
WelcomeLabel2=このウィザードは {#AppName} をインストールします。%n%nGXWorks3本体には一切変更を加えません。

[Files]
; --- framework-dependent（.NET 8 Runtime が別途必要） ---
; Source: "publish\launcher\{#LauncherExe}"; DestDir: "{app}\launcher"; Flags: ignoreversion
; Source: "publish\warmer\{#WarmerExe}";     DestDir: "{app}\warmer";   Flags: ignoreversion

; --- self-contained（ランタイム同梱、配布推奨） ---
; ★ recursesubdirs + createallsubdirs でランタイムファイルをすべてコピー
;    uninsremovereadonly でReadOnly属性でも削除できるようにする
Source: "..\publish\launcher\*"; DestDir: "{app}\launcher"; \
  Flags: ignoreversion recursesubdirs createallsubdirs uninsremovereadonly
Source: "..\publish\warmer\*"; DestDir: "{app}\warmer"; \
  Flags: ignoreversion recursesubdirs createallsubdirs uninsremovereadonly

[Dirs]
; ★ インストールディレクトリをアンインストール時に強制削除
;    （ランタイムログなど想定外ファイルが残っても削除される）
Name: "{app}\launcher"; Flags: uninsalwaysuninstall
Name: "{app}\warmer";   Flags: uninsalwaysuninstall
Name: "{app}";          Flags: uninsalwaysuninstall

[Registry]
; .gx3 ファイル関連付け（HKCU: ユーザースコープ、管理者権限不要）
Root: HKCU; Subkey: "Software\Classes\.gx3\shell\open\command"; \
  ValueType: string; ValueName: ""; \
  ValueData: """{app}\launcher\{#LauncherExe}"" ""%1"""; \
  Flags: uninsdeletekey

; Windows スタートアップ登録（ウォーマー自動起動）
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#AppName}"; \
  ValueData: """{app}\warmer\{#WarmerExe}"""; \
  Flags: uninsdeletevalue

[Icons]
Name: "{group}\{#AppName} 診断"; Filename: "{app}\warmer\{#WarmerExe}"
Name: "{group}\{#AppName} アンインストール"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\warmer\{#WarmerExe}"; \
  Description: "{#AppName} をバックグラウンドで起動する"; \
  Flags: postinstall nowait skipifsilent

[UninstallRun]
; ★ アンインストール開始前にウォーマーを強制終了する
;    （プロセスが残っているとファイル削除が失敗するため）
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#WarmerExe}"; \
  Flags: runhidden skipifdoesntexist waituntilterminated; \
  RunOnceId: "KillWarmer"

[Code]
// ============================================================
// カスタム Pascal スクリプト
// ============================================================

// Windows のインストール済みアプリ登録（Uninstall レジストリ）から GXW3.exe を探す
// PowerShell スクリプトを一時ファイルに書き出して実行する
function FindGxWorks3ViaUninstall(): Boolean;
var
  ScriptFile, ResultFile: String;
  Script: TArrayOfString;
  Results: TArrayOfString;
  ResultCode: Integer;
  Loc: String;
begin
  Result := False;
  ScriptFile := ExpandConstant('{tmp}\find_gxw3.ps1');
  ResultFile := ExpandConstant('{tmp}\gxw3path.txt');

  // InstallLocation が上位フォルダを指す場合に備えて DisplayIcon のディレクトリもフォールバックで確認する
  // 例: InstallLocation=…\MELSOFT, DisplayIcon=…\MELSOFT\GPPW3\GXWorks3.ico,0
  SetArrayLength(Script, 16);
  Script[0]  := '$bases = @(''HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'',';
  Script[1]  := '           ''HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'')';
  Script[2]  := '$e = Get-ChildItem $bases -EA SilentlyContinue | Get-ItemProperty -EA SilentlyContinue |';
  Script[3]  := '    Where-Object { $_.DisplayName -like ''*GX Works3*'' } | Select-Object -First 1';
  Script[4]  := 'if ($e) {';
  Script[5]  := '    $dir = $null';
  Script[6]  := '    if ($e.InstallLocation) {';
  Script[7]  := '        $loc = $e.InstallLocation.TrimEnd(''\'')';
  Script[8]  := '        if (Test-Path "$loc\GXW3.exe") { $dir = $loc }';
  Script[9]  := '    }';
  Script[10] := '    if (-not $dir -and $e.DisplayIcon) {';
  Script[11] := '        $iconDir = Split-Path ($e.DisplayIcon.Split('','')[0].Trim()) -Parent';
  Script[12] := '        if (Test-Path "$iconDir\GXW3.exe") { $dir = $iconDir }';
  Script[13] := '    }';
  Script[14] := '    if ($dir) { [System.IO.File]::WriteAllText(''' + ResultFile + ''', $dir) }';
  Script[15] := '}';

  if not SaveStringsToFile(ScriptFile, Script, False) then Exit;

  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptFile + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if LoadStringsFromFile(ResultFile, Results) and (GetArrayLength(Results) > 0) then
  begin
    Loc := Trim(Results[0]);
    Result := (Loc <> '') and FileExists(Loc + '\GXW3.exe');
  end;

  DeleteFile(ScriptFile);
  DeleteFile(ResultFile);
end;

// GXWorks3 のインストールを確認する
// 優先順位: Uninstall レジストリ → 既定パス
function IsGxWorks3Installed(): Boolean;
begin
  // 1. Windows のインストール済みアプリ登録（Uninstall レジストリ）から検索
  if FindGxWorks3ViaUninstall() then
  begin Result := True; Exit; end;

  // 2. 実機確認済みの既定パスを確認（最終フォールバック）
  Result := FileExists('C:\Program Files (x86)\MELSOFT\GPPW3\GXW3.exe');
end;

// インストール前チェック: GXWorks3 が見つからない場合はキャンセル
function InitializeSetup(): Boolean;
begin
  Result := IsGxWorks3Installed();
  if not Result then
    MsgBox(
      'MELSOFT GXWorks3（32bit）がこのPCにインストールされていることを確認できませんでした。' + #13#10#13#10 +
      'GXW3Boost は GXWorks3 の起動を高速化するツールです。' + #13#10 +
      'GXWorks3 をインストールしてから再度お試しください。' + #13#10#13#10 +
      'インストールをキャンセルします。',
      mbError, MB_OK);
end;

// 安全な更新: ファイルコピー前にウォーマープロセスを終了させる
// 上書きインストール時に {app}\warmer\GXW3Boost.Warmer.exe がロックされないようにする
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
    Exec(ExpandConstant('{sys}\taskkill.exe'),
      '/F /IM {#WarmerExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// ★ アンインストールのステップ管理
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  case CurUninstallStep of

    // usUninstall: ファイル削除の直前
    usUninstall:
    begin
      // [UninstallRun] の taskkill と二重で終了を確実にする
      Exec(ExpandConstant('{sys}\taskkill.exe'),
        '/F /IM {#WarmerExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;

    // usPostUninstall: すべての削除が完了した後
    usPostUninstall:
    begin
      // [Registry] の uninsdeletekey で通常は自動削除されるが、
      // 子キーが残っているケースの保険として再度クリーンアップ
      if RegKeyExists(HKCU, 'Software\Classes\.gx3\shell\open\command') then
        RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\.gx3\shell\open\command');

      // 空になった親キーを上へ向かって削除
      if RegKeyExists(HKCU, 'Software\Classes\.gx3\shell\open') then
      begin
        if not RegKeyExists(HKCU, 'Software\Classes\.gx3\shell\open\command') then
          RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\.gx3\shell\open');
      end;
      if RegKeyExists(HKCU, 'Software\Classes\.gx3\shell') then
      begin
        if not RegKeyExists(HKCU, 'Software\Classes\.gx3\shell\open') then
          RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\.gx3\shell');
      end;

      // Windowsシェルへ関連付け変更を通知（エクスプローラーが即時更新される）
      Exec(ExpandConstant('{sys}\ie4uinit.exe'),
        '-show', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;
