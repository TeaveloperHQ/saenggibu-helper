"""teaveloper 공통 테마 — 형제 앱들과 같은 브랜드(인디고→바이올렛→시안 그라디언트,
zinc 뉴트럴, 인디고 강조, 둥근 모서리, 한글 폰트)를 Qt QSS로 구현한다."""
from __future__ import annotations

# 브랜드 색(teaveloper 아이콘 그라디언트)
INDIGO = "#6366f1"
VIOLET = "#8b5cf6"
CYAN = "#06b6d4"
ACCENT = "#4f46e5"        # 인디고-600(선택·기본 버튼)
ACCENT_HOVER = "#4338ca"

FONT = "'Malgun Gothic', 'Noto Sans CJK KR', 'Segoe UI', 'Apple SD Gothic Neo', sans-serif"

# 헤더 그라디언트(대각선) — 아이콘과 동일 스톱
HEADER_GRADIENT = (
    f"qlineargradient(x1:0, y1:0, x2:1, y2:1, "
    f"stop:0 {INDIGO}, stop:0.5 {VIOLET}, stop:1 {CYAN})")

QSS = f"""
* {{ font-family: {FONT}; font-size: 13px; }}
QMainWindow, QWidget {{ background: #fafafa; color: #18181b; }}
QLabel {{ color: #27272a; background: transparent; }}

/* 최상단 모드 탭 */
QTabWidget::pane {{ border: 1px solid #e4e4e7; border-radius: 10px;
                    background: #ffffff; top: -1px; }}
QTabBar::tab {{ background: #f4f4f5; color: #52525b; padding: 7px 16px;
               margin-right: 4px; border: 1px solid #e4e4e7; border-bottom: none;
               border-top-left-radius: 9px; border-top-right-radius: 9px; }}
QTabBar::tab:selected {{ background: #ffffff; color: {ACCENT}; }}
QTabBar::tab:hover {{ background: #ffffff; }}

/* 버튼 */
QPushButton {{ background: #ffffff; color: #27272a; border: 1px solid #d4d4d8;
              border-radius: 8px; padding: 6px 14px; }}
QPushButton:hover {{ border-color: {INDIGO}; color: {ACCENT}; }}
QPushButton:pressed {{ background: #f4f4f5; }}
QPushButton:default {{ background: {ACCENT}; color: #ffffff; border: 1px solid {ACCENT}; }}
QPushButton:default:hover {{ background: {ACCENT_HOVER}; border-color: {ACCENT_HOVER}; }}
QPushButton:disabled {{ color: #a1a1aa; background: #f4f4f5; border-color: #e4e4e7; }}
QPushButton#saveBtn {{ background: #16a34a; color: #ffffff; border: 1px solid #16a34a; font-weight: bold; }}
QPushButton#saveBtn:hover {{ background: #15803d; border-color: #15803d; }}
QPushButton#fullBtn {{ color: {ACCENT}; }}

/* 입력 */
QLineEdit, QPlainTextEdit, QTextEdit, QComboBox {{ background: #ffffff;
    border: 1px solid #d4d4d8; border-radius: 8px; padding: 5px 8px;
    selection-background-color: #c7d2fe; selection-color: #18181b; }}
QLineEdit:focus, QPlainTextEdit:focus, QTextEdit:focus, QComboBox:focus {{
    border: 1px solid {INDIGO}; }}
QComboBox::drop-down {{ border: none; width: 22px; }}

/* 표 */
QTableWidget, QTableView {{ background: #ffffff; border: 1px solid #e4e4e7;
    border-radius: 8px; gridline-color: #eeeef0;
    selection-background-color: #eef2ff; selection-color: #18181b; }}
QHeaderView::section {{ background: #f4f4f5; color: #52525b; border: none;
    border-right: 1px solid #e4e4e7; border-bottom: 1px solid #e4e4e7;
    padding: 6px 8px; font-weight: bold; }}
QTableCornerButton::section {{ background: #f4f4f5; border: none; }}

/* 그룹박스(학습 모드) */
QGroupBox {{ border: 1px solid #e4e4e7; border-radius: 10px; margin-top: 12px;
    background: #ffffff; font-weight: bold; color: #3f3f46; padding-top: 6px; }}
QGroupBox::title {{ subcontrol-origin: margin; left: 12px; padding: 0 6px; }}

/* 목록 */
QListWidget {{ background: #ffffff; border: 1px solid #e4e4e7; border-radius: 8px; }}
QListWidget::item:selected {{ background: #eef2ff; color: #18181b; }}

/* 진행바 */
QProgressBar {{ border: 1px solid #e4e4e7; border-radius: 6px; background: #f4f4f5;
    text-align: center; color: #3f3f46; }}
QProgressBar::chunk {{ background: {INDIGO}; border-radius: 6px; }}

/* 스크롤바(가늘게) */
QScrollBar:vertical {{ background: transparent; width: 10px; margin: 2px; }}
QScrollBar::handle:vertical {{ background: #d4d4d8; border-radius: 5px; min-height: 26px; }}
QScrollBar::handle:vertical:hover {{ background: #a1a1aa; }}
QScrollBar:horizontal {{ background: transparent; height: 10px; margin: 2px; }}
QScrollBar::handle:horizontal {{ background: #d4d4d8; border-radius: 5px; min-width: 26px; }}
QScrollBar::add-line, QScrollBar::sub-line {{ width: 0; height: 0; }}

QToolTip {{ background: #18181b; color: #fafafa; border: none;
    padding: 5px 8px; border-radius: 6px; }}
"""


def apply(app) -> None:
    """QApplication에 teaveloper 테마 적용."""
    app.setStyleSheet(QSS)


# --- 브랜드 아이콘(죽방 엠블럼) 렌더링 -------------------------------------
from pathlib import Path

