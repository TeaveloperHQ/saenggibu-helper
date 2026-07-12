using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using System;

namespace Memo;

public class App : Application
{
    private MemoPopup? _popup;
    private WinHotkey? _hotkey;

    public override void Initialize()
    {
        // Fluent 테마(코드 온리 — App.axaml 없음)
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _popup = new MemoPopup();

            var open = new NativeMenuItem("메모 열기 (Ctrl+Alt+M)");
            open.Click += (_, _) => Show();
            var quit = new NativeMenuItem("종료");
            quit.Click += (_, _) => { _hotkey?.Dispose(); desktop.Shutdown(); };

            var tray = new TrayIcon
            {
                ToolTipText = "수업 메모 — 클릭 또는 Ctrl+Alt+M",
                Menu = new NativeMenu { Items = { open, quit } },
            };
            try { tray.Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Memo/Assets/appicon.png"))); } catch { }
            tray.Clicked += (_, _) => Show();
            TrayIcon.SetIcons(this, new TrayIcons { tray });

            _hotkey = new WinHotkey(() => Dispatcher.UIThread.Post(Show));
            _hotkey.Start();

            // 메모 도구가 직접 실행되면 자기 자신을 자동시작 등록(상주 지속)
            if (Saenggibu.Autostart.IsSupported && OperatingSystem.IsWindows())
                try { var exe = Environment.ProcessPath; if (exe != null && !Saenggibu.Autostart.IsRegistered()) Saenggibu.Autostart.Register(exe); } catch { }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void Show() => _popup?.PopupBar();
}
