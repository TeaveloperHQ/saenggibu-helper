using Avalonia;
using System;
using System.Threading;

namespace Memo;

// 수업 중 빠른 메모 — 트레이 상주 + 팝업. app/ui/quicknote.py 이식.
// 메인 앱과 별개의 가벼운 프로세스(모델을 올리지 않아 즉시 뜸), 같은 로컬 데이터 폴더의 명단을 공유.
class Program
{
    /// <summary>이미 떠 있는 메모 도구에 '팝업 열기'를 요청하는 이벤트 이름.</summary>
    public const string ShowEventName = @"Local\SaenggibuQuickNote.Show";

    [STAThread]
    public static void Main(string[] args)
    {
        // 한 번만 상주: 이미 떠 있으면(작업표시줄 고정 아이콘·시작 메뉴 실행 등) 기존 인스턴스에 팝업을 요청하고 바로 끝낸다
        Mutex? single = null;
        if (OperatingSystem.IsWindows())
        {
            single = new Mutex(true, @"Local\SaenggibuQuickNote", out bool first);
            if (!first)
            {
                if (Array.IndexOf(args, "--tray") < 0)
                    try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); } catch { }
                return;
            }
        }
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        finally { single?.Dispose(); }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { EnableIme = true })
            .WithInterFont()
            .LogToTrace();
}
