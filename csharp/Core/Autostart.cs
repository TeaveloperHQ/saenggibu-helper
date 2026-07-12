using System.Diagnostics;
using System.Runtime.Versioning;

namespace Saenggibu;

/// <summary>
/// Windows 시작 시 '수업 메모 도구' 자동 실행 등록/해제 + 실행 보장.
/// app/autostart.py 이식 — HKCU\...\Run (관리자 권한 불필요). 비Windows는 무동작.
/// 메인 앱이 설치·최초 실행되면 옆의 메모 exe를 자동시작 등록하고 즉시 띄운다.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "SaenggibuQuickNote";
    // 배포 시 메모 exe 후보명(한글 리네임 우선)
    private static readonly string[] MemoExeNames = { "수업메모.exe", "Memo.exe" };

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>메인 exe 옆에서 메모 exe를 찾는다(없으면 null).</summary>
    public static string? FindMemoExe()
    {
        var dir = AppContext.BaseDirectory;
        foreach (var name in MemoExeNames)
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    [SupportedOSPlatform("windows")]
    public static bool IsRegistered()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(AppName) != null;
    }

    [SupportedOSPlatform("windows")]
    public static void Register(string exePath)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        k?.SetValue(AppName, $"\"{exePath}\"");
    }

    [SupportedOSPlatform("windows")]
    public static void Unregister()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        k?.DeleteValue(AppName, throwOnMissingValue: false);
    }

    /// <summary>메모 도구 자동시작 등록 + 아직 안 떠 있으면 실행. 메인 앱 시작 시 1회 호출.
    /// 비Windows·메모 exe 없음·이미 실행 중이면 안전하게 건너뛴다.</summary>
    public static void EnsureMemoInstalled()
    {
        if (!IsSupported) return;
        var exe = FindMemoExe();
        if (exe == null) return;
        try
        {
            if (!IsRegistered()) Register(exe);
            var procName = Path.GetFileNameWithoutExtension(exe);
            if (Process.GetProcessesByName(procName).Length == 0)
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch { /* 자동시작 실패는 치명적이지 않음 */ }
    }
}
