using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Memo;

/// <summary>
/// Windows 전역 단축키(Ctrl+Alt+M). app/ui/quicknote.py 의 RegisterHotKey 흐름 이식.
/// 전용 스레드에서 RegisterHotKey(null 스레드 훅) + GetMessage 루프로 WM_HOTKEY를 받는다.
/// Windows가 아니면 아무 것도 하지 않는다(트레이 클릭으로 대체).
/// </summary>
public sealed class WinHotkey : IDisposable
{
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    private const uint WM_HOTKEY = 0x0312, WM_QUIT = 0x0012;
    private const int HOTKEY_ID = 1;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }

    private Thread? _thread;
    private uint _threadId;
    private readonly Action _onFire;
    private readonly uint _mods, _vk;

    public WinHotkey(Action onFire, uint mods, uint vk) { _onFire = onFire; _mods = mods; _vk = vk; }

    // "Ctrl+Alt+M" → (modifiers, virtual-key). 실패 시 Ctrl+Alt+M.
    public static (uint mods, uint vk) Parse(string? s)
    {
        uint mods = 0, vk = 0;
        foreach (var raw in (s ?? "").Split('+'))
        {
            var p = raw.Trim().ToLowerInvariant();
            if (p is "ctrl" or "control") mods |= MOD_CONTROL;
            else if (p == "alt") mods |= MOD_ALT;
            else if (p == "shift") mods |= 0x0004;
            else if (p is "win" or "meta") mods |= 0x0008;
            else if (p.Length == 1 && char.IsLetterOrDigit(p[0])) vk = char.ToUpperInvariant(p[0]);
            else if (p.Length >= 2 && p[0] == 'f' && int.TryParse(p[1..], out var fn) && fn is >= 1 and <= 12) vk = (uint)(0x70 + fn - 1);
        }
        if (vk == 0 || mods == 0) { mods = MOD_CONTROL | MOD_ALT; vk = 0x4D; }
        return (mods, vk);
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindows()) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "memo-hotkey" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Loop()
    {
        _threadId = GetCurrentThreadId();
        // 스레드에 귀속된 전역 단축키(hWnd=IntPtr.Zero) → WM_HOTKEY가 이 스레드 큐로 전달됨
        if (!RegisterHotKey(IntPtr.Zero, HOTKEY_ID, _mods | MOD_NOREPEAT, _vk)) return;
        try
        {
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_HOTKEY && msg.wParam.ToInt32() == HOTKEY_ID)
                    _onFire();
            }
        }
        finally { UnregisterHotKey(IntPtr.Zero, HOTKEY_ID); }
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows() && _threadId != 0)
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
