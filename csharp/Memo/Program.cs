using Avalonia;
using System;

namespace Memo;

// 수업 중 빠른 메모 — 트레이 상주 + 팝업. app/ui/quicknote.py 이식.
// 메인 앱과 별개의 가벼운 프로세스(모델을 올리지 않아 즉시 뜸), 같은 로컬 데이터 폴더의 명단을 공유.
class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { EnableIme = true })
            .WithInterFont()
            .LogToTrace();
}
