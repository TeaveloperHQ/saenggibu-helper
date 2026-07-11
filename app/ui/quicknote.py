"""수업 중 빠른 메모 — 시스템 트레이 상주 + 팝업 입력창.

메인 앱과 별개의 '가벼운' 프로세스다(모델 4.7GB를 안 올림 → 즉시 뜸).
같은 로컬 DB(memos 테이블)를 공유하므로, 여기서 적은 메모를 본 앱이 바로 소비한다.

실행: python quicknote.py   (또는 배포 exe의 보조 엔트리)
트레이 아이콘 클릭 또는 전역 단축키 Ctrl+Alt+M(윈도우) 로 팝업.
"""
from __future__ import annotations

import sys

from PySide6.QtCore import Qt, QAbstractNativeEventFilter, QStringListModel
from PySide6.QtGui import QGuiApplication, QKeySequence, QShortcut
from PySide6.QtWidgets import (QApplication, QComboBox, QCompleter, QHBoxLayout, QLabel,
                               QMenu, QPlainTextEdit, QPushButton, QSystemTrayIcon, QWidget)
from PySide6.QtCore import QTimer, QPropertyAnimation, QEasingCurve, QRect

from PySide6.QtGui import QAction

from .. import autostart, config, roster_data, settings, theme
from ..prompts import AREAS

_HOTKEY_ID = 1
_MOD_ALT, _MOD_CONTROL = 0x0001, 0x0002
_WM_HOTKEY = 0x0312


