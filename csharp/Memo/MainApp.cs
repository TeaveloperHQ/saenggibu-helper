using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Saenggibu;

namespace Memo;

/// <summary>
/// 메모 도구에서 메인 앱(생기부 도우미) 띄우기.
/// 메인 앱이 실행될 때 설정에 적어 둔 exe 경로를 쓴다(메모는 프로그램 폴더, 메인 exe는 선생님이 둔 곳).
/// 이미 떠 있으면 새로 실행하지 않고 그 창을 앞으로 가져온다.
/// </summary>
public static class MainApp
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;

    /// <summary>설정에 적힌 메인 exe 경로(없거나 파일이 사라졌으면 null).</summary>
    public static string? ExePath(Settings settings)
    {
        var p = settings.Get<string>("main_exe_path");
        return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
    }

    /// <summary>메인 앱을 앞으로 가져오거나 실행. 경로를 모르면 false.</summary>
    public static bool Launch(Settings settings)
    {
        var exe = ExePath(settings);
        if (exe == null) return false;
        try
        {
            if (OperatingSystem.IsWindows())
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
                    using (p)
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(p.MainWindowHandle, SW_RESTORE);
                            SetForegroundWindow(p.MainWindowHandle);
                            return true;
                        }
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}
