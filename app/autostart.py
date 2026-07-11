"""Windows 시작 시 '수업 메모 도구' 자동 실행 등록/해제.

레지스트리 HKCU\\...\\Run 에 항목을 넣는다(관리자 권한 불필요, 현재 사용자만).
Windows 외 OS에서는 무동작(지원 안 함). 메인 앱이 아니라 '가벼운 메모 도구'를 등록한다.
"""
from __future__ import annotations

import sys
from pathlib import Path

_RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
_APP_NAME = "SaenggibuQuickNote"


def is_supported() -> bool:
    return sys.platform.startswith("win")


def _command() -> str:
    """자동 실행에 넣을 명령. 배포 exe면 그 exe, 개발이면 pythonw로 콘솔 없이 실행."""
    if getattr(sys, "frozen", False):
        return f'"{sys.executable}"'                # quicknote.exe 자신
    py = Path(sys.executable)
    pyw = py.with_name("pythonw.exe")               # 콘솔창 없이
    exe = str(pyw if pyw.exists() else py)
    script = Path(__file__).resolve().parent.parent / "quicknote.py"
    return f'"{exe}" "{script}"'


def is_enabled() -> bool:
    if not is_supported():
        return False
    import winreg
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, _RUN_KEY) as k:
            winreg.QueryValueEx(k, _APP_NAME)
        return True
    except (FileNotFoundError, OSError):
        return False


def enable() -> bool:
    if not is_supported():
        return False
    import winreg
    try:
        with winreg.CreateKey(winreg.HKEY_CURRENT_USER, _RUN_KEY) as k:
            winreg.SetValueEx(k, _APP_NAME, 0, winreg.REG_SZ, _command())
        return True
    except OSError:
        return False


def disable() -> bool:
    if not is_supported():
        return False
    import winreg
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, _RUN_KEY, 0,
                            winreg.KEY_SET_VALUE) as k:
            winreg.DeleteValue(k, _APP_NAME)
    except (FileNotFoundError, OSError):
        pass
    return True


def set_enabled(on: bool) -> bool:
    return enable() if on else disable()