class QuickNotePopup(QWidget):
    """트레이 아이콘이 '가로로 확장'되는 느낌의 한 줄 입력 바. 화면 하단에 도킹."""

    def __init__(self):
        super().__init__(None, Qt.WindowType.Tool | Qt.WindowType.FramelessWindowHint
                         | Qt.WindowType.WindowStaysOnTopHint)
        self.setFixedHeight(56)
        self.setStyleSheet(
            "QWidget#bar{background:#ffffff; border:1px solid #c7c9d1; border-radius:16px;}"
            "QComboBox,QPlainTextEdit{border:1px solid #d8d8de; border-radius:9px; padding:4px 8px;}"
            "QLabel{color:#71717a; font-size:12px;}")

        outer = QHBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)
        bar = QWidget()
        bar.setObjectName("bar")
        outer.addWidget(bar)
        h = QHBoxLayout(bar)
        h.setContentsMargins(14, 6, 10, 6)
        h.setSpacing(7)

        emo = QLabel()                              # 메모 도구 브랜드 아이콘(메인 💬와 구분)
        emo.setPixmap(theme.svg_pixmap("memo", 24))
        emo.setStyleSheet("background:transparent;")
        h.addWidget(emo)

        # 학급·번호·이름 — 셋 중 하나만 입력/선택해도 나머지를 등록 데이터로 동기화
        self._syncing = False
        self._records: list[dict] = []
        self.class_combo = self._mk_combo("학급", 74, "학급")
        self.num_combo = self._mk_combo("번호", 62, "번호")
        self.name_combo = self._mk_combo("이름", 108, "이름")
        self._key_of = {self.class_combo: "klass", self.num_combo: "num",
                        self.name_combo: "name"}
        for combo in (self.class_combo, self.num_combo, self.name_combo):
            combo.activated.connect(lambda _i, c=combo: self._sync_from(c))
            combo.lineEdit().editingFinished.connect(
                lambda c=combo: self._sync_from(c))
            combo.completer().activated.connect(
                lambda _t, c=combo: self._sync_from(c))
        self.area_combo = QComboBox()
        for a in AREAS:
            self.area_combo.addItem(a.title, a.key)
        self.area_combo.setFixedWidth(150)
        self.area_combo.setToolTip("영역")
        self.area_combo.currentIndexChanged.connect(self._on_area_changed)
        self.subject_combo = QComboBox()
        self.subject_combo.setEditable(True)
        self.subject_combo.setFixedWidth(96)
        self.subject_combo.setToolTip("과목")
        self.subject_combo.lineEdit().setPlaceholderText("과목")
        for w in (self.class_combo, self.num_combo, self.name_combo,
                  self.area_combo, self.subject_combo):
            h.addWidget(w)

        self.text = QPlainTextEdit()                # 메모(넓게 채움) — Enter=줄바꿈, Ctrl+S=저장
        self.text.setPlaceholderText("관찰 메모 입력 · Ctrl+S 저장 · Enter 줄바꿈")
        self.text.setFixedHeight(44)
        self.text.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        h.addWidget(self.text, 1)

        self.status = QLabel("")
        self.status.setStyleSheet("color:#16a34a; font-size:16px;")
        self.status.setFixedWidth(20)
        h.addWidget(self.status)
        save = QPushButton()
        save.setObjectName("saveBtn")
        save.setIcon(theme.ui_icon("save", 16, "#ffffff"))
        save.setFixedWidth(42)
        save.setToolTip("저장 (Ctrl+S)")
        save.clicked.connect(self._save)
        h.addWidget(save)

        close = QPushButton("✕")                    # 닫기(Esc 와 동일)
        close.setObjectName("closeBtn")
        close.setFixedWidth(30)
        close.setToolTip("닫기 (Esc)")
        close.setStyleSheet("QPushButton#closeBtn{border:none; color:#9ca3af;"
                            " font-size:15px; background:transparent;}"
                            "QPushButton#closeBtn:hover{color:#ef4444;}")
        close.clicked.connect(self.hide)
        h.addWidget(close)

        QShortcut(QKeySequence(Qt.Key.Key_Escape), self, self.hide)
        QShortcut(QKeySequence("Ctrl+S"), self, self._save)   # 저장(Enter 와 동일)

    # ---- 콤보 생성/옵션 --------------------------------------------------
    def _mk_combo(self, tip: str, width: int, placeholder: str) -> QComboBox:
        """편집 가능 + 부분일치 자동완성 콤보(직접 입력 허용)."""
        c = QComboBox()
        c.setEditable(True)
        c.setFixedWidth(width)
        c.setInsertPolicy(QComboBox.InsertPolicy.NoInsert)
        c.setToolTip(f"{tip} (등록 데이터 자동완성·동기화 · 직접 입력 가능)")
        c.lineEdit().setPlaceholderText(placeholder)
        comp = QCompleter([], c)
        comp.setCaseSensitivity(Qt.CaseSensitivity.CaseInsensitive)
        comp.setFilterMode(Qt.MatchFlag.MatchContains)
        comp.setCompletionMode(QCompleter.CompletionMode.PopupCompletion)
        c.setCompleter(comp)
        return c

    def _set_options(self, combo: QComboBox, opts: list[str]) -> None:
        """콤보 드롭다운·자동완성 후보를 갱신(현재 입력 텍스트는 보존)."""
        cur = combo.currentText()
        combo.blockSignals(True)
        combo.clear()
        combo.addItems(opts)
        combo.setCurrentText(cur)
        combo.blockSignals(False)
        combo.completer().setModel(QStringListModel(opts, combo.completer()))

    # ---- 데이터 채우기 (상속: 영역 → 학급·번호·이름) ----------------------
    def _reload_all(self) -> None:
        idx = self.area_combo.findData(settings.get("quicknote_last_area", "seteuk"))
        self.area_combo.blockSignals(True)
        if idx >= 0:
            self.area_combo.setCurrentIndex(idx)
        self.area_combo.blockSignals(False)
        self._reload_records()
        self._sync_subject()

    def _reload_records(self) -> None:
        """영역의 등록 레코드를 불러와 세 콤보 초기 후보를 채우고 최근 학급 복원."""
        area = self.area_combo.currentData()
        self._records = roster_data.roster_records(area)
        self.class_combo.setCurrentText("")
        self.num_combo.setCurrentText("")
        self.name_combo.setCurrentText("")
        self._refresh_options(None)
        last = settings.get("quicknote_last_class", "")
        if last and any(r["klass"] == last for r in self._records):
            self.class_combo.setCurrentText(last)
            self._sync_from(self.class_combo)

    def _distinct(self, field: str, recs: list[dict]) -> list[str]:
        vals = {r[field] for r in recs if r[field]}
        return sorted(vals, key=lambda s: (len(s), s)) if field == "num" else sorted(vals)

    def _refresh_options(self, source: QComboBox | None) -> None:
        """각 콤보 후보를 '다른 두 필드가 정한 값'으로 필터해 다시 채운다."""
        vals = {self._key_of[c]: c.currentText().strip()
                for c in (self.class_combo, self.num_combo, self.name_combo)}
        known = {f: (bool(v) and any(r[f] == v for r in self._records))
                 for f, v in vals.items()}
        for combo, field in self._key_of.items():
            others = [g for g in vals if g != field and known[g]]
            cand = [r for r in self._records
                    if all(r[g] == vals[g] for g in others)]
            opts = self._distinct(field, cand)
            self._set_options(combo, opts)
            if combo is source:
                continue
            cur = combo.currentText().strip()
            if len(opts) == 1:                       # 유일 → 자동 채움
                combo.setCurrentText(opts[0])
            elif cur and cur not in opts:            # 현재값이 후보 밖 → 비움(재선택)
                combo.setCurrentText("")

    def _sync_from(self, source: QComboBox) -> None:
        if self._syncing:
            return
        self._syncing = True
        try:
            # 유일값 자동 채움이 연쇄(번호→이름 확정→학급 확정)되도록 두 번 수렴
            self._refresh_options(source)
            self._refresh_options(source)
        finally:
            self._syncing = False

    def _on_area_changed(self) -> None:
        self._reload_records()                       # 영역 바뀌면 재상속
        self._sync_subject()

    def _sync_subject(self) -> None:
        is_seteuk = self.area_combo.currentData() == "seteuk"
        self.subject_combo.setVisible(is_seteuk)   # 세특일 때만 과목칸
        if is_seteuk:
            subs = [s for s in settings.get("subjects", []) if s]
            self.subject_combo.clear()
            self.subject_combo.addItems(subs)
            self.subject_combo.setCurrentText(settings.get("quicknote_last_subject", ""))

    # ---- 표시 / 저장 ------------------------------------------------------
    def popup(self) -> None:
        self._reload_all()
        self.text.clear()
        self.status.setText("")
        geo = QGuiApplication.primaryScreen().availableGeometry()
        w = min(880, geo.width() - 40)
        h = self.height()
        right = geo.right() - 14                    # 트레이(우하단) 쪽 오른쪽 끝
        y = geo.bottom() - h - 12
        final = QRect(right - w, y, w, h)           # 펼쳐진 최종(하단, 오른쪽 정렬)
        start = QRect(right - 64, y, 64, h)         # 작은 아이콘 크기에서 시작
        self.setGeometry(start)
        self.show()
        self.raise_()
        self.activateWindow()
        # 트레이 아이콘이 '길게 펴지듯' 좌측으로 확장
        self._anim = QPropertyAnimation(self, b"geometry", self)
        self._anim.setDuration(190)
        self._anim.setStartValue(start)
        self._anim.setEndValue(final)
        self._anim.setEasingCurve(QEasingCurve.Type.OutCubic)
        self._anim.finished.connect(self.text.setFocus)
        self._anim.start()

    def _save(self) -> None:
        t = self.text.toPlainText().strip()
        if not t:
            self.text.setPlaceholderText("메모를 입력한 뒤 Ctrl+S 하세요")
            return
        area = self.area_combo.currentData()
        subj = self.subject_combo.currentText().strip() if area == "seteuk" else ""
        klass = self.class_combo.currentText().strip()
        num = self.num_combo.currentText().strip()
        name = self.name_combo.currentText().strip()
        if not klass:
            self._flash_status("!", "#dc2626", "이 영역에 등록된 학급 시트가 없습니다")
            return
        if not (num or name):
            self._flash_status("!", "#dc2626", "번호나 이름 중 하나는 입력하세요")
            return
        # 로스터 반영: 기본은 학생 내용에 이어붙이기(추가), 학생 없으면 행 삽입
        result = roster_data.add_memo_to_roster(area=area, klass=klass, num=num,
                                                name=name, text=t)
        if result in ("", "no_class"):               # 저장 실패 → 메모 보존, 경고
            msg = "번호나 이름을 입력하세요" if result == "" else "등록된 학급이 아닙니다"
            self._flash_status("!", "#dc2626", msg)
            return
        settings.set("quicknote_last_class", klass)
        settings.set("quicknote_last_area", area)
        if subj:
            settings.set("quicknote_last_subject", subj)
            subs = [s for s in settings.get("subjects", []) if s and s != subj]
            subs.insert(0, subj)
            settings.set("subjects", subs[:5])
        self.text.clear()
        # ＋: 새 학생 행 삽입 / ✓: 기존 학생 내용에 추가
        if result == "insert":
            self._flash_status("＋", "#16a34a", "새 학생 행 추가됨")
        else:
            self._flash_status("✓", "#16a34a", "기존 학생 내용에 추가됨")
        self.text.setFocus()

    def _flash_status(self, mark: str, color: str, tip: str) -> None:
        self.status.setStyleSheet(f"color:{color}; font-size:16px;")
        self.status.setText(mark)
        self.status.setToolTip(tip)
        QTimer.singleShot(1600, lambda: self.status.setText(""))


