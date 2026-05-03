using GXW3Boost.Warmer;

// ============================================================
// GXW3Boost.Warmer
//
// PC起動時に自動起動し、システムトレイに常駐します。
// 定期的にDLLウォームアップと関連付け監視を行います。
// ============================================================

// 二重起動防止（名前付きMutex）
const string MutexName = "Global\\GXW3Boost.Warmer.SingleInstance";
using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);

if (!isFirstInstance)
{
    // 既に起動中 → 静かに終了
    return;
}

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.SetHighDpiMode(HighDpiMode.SystemAware);

// カスタム ApplicationContext でトレイアイコンを管理
Application.Run(new TrayApplicationContext());

// Mutex は using で解放される