_ASSETS = Path(__file__).resolve().parent / "assets"


def svg_pixmap(name: str, px: int):
    """assets/<name>.svg 를 px 크기 투명 QPixmap으로 렌더링."""
    from PySide6.QtSvg import QSvgRenderer
    from PySide6.QtGui import QPixmap, QPainter
    from PySide6.QtCore import Qt
    pm = QPixmap(px, px)
    pm.fill(Qt.GlobalColor.transparent)
    f = _ASSETS / f"{name}.svg"
    if f.exists():
        r = QSvgRenderer(str(f))
        p = QPainter(pm)
        r.render(p)
        p.end()
    return pm


def app_icon():
    """작업표시줄·창 아이콘용 teaveloper 브랜드 QIcon."""
    from PySide6.QtGui import QIcon
    ic = QIcon()
    for s in (16, 32, 48, 64, 128, 256):
        ic.addPixmap(svg_pixmap("teaveloper", s))
    return ic


def emblem_pixmap(px: int = 40):
    """헤더용 죽방 엠블럼(흰색) QPixmap."""
    return svg_pixmap("emblem", px)


def memo_icon():
    """메모 도구용 teaveloper 브랜드 아이콘(그라디언트 + 흰 메모지) QIcon."""
    from PySide6.QtGui import QIcon
    ic = QIcon()
    for s in (16, 24, 32, 48, 64, 128, 256):
        ic.addPixmap(svg_pixmap("memo", s))
    return ic


# ── UI 라인 아이콘(teaveloper 브랜드 인디고) — 이모지 대체용 ──────────────
# 24x24 viewBox, stroke 기반(Feather 스타일). 이모지의 플랫폼 편차·잡색을 없애고
# 브랜드 색으로 통일한다.
_UI_ICONS = {
    "edit":     "<path d='M12 20h9'/><path d='M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5z'/>",
    "book":     "<path d='M4 19.5A2.5 2.5 0 0 1 6.5 17H20'/>"
                "<path d='M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z'/>",
    "steps":    "<circle cx='5' cy='6' r='1.6'/><path d='M10 6h10'/>"
                "<circle cx='5' cy='12' r='1.6'/><path d='M10 12h10'/>"
                "<circle cx='5' cy='18' r='1.6'/><path d='M10 18h10'/>"
                "<path d='M5 7.6v2.8'/><path d='M5 13.6v2.8'/>",
    "copy":     "<rect x='9' y='9' width='11' height='11' rx='2'/>"
                "<path d='M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1'/>",
    "user":     "<path d='M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2'/><circle cx='12' cy='7' r='4'/>",
    "lock":     "<rect x='3' y='11' width='18' height='11' rx='2'/>"
                "<path d='M7 11V7a5 5 0 0 1 10 0v4'/>",
    "down":     "<path d='M12 5v14'/><path d='M19 12l-7 7-7-7'/>",
    "clipboard": "<rect x='8' y='3' width='8' height='4' rx='1'/>"
                 "<path d='M8 5H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2'/>"
                 "<path d='M9 12h6'/><path d='M9 16h4'/>",
    "save":     "<path d='M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4'/>"
                "<path d='M7 10l5 5 5-5'/><path d='M12 15V3'/>",
    "maximize": "<path d='M8 3H5a2 2 0 0 0-2 2v3'/><path d='M21 8V5a2 2 0 0 0-2-2h-3'/>"
                "<path d='M3 16v3a2 2 0 0 0 2 2h3'/><path d='M16 21h3a2 2 0 0 0 2-2v-3'/>",
    "plus":     "<path d='M12 5v14'/><path d='M5 12h14'/>",
    "trash":    "<path d='M3 6h18'/><path d='M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2'/>"
                "<path d='M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6'/>",
    "check":    "<path d='M20 6L9 17l-5-5'/>",
    "import":   "<path d='M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4'/>"
                "<path d='M7 10l5 5 5-5'/><path d='M12 15V3'/>",
    "export":   "<path d='M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4'/>"
                "<path d='M17 8l-5-5-5 5'/><path d='M12 3v12'/>",
    "tag":      "<path d='M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z'/>"
                "<circle cx='7' cy='7' r='1.2'/>",
}


def emoji_icon(emoji: str, px: int = 64):
    """이모지 문자를 QIcon으로 렌더링(트레이 아이콘 등 두 앱 구분용)."""
    from PySide6.QtGui import QPixmap, QPainter, QFont, QIcon
    from PySide6.QtCore import Qt
    pm = QPixmap(px, px)
    pm.fill(Qt.GlobalColor.transparent)
    p = QPainter(pm)
    f = QFont()
    f.setPixelSize(int(px * 0.82))
    p.setFont(f)
    p.drawText(pm.rect(), Qt.AlignmentFlag.AlignCenter, emoji)
    p.end()
    return QIcon(pm)


def ui_icon(name: str, px: int = 18, color: str = ACCENT):
    """브랜드 라인 아이콘 QIcon. color를 흰색으로 주면 컬러 버튼 위에도 쓸 수 있다."""
    from PySide6.QtSvg import QSvgRenderer
    from PySide6.QtGui import QPixmap, QPainter, QIcon
    from PySide6.QtCore import Qt, QByteArray
    body = _UI_ICONS.get(name, "")
    svg = (f"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' "
           f"stroke='{color}' stroke-width='2' stroke-linecap='round' "
           f"stroke-linejoin='round'>{body}</svg>")
    pm = QPixmap(px, px)
    pm.fill(Qt.GlobalColor.transparent)
    QPainter_ = QPainter(pm)
    QSvgRenderer(QByteArray(svg.encode("utf-8"))).render(QPainter_)
    QPainter_.end()
    return QIcon(pm)