class _HotkeyFilter(QAbstractNativeEventFilter):
    def __init__(self, callback):
        super().__init__()
        self._cb = callback

    def nativeEventFilter(self, etype, message):  # noqa: N802
        if sys.platform.startswith("win"):
            try:
                import ctypes
                from ctypes import wintypes
                msg = wintypes.MSG.from_address(int(message))
                if msg.message == _WM_HOTKEY and msg.wParam == _HOTKEY_ID:
                    self._cb()
                    return True, 0
            except Exception:
                pass
        return False, 0


_DEFAULT_HOTKEY = "Ctrl+Alt+M"


def _qt_key_to_vk(key: int):
    """Qt.Key → Windows Virtual-Key(문자·숫자·F키). 지원 안 하면 None."""
    if Qt.Key.Key_A <= key <= Qt.Key.Key_Z:      # 0x41-0x5A = VK_A..Z
        return int(key)
    if Qt.Key.Key_0 <= key <= Qt.Key.Key_9:      # 0x30-0x39 = VK_0..9
        return int(key)
    if Qt.Key.Key_F1 <= key <= Qt.Key.Key_F12:   # VK_F1 = 0x70
        return 0x70 + (int(key) - int(Qt.Key.Key_F1))
    return None


def _parse_hotkey(seq_str: str):
    """'Ctrl+Alt+M' → (win_modifiers, vk). 조합키·본키가 다 있어야 유효."""
    seq = QKeySequence(seq_str)
    if seq.isEmpty():
        return None
    kc = seq[0]                                   # 첫 조합(QKeyCombination)
    mods = kc.keyboardModifiers()
    win_mods = 0
    if mods & Qt.KeyboardModifier.ControlModifier:
        win_mods |= _MOD_CONTROL
    if mods & Qt.KeyboardModifier.AltModifier:
        win_mods |= _MOD_ALT
    if mods & Qt.KeyboardModifier.ShiftModifier:
        win_mods |= 0x0004
    if mods & Qt.KeyboardModifier.MetaModifier:
        win_mods |= 0x0008
    vk = _qt_key_to_vk(int(kc.key()))
    if not win_mods or vk is None:
        return None
    return win_mods, vk


