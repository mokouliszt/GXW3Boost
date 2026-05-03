<div align="center">

<img src="assets/gxw3boost.ico" width="96" alt="GXW3Boost">

# GXW3Boost

**MELSOFT GXWorks3の起動を安全に最適化・高速化するタスクトレイアプリ**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-blue)](https://github.com/mokouliszt/GXW3Boost)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)

> GXWorks3本体には一切変更を加えません。

</div>

---

## 概要

本ソフトウェアは三菱電機株式会社製エンジニアリングソフトウェア MELSOFT GXWorks3（32bit）の起動の安全な最適化・高速化を試みることを目的としたタスクトレイ常駐型アプリケーションです。

<img width="253" height="191" alt="Image" src="https://github.com/user-attachments/assets/efe81e86-251d-4426-a0f7-5cad8372ba3c" />

GXW3Boost は **OSのファイル関連付けを経由するランチャー** と **バックグラウンド常駐のウォーマー** によって、GXWorks3本体に一切手を加えることなく起動を高速化します。

---

## 仕組み

```
.gx3 ダブルクリック
  └─ GXW3Boost.Launcher（ファイル関連付け）
       ├─ DLLウォームアップを非同期で開始（待たない）
       └─ GXW3.exe を普通に起動（ファイルパスをそのまま渡す）

GXW3Boost.Warmer（常駐トレイアプリ）
  ├─ 起動30秒後・以降30分ごとにDLLをOSキャッシュへ読み込み
  └─ 関連付けが外れていないか5分ごとに監視・通知
```

### なぜ速くなるか

GXWorks3が依存する大量のDLLをあらかじめOSのページキャッシュに乗せておくことで、GXWorks3がLoadLibraryを呼ぶ際のディスクI/Oをほぼゼロにします。

---

## 安全性

| リスクシナリオ | 対処 |
|---|---|
| GXWorks3が更新されDLL依存関係が変わった | ウォーマーが次サイクルで自動再スキャン。効果が一時低下するだけ |
| GXW3.exeのパスが変わった | レジストリから動的解決。失敗時は ShellExecute にフォールバック |
| インストーラーが関連付けを上書きした | ウォーマーが5分ごとに監視し、バルーン通知で警告 |
| GXW3Boost自体がクラッシュした | ShellExecute フォールバックでGXWorks3を直接起動 |
| GXW3Boostをアンインストールした | HKCUエントリのみ削除。HKLMのGXWorks3登録は無傷 |

**GXWorks3本体のファイル・レジストリは読み取りのみです。書き込みは一切行いません。**

---

## インストール

### 方法1：インストーラー（推奨）

[Releases](https://github.com/mokouliszt/GXW3Boost/releases) から `GXW3Boost_Setup_x.x.x.exe` をダウンロードして実行します。

- **管理者権限は不要**です（ユーザースコープにインストールされます）
- インストール先: `%LOCALAPPDATA%\GXW3Boost\`
- `.gx3` のファイル関連付けを自動設定します
- PC起動時にウォーマーが自動起動するよう登録します

### インストーラーが行う変更

```
[追加]
%LOCALAPPDATA%\GXW3Boost\launcher\GXW3Boost.Launcher.exe
%LOCALAPPDATA%\GXW3Boost\warmer\GXW3Boost.Warmer.exe

[レジストリ書き込み（HKCUのみ）]
HKCU\Software\Classes\.gx3\shell\open\command  ← .gx3 関連付け
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\GXW3Boost  ← 自動起動
```

---

## アンインストール

**設定 → アプリ → インストールされているアプリ** から「GXW3Boost」を選んでアンインストールします。

アンインストール時に行われること：

1. ウォーマープロセスを終了
2. インストールフォルダをすべて削除
3. `.gx3` 関連付け（HKCU）を削除 → GXWorks3本体の設定（HKLM）が自動的に復活
4. スタートアップ登録を削除
5. Windowsシェルへ変更を通知（エクスプローラーのアイコンが即時更新）

> **インストーラーを使わずに削除したい場合**  
> トレイアイコンを右クリック →「関連付けを削除して終了」を実行してから、フォルダを手動削除してください。

---

## ビルド方法

### 前提条件

| ツール | バージョン | 用途 |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0以上 | ビルド |
| [Inno Setup](https://jrsoftware.org/isinfo.php) | 6.x | インストーラー生成 |

### 手順

```powershell
# リポジトリをクローン
git clone https://github.com/mokouliszt/GXW3Boost.git
cd GXW3Boost

# self-contained ビルド（.NETランタイム同梱・配布推奨）
.\installer\build.ps1 -SelfContained

# framework-dependent ビルド（別途 .NET 8 Runtime が必要）
.\installer\build.ps1

# インストーラー生成をスキップ
.\installer\build.ps1 -SelfContained -SkipInstaller
```

### 生成物

```
publish\
  launcher\GXW3Boost.Launcher.exe   ← .gx3 起動ハンドラ
  warmer\GXW3Boost.Warmer.exe       ← 常駐トレイアプリ
dist\
  GXW3Boost_Setup_1.0.0.exe         ← Inno Setup インストーラー
```

---

## プロジェクト構成

```
GXW3Boost/
├── GXW3Boost.sln
├── GXW3Boost.Core/               # 共通ライブラリ（.NET 8 クラスライブラリ）
│   ├── GxWorks3Locator.cs        # レジストリからGXW3.exeを検索
│   ├── PeImportParser.cs         # PEインポートテーブルを静的解析
│   ├── DllPrewarmer.cs           # DLLをOSファイルキャッシュへ事前読み込み
│   ├── FileAssociationManager.cs # .gx3関連付けをHKCUで管理
│   └── DiagnosticsReporter.cs    # 環境診断・レポート出力
├── GXW3Boost.Launcher/           # .gx3ダブルクリック時のエントリポイント
│   └── Program.cs
├── GXW3Boost.Warmer/             # 常駐トレイアプリ（WinForms）
│   ├── Program.cs
│   └── TrayApplicationContext.cs
├── assets/
│   └── gxw3boost.ico             # アプリアイコン（16〜256px）
└── installer/
    ├── setup.iss                  # Inno Setup スクリプト
    └── build.ps1                  # ビルド＆パッケージングスクリプト
```

---

## トレイアイコンのメニュー

| メニュー項目 | 動作 |
|---|---|
| 今すぐウォームアップ | DLLキャッシュの読み込みを即時実行 |
| 診断レポートを表示 | 環境診断を実行しメモ帳で結果を表示 |
| .gx3 関連付けを再設定 | GXWorks3更新などで外れた関連付けを復元 |
| 関連付けを削除して終了 | HKCUの関連付けを削除してGXW3Boostを終了 |
| 終了 | 関連付けはそのままでウォーマーを終了 |

---

## 動作要件

- Windows 10 / 11
- GXWorks3（32bit）がインストール済みであること

---

## 免責事項

本ソフトウェアは現状有姿（AS IS）で提供されます。

- 本ソフトウェアの使用は**自己責任**のもとで行ってください。
- 本ソフトウェアの使用によって生じたいかなる損害（データ損失・業務停止・機器の不具合を含むがこれに限らない）についても、作者は一切の責任を負いません。
- 本ソフトウェアは三菱電機株式会社とは無関係の非公式ツールです。GXWorks3 の利用規約は必ずご確認のうえ、自己の判断と責任においてご使用ください。
- 実運用環境への適用前に、十分な検証を行うことを強く推奨します。

---

## ライセンス

[MIT License](LICENSE) © 2025 mokouliszt
