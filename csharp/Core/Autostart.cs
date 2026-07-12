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

    /// <summary>메인 exe 옆에서 메모 exe를 찾는다(배포용 자동시작 등록에 사용). 없으면 null.</summary>
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

    /// <summary>실행할 메모 바이너리(배포=exe/리눅스 실행파일, 개발=형제 Memo/bin의 Memo.dll)를 찾는다.</summary>
    public static string? FindMemoLaunch()
    {
        var dir = AppContext.BaseDirectory;
        foreach (var name in new[] { "수업메모.exe", "Memo.exe", "수업메모", "Memo", "Memo.dll" })
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p)) return p;
        }
        // 개발 폴백: …/Gui/bin/<cfg>/<tfm>/ 옆의 …/Memo/bin/.../Memo.dll
        try
        {
            for (var cur = new DirectoryInfo(dir); cur?.Parent != null; cur = cur.Parent)
            {
                var memoBin = Path.Combine(cur.Parent.FullName, "Memo", "bin");
                if (Directory.Exists(memoBin))
                {
                    var dll = Directory.GetFiles(memoBin, "Memo.dll", SearchOption.AllDirectories).FirstOrDefault();
                    if (dll != null) return dll;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>메모 도구 실행(.dll이면 dotnet으로). popup=true면 즉시 팝업. 성공 시 true.</summary>
    public static bool LaunchMemo(bool popup = false)
    {
        var p = FindMemoLaunch();
        if (p == null) return false;
        string arg = popup ? " --popup" : "";
        try
        {
            var psi = p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("dotnet", $"\"{p}\"{arg}")
                : new ProcessStartInfo(p) { UseShellExecute = true, Arguments = arg.Trim() };
            Process.Start(psi);
            return true;
        }
        catch { return false; }
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