class QuickNoteApp:
    def __init__(self):
        self.app = QApplication.instance() or QApplication(sys.argv)
        self.app.setApplicationName("생기부 수업 메모")
        self.app.setDesktopFileName("saenggibu-quicknote")   # 메인 앱과 다른 app_id
        self.app.setQuitOnLastWindowClosed(False)   # 팝업 닫아도 트레이는 유지
        theme.apply(self.app)
        self.popup = QuickNotePopup()

        # 트레이 아이콘 = 📝 (메인 앱 💬와 구분)
        self.tray = QSystemTrayIcon(theme.memo_icon(), self.app)
        menu = QMenu()
        menu.addAction("메모 작성", self.popup.popup)
        self.hotkey_action = menu.addAction("단축키 변경…", self._change_hotkey)
        menu.addSeparator()
        self.autostart_action = QAction("Windows 시작 시 자동 실행", menu, checkable=True)
        if autostart.is_supported():
            # 최초 실행 시 기본으로 자동 실행 등록('작업표시줄에 항상'). 이후엔
            # 사용자가 끈 선택을 존중하려고 1회만 강제한다.
            if not settings.get("quicknote_autostart_setup", False):
                autostart.enable()
                settings.set("quicknote_autostart_setup", True)
            self.autostart_action.setChecked(autostart.is_enabled())
            self.autostart_action.toggled.connect(autostart.set_enabled)
        else:
            self.autostart_action.setText("Windows 시작 시 자동 실행 (Windows 전용)")
            self.autostart_action.setEnabled(False)
        menu.addAction(self.autostart_action)
        menu.addSeparator()
        menu.addAction("종료", self.app.quit)
        self.tray.setContextMenu(menu)
        self.tray.activated.connect(self._on_tray)
        self._hotkey_filter = None
        self._register_hotkey()
        self.tray.show()

    def _on_tray(self, reason) -> None:
        if reason in (QSystemTrayIcon.ActivationReason.Trigger,
                      QSystemTrayIcon.ActivationReason.DoubleClick):
            self.popup.popup()

    def _hotkey(self) -> str:
        return settings.get("quicknote_hotkey", _DEFAULT_HOTKEY) or _DEFAULT_HOTKEY

    def _register_hotkey(self) -> None:
        """설정된 전역 단축키를 등록(Windows). 툴팁·메뉴에 현재 단축키 표시."""
        hk = self._hotkey()
        self.tray.setToolTip(f"생기부 수업 메모 — 클릭 또는 {hk}")
        self.hotkey_action.setText(f"단축키 변경…  (현재: {hk})")
        if not sys.platform.startswith("win"):
            return
        try:
            import ctypes
            if self._hotkey_filter is None:
                self._hotkey_filter = _HotkeyFilter(self.popup.popup)
                self.app.installNativeEventFilter(self._hotkey_filter)
            ctypes.windll.user32.UnregisterHotKey(None, _HOTKEY_ID)
            parsed = _parse_hotkey(hk)
            if parsed:
                mods, vk = parsed
                ctypes.windll.user32.RegisterHotKey(None, _HOTKEY_ID, mods, vk)
        except Exception:
            pass

    def _change_hotkey(self) -> None:
        from PySide6.QtWidgets import (QDialog, QVBoxLayout, QLabel, QKeySequenceEdit,
                                       QDialogButtonBox)
        dlg = QDialog()
        dlg.setWindowTitle("메모 단축키 설정")
        dlg.setWindowIcon(theme.memo_icon())
        v = QVBoxLayout(dlg)
        v.addWidget(QLabel("메모 입력창을 여는 전역 단축키를 눌러 지정하세요.\n"
                           "(조합키 Ctrl/Alt/Shift + 문자·숫자·F키)"))
        edit = QKeySequenceEdit(QKeySequence(self._hotkey()))
        v.addWidget(edit)
        bb = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok
                              | QDialogButtonBox.StandardButton.Cancel)
        bb.accepted.connect(dlg.accept)
        bb.rejected.connect(dlg.reject)
        v.addWidget(bb)
        if dlg.exec() == QDialog.DialogCode.Accepted:
            s = edit.keySequence().toString()
            if s and _parse_hotkey(s):
                settings.set("quicknote_hotkey", s)
                self._register_hotkey()

    def run(self) -> int:
        if not QSystemTrayIcon.isSystemTrayAvailable():
            self.popup.popup()               # 트레이 없으면 팝업만이라도
        return self.app.exec()


def main() -> int:
    return QuickNoteApp().run()


if __name__ == "__main__":
    raise SystemExit(main())
