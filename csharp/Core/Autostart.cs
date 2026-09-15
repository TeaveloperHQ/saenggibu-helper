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

    /// <summary>메인 exe에 동봉된 메모 도구를 꺼내 두는 고정 경로(작업표시줄 고정·자동시작 대상).</summary>
    public static string MemoInstallPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        OperatingSystem.IsWindows() ? "SaenggibuHelper" : "saenggibu-helper", "수업메모.exe");

    /// <summary>메모 exe를 찾는다 — 메인 exe 옆(publish-win.sh 폴더 배포) → 꺼내 둔 설치 경로. 없으면 null.</summary>
    public static string? FindMemoExe()
    {
        var dir = AppContext.BaseDirectory;
        foreach (var name in MemoExeNames)
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p)) return p;
        }
        return File.Exists(MemoInstallPath) ? MemoInstallPath : null;
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
        if (File.Exists(MemoInstallPath)) return MemoInstallPath;
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
        string arg = popup ? " --popup" : " --tray";
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
        k?.SetValue(AppName, $"\"{exePath}\" --tray");   // 부팅 시엔 팝업 없이 트레이에만 상주
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
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Arguments = "--tray" });
        }
        catch { /* 자동시작 실패는 치명적이지 않음 */ }
    }

    /// <summary>메인 exe에 동봉된 메모 도구(리소스 'memo.exe')를 <see cref="MemoInstallPath"/>로 꺼낸다.
    /// 이미 같은 빌드면 그대로, 새 빌드면 실행 중인 메모를 끄고 교체한다(다시 띄우는 건 EnsureMemoInstalled).
    /// 시작 메뉴에 '수업 메모' 바로가기도 만들어 작업표시줄에 고정할 수 있게 한다. 동봉 안 됨(개발 빌드)·비Windows면 false.</summary>
    public static bool InstallEmbeddedMemo(System.Reflection.Assembly host)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var res = host.GetManifestResourceStream("memo.exe");
        if (res == null) return false;
        var exe = MemoInstallPath;
        var stampPath = exe + ".build";
        string build = $"{host.ManifestModule.ModuleVersionId}:{res.Length}";
        bool same = File.Exists(exe) && File.Exists(stampPath) && File.ReadAllText(stampPath) == build;
        if (!same)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
                using (p) try { p.Kill(); p.WaitForExit(5000); } catch { }
            var tmp = exe + ".new";
            using (var fs = File.Create(tmp)) res.CopyTo(fs);
            File.Move(tmp, exe, overwrite: true);
            File.WriteAllText(stampPath, build);
        }
        CreateStartMenuShortcut(exe);
        return true;
    }

    /// <summary>시작 메뉴 '수업 메모' 바로가기(없을 때만). 시작 메뉴에서 우클릭 → 작업 표시줄에 고정.</summary>
    [SupportedOSPlatform("windows")]
    private static void CreateStartMenuShortcut(string exe)
    {
        try
        {
            var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "수업 메모.lnk");
            if (File.Exists(lnk)) return;
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            var shell = Activator.CreateInstance(shellType)!;
            var sc = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk })!;
            void Set(string prop, string val) => sc.GetType().InvokeMember(prop, System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { val });
            Set("TargetPath", exe);
            Set("WorkingDirectory", Path.GetDirectoryName(exe)!);
            Set("Description", "수업 메모 — 수업 중 관찰을 학급 명단에 빠르게 기록");
            sc.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
        }
        catch { /* 바로가기 실패는 치명적이지 않음 */ }
    }
}
