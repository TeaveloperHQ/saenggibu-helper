"""앱 진입점. `python -m app.main` 또는 PyInstaller 엔트리로 실행한다."""
from __future__ import annotations

import sys
from pathlib import Path

from PySide6.QtWidgets import QApplication

from . import config, theme
from .ui.main_window import MainWindow

_DESKTOP_ID = "saenggibu-helper"


def _ensure_linux_desktop() -> None:
    """리눅스(특히 Wayland/KDE)에서 좌상단·작업표시줄 아이콘이 뜨게 한다.
    Wayland는 setWindowIcon을 무시하고 app_id에 매칭되는 .desktop의 Icon을 쓰므로,
    아이콘 PNG와 .desktop을 사용자 폴더에 설치한다(idempotent)."""
    if sys.platform != "linux":
        return
    try:
        import subprocess
        share = Path.home() / ".local/share"
        icon_root = share / "icons"
        hicolor = icon_root / "hicolor"
        app_dir = share / "applications"
        app_dir.mkdir(parents=True, exist_ok=True)
        icon_root.mkdir(parents=True, exist_ok=True)
        # 최신 아이콘(teaveloper.svg)을 표준 icon-theme(hicolor) 경로에 여러 크기로 설치
        # → Icon=saenggibu-helper 가 아이콘 테마에서 확실히 해석되게 한다.
        for s in (16, 32, 48, 64, 128, 256):
            d = hicolor / f"{s}x{s}" / "apps"
            d.mkdir(parents=True, exist_ok=True)
            theme.svg_pixmap("teaveloper", s).save(str(d / f"{_DESKTOP_ID}.png"))
        # SVG 자체도 scalable로 설치(선명)
        svg_dir = hicolor / "scalable" / "apps"
        svg_dir.mkdir(parents=True, exist_ok=True)
        try:
            (svg_dir / f"{_DESKTOP_ID}.svg").write_bytes(
                (theme._ASSETS / "teaveloper.svg").read_bytes())
        except OSError:
            pass
        # 상위 폴더에도 한 장(구형 폴백)
        icon_abs = icon_root / f"{_DESKTOP_ID}.png"
        theme.svg_pixmap("teaveloper", 256).save(str(icon_abs))
        run_py = Path(__file__).resolve().parent.parent / "run.py"
        # Icon= 은 테마 이름이 아니라 '절대경로'로 지정 — 테마 인덱싱/캐시 문제를 우회한다.
        (app_dir / f"{_DESKTOP_ID}.desktop").write_text(
            "[Desktop Entry]\n"
            "Type=Application\n"
            f"Name={config.APP_NAME}\n"
            "Comment=오프라인 생기부 문장 변형 도우미\n"
            f"Exec={sys.executable} {run_py}\n"
            f"Icon={icon_abs}\n"
            "Terminal=false\n"
            "Categories=Education;\n"
            f"StartupWMClass={_DESKTOP_ID}\n",
            encoding="utf-8")
        # 아이콘·메뉴 캐시 갱신(있는 도구만; 없으면 조용히 넘어감)
        for cmd in (["gtk-update-icon-cache", "-f", "-t", str(hicolor)],
                    ["kbuildsycoca6"], ["kbuildsycoca5"]):
            try:
                subprocess.run(cmd, check=False, capture_output=True, timeout=20)
            except (OSError, subprocess.SubprocessError):
                pass
    except OSError:
        pass


def main() -> int:
    app = QApplication(sys.argv)
    app.setApplicationName(config.APP_NAME)
    # Wayland app_id → 같은 이름의 .desktop 아이콘으로 창/작업표시줄 아이콘 매핑
    app.setDesktopFileName(_DESKTOP_ID)
    theme.apply(app)                       # teaveloper 공통 테마
    app.setWindowIcon(theme.app_icon())    # (X11·Windows용) 브랜드 아이콘
    _ensure_linux_desktop()                # (리눅스) 좌상단 아이콘 표시용 설치
    win = MainWindow()
    win.show()
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
