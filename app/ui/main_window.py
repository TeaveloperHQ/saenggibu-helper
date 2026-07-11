"""메인 창. 영역 탭 + 최초 실행 시 모델 다운로드/준비 안내."""
from __future__ import annotations

from PySide6.QtCore import Qt, QSize
from PySide6.QtGui import QKeySequence, QShortcut
from PySide6.QtWidgets import (
    QComboBox, QFileDialog, QHBoxLayout, QLabel, QMainWindow, QMessageBox,
    QProgressBar, QPushButton, QTabWidget, QVBoxLayout, QWidget,
)

from .. import config, downloader, settings, theme
from ..engine import LlamaEngine
from ..memory_store import MemoryStore
from ..prompts import AREAS
from .area_tab import AreaTab
from .workers import DownloadWorker


class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle(f"{config.APP_NAME}  v{config.APP_VERSION}")
        self.setWindowIcon(theme.app_icon())       # teaveloper 브랜드 아이콘
        self.resize(820, 720)

        self.store = MemoryStore()
        self.store.load_seed_corpus(config.SEED_CORPUS_PATH)  # 내장 형식 코퍼스 적재
        self.engine = LlamaEngine(self.store)
        self._dl_worker: DownloadWorker | None = None

        self._focus_mode = False
        self._build_ui()
        esc = QShortcut(QKeySequence(Qt.Key.Key_Escape), self)   # 전체화면 해제
        esc.activated.connect(self._exit_focus_mode)
        self._check_model()
        # 첫 실행: 모델이 없으면 자동으로 다운로드를 안내·시작한다(없으면 사용 불가이므로).
        if not downloader.model_exists():
            from PySide6.QtCore import QTimer
            QTimer.singleShot(300, self._prompt_initial_download)

    def _build_ui(self) -> None:
        central = QWidget()
        root = QVBoxLayout(central)
        root.setContentsMargins(0, 0, 0, 0)
        root.setSpacing(0)

        # 브랜드 헤더(teaveloper 그라디언트)
        header = QWidget()
        self.header = header
        header.setFixedHeight(58)
        header.setStyleSheet(f"background: {theme.HEADER_GRADIENT};")
        hl = QHBoxLayout(header)
        hl.setContentsMargins(18, 0, 20, 0)
        logo = QLabel()                          # 브랜드 마크(배경 없이 전경만, 크게)
        logo.setPixmap(theme.svg_pixmap("brandmark", 44))
        logo.setStyleSheet("background: transparent;")
        logo.setFixedWidth(52)
        logo.setAlignment(Qt.AlignmentFlag.AlignCenter)
        hl.addWidget(logo)
        tbox = QVBoxLayout()
        tbox.setSpacing(0)
        t1 = QLabel(config.APP_NAME)
        t1.setStyleSheet("color:#ffffff; font-size:17px; font-weight:bold; background:transparent;")
        t2 = QLabel("teaveloper · 오프라인 생기부 문장 변형")
        t2.setStyleSheet("color: rgba(255,255,255,0.85); font-size:11px; background:transparent;")
        tbox.addWidget(t1)
        tbox.addWidget(t2)
        hl.addLayout(tbox)
        hl.addStretch(1)
        ver = QLabel(f"v{config.APP_VERSION}")
        ver.setStyleSheet("color: rgba(255,255,255,0.8); font-size:12px; background:transparent;")
        hl.addWidget(ver)
        root.addWidget(header)

        # 상단 모델 상태 바(항상 표시 — 두 모드 공통)
        self.banner = QWidget()
        bl = QHBoxLayout(self.banner)
        bl.setContentsMargins(12, 8, 12, 8)
        self.banner_label = QLabel("")
        self.banner_label.setWordWrap(True)
        bl.addWidget(self.banner_label, 1)
        self.dl_btn = QPushButton("모델 내려받기")
        self.dl_btn.clicked.connect(self._start_download)
        bl.addWidget(self.dl_btn)
        self.progress = QProgressBar()
        self.progress.setVisible(False)
        self.progress.setFixedWidth(220)
        bl.addWidget(self.progress)
        root.addWidget(self.banner)

        # ── 최상단 모드 탭: 생성 / 학습 ──────────────────────────────────
        self.mode_tabs = QTabWidget()
        self.mode_tabs.setStyleSheet("QTabBar::tab{padding:8px 20px;font-weight:bold;}")

        # [생성 모드] 영역별 입력→변형→표
        gen = QWidget()
        gl = QVBoxLayout(gen)
        gl.setContentsMargins(6, 6, 6, 6)
        self.tabs = QTabWidget()
        self.area_tabs: list[AreaTab] = []
        for area in AREAS:
            t = AreaTab(area, self.engine, self.store)
            self.area_tabs.append(t)
            self.tabs.addTab(t, area.title)
        gl.addWidget(self.tabs, 1)
        i0 = self.mode_tabs.addTab(gen, "생성 모드")
        # [학습 모드] 불러오기·백업/복원·모델·용어·현황
        i1 = self.mode_tabs.addTab(self._build_learn_tab(), "학습 모드")
        # [과정 안내] 변형 생성 과정 도식
        i2 = self.mode_tabs.addTab(self._build_process_tab(), "과정 안내")
        self.mode_tabs.setIconSize(QSize(18, 18))
        self.mode_tabs.setTabIcon(i0, theme.ui_icon("edit"))
        self.mode_tabs.setTabIcon(i1, theme.ui_icon("book"))
        self.mode_tabs.setTabIcon(i2, theme.ui_icon("steps"))
        self._learn_tab_index = i1
        self.mode_tabs.currentChanged.connect(self._on_mode_changed)

        root.addWidget(self.mode_tabs, 1)
        self.setCentralWidget(central)
        self._populate_models()
        self._update_learn_summary()
        self._refresh_glossary()

    # ---- 전체화면(시트 집중 모드) ----------------------------------------
    def set_focus_mode(self, on: bool) -> None:
        """시트 전체화면: 브랜드 헤더·모델 바를 숨기고 창을 전체화면으로."""
        self._focus_mode = on
        self.header.setVisible(not on)
        self.banner.setVisible(not on)
        self.mode_tabs.tabBar().setVisible(not on)   # 모드 탭바도 숨겨 시트에 집중
        if on:
            self.showFullScreen()
        else:
            self.showNormal()

    def _exit_focus_mode(self) -> None:
        if not self._focus_mode:
            return
        tab = self.tabs.currentWidget()              # 현재 영역 탭
        if isinstance(tab, AreaTab):
            tab._toggle_fullscreen()                 # AreaTab 상태와 동기화하며 해제

    # ---- 학습 모드 패널 ---------------------------------------------------
    def _build_learn_tab(self) -> QWidget:
        from PySide6.QtWidgets import QGroupBox, QListWidget
        w = QWidget()
        v = QVBoxLayout(w)
        v.setContentsMargins(16, 14, 16, 14)
        v.setSpacing(12)

        intro = QLabel(
            "📚 <b>학습 모드</b> — 앱에게 '내 생기부'를 가르치는 곳입니다. "
            "여기서 학습할수록 <b>생성 모드</b>의 변형이 내 문체·표현을 닮아갑니다.")
        intro.setWordWrap(True)
        intro.setStyleSheet("color:#333; background:#e3f2fd; padding:10px; border-radius:6px;")
        v.addWidget(intro)

        # 1) 내 생기부 불러오기
        g1 = QGroupBox("① 내 생기부 불러와 학습")
        l1 = QVBoxLayout(g1)
        self.learn_summary = QLabel("")
        self.learn_summary.setStyleSheet("color:#555;")
        l1.addWidget(self.learn_summary)
        self.import_btn = QPushButton("내 생기부 파일 불러오기(엑셀)")
        self.import_btn.setToolTip("그동안 작성한 생기부(xlsx)를 불러와 학습시킵니다.")
        self.import_btn.clicked.connect(self._open_import)
        l1.addWidget(self.import_btn, 0, Qt.AlignmentFlag.AlignLeft)
        v.addWidget(g1)

        # 2) 등록 용어(고유명사·전문용어)
        g2 = QGroupBox("② 등록 용어(변형 시 그대로 보존·분절 방지)")
        l2 = QVBoxLayout(g2)
        self.glossary_list = QListWidget()
        self.glossary_list.setFixedHeight(110)
        l2.addWidget(self.glossary_list)
        gh = QHBoxLayout()
        self.term_add_edit = None
        from PySide6.QtWidgets import QLineEdit
        self.term_add_edit = QLineEdit()
        self.term_add_edit.setPlaceholderText("예) 아이오아딘 아이오아딘화 칼륨")
        gh.addWidget(self.term_add_edit, 1)
        add_btn = QPushButton("추가")
        add_btn.clicked.connect(self._add_term)
        gh.addWidget(add_btn)
        del_btn = QPushButton("선택 삭제")
        del_btn.clicked.connect(self._remove_term)
        gh.addWidget(del_btn)
        l2.addLayout(gh)
        v.addWidget(g2)

        # 3) 기본 모델 + 학습 백업/복원
        g3 = QGroupBox("③ 모델·학습 데이터 관리")
        l3 = QHBoxLayout(g3)
        l3.addWidget(QLabel("기본 모델"))
        self.model_combo = QComboBox()
        self.model_combo.setMinimumWidth(260)
        self.model_combo.setToolTip("base 모델을 교체해도 교사 학습 데이터는 그대로 유지됩니다.")
        self.model_combo.currentIndexChanged.connect(self._on_model_changed)
        l3.addWidget(self.model_combo)
        l3.addStretch(1)
        self.backup_btn = QPushButton("학습 백업")
        self.backup_btn.setToolTip("이 교사의 학습 데이터를 파일로 저장합니다(다른 PC로 이전 가능).")
        self.backup_btn.clicked.connect(self._on_backup)
        l3.addWidget(self.backup_btn)
        self.restore_btn = QPushButton("학습 복원")
        self.restore_btn.setToolTip("백업한 학습 데이터를 현재 학습에 병합합니다.")
        self.restore_btn.clicked.connect(self._on_restore)
        l3.addWidget(self.restore_btn)
        v.addWidget(g3)

        v.addStretch(1)
        return w

    # ---- 과정 안내(도식) 패널 --------------------------------------------
    def _build_process_tab(self) -> QWidget:
        from PySide6.QtWidgets import QScrollArea
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        w = QWidget()
        v = QVBoxLayout(w)
        v.setContentsMargins(28, 20, 28, 24)
        v.setSpacing(6)

        from PySide6.QtWidgets import QFrame, QHBoxLayout

        def add(widget, top=0):
            if top:
                v.addSpacing(top)
            v.addWidget(widget, 0, Qt.AlignmentFlag.AlignHCenter)

        def bigbox(icon, icon_color, title, desc, bg, border):
            box = QFrame()
            box.setFixedWidth(520)
            box.setStyleSheet(
                f"QFrame{{background:{bg}; border:2px solid {border}; border-radius:12px;}}")
            hl = QHBoxLayout(box)
            hl.setContentsMargins(18, 14, 18, 14)
            hl.setSpacing(14)
            ic = QLabel()
            ic.setPixmap(theme.ui_icon(icon, 28, icon_color).pixmap(28, 28))
            ic.setStyleSheet("background:transparent; border:none;")
            ic.setAlignment(Qt.AlignmentFlag.AlignVCenter)
            hl.addWidget(ic, 0)
            tx = QLabel(f"<b style='font-size:16px'>{title}</b>"
                        + (f"<br><span style='color:#555;font-size:13px'>{desc}</span>"
                           if desc else ""))
            tx.setWordWrap(True)
            tx.setStyleSheet("background:transparent; border:none;")
            hl.addWidget(tx, 1)
            add(box)

        def arrow(txt=""):
            row = QFrame()
            hl = QHBoxLayout(row)
            hl.setContentsMargins(0, 0, 0, 0)
            hl.setSpacing(6)
            hl.addStretch(1)
            ic = QLabel()
            ic.setPixmap(theme.ui_icon("down", 18, "#9aa0a6").pixmap(18, 18))
            hl.addWidget(ic, 0)
            if txt:
                t = QLabel(txt)
                t.setStyleSheet("color:#777; font-size:13px;")
                hl.addWidget(t, 0)
            hl.addStretch(1)
            add(row)

        def header(txt):
            s = QLabel(txt)
            s.setAlignment(Qt.AlignmentFlag.AlignCenter)
            s.setStyleSheet("font-size:17px; font-weight:bold; color:#4f46e5;"
                            " padding:6px 0 4px 0;")
            add(s, top=10)

        # ── 1) 하나를 여러 개로 ──────────────────────────────────────────
        header("① 문장 하나 → 여러 개로")
        bigbox("edit", "#1565c0", "내가 쓴 문장 하나",
               "예) 광합성 실험에서 변인을 통제하고 결과를 발표함", "#e3f2fd", "#90caf9")
        arrow("뜻과 이름은 그대로, 표현만 바꿔서")
        bigbox("copy", "#2e7d32", "같은 뜻, 다른 표현 여러 개",
               "학생마다 조금씩 다르게 — 동료 점검 때 복붙처럼 보이지 않음",
               "#e8f5e9", "#a5d6a7")

        # ── 2) 쓸수록 내 말투 ────────────────────────────────────────────
        header("② 쓸수록 내 말투를 따라감")
        bigbox("save", "#7b1fa2", "마음에 든 문장 저장",
               "'저장' 버튼을 누르면 이 컴퓨터에 내 문장이 쌓임", "#f3e5f5", "#ce93d8")
        arrow("다음에 비슷한 걸 쓸 때")
        bigbox("user", "#7b1fa2", "내 말투를 따라 써 줌",
               "저장이 쌓일수록 점점 더 나를 닮은 문장이 나옴", "#f3e5f5", "#ce93d8")

        note = QFrame()
        note.setFixedWidth(520)
        note.setStyleSheet("QFrame{color:#555; background:#fffde7;"
                           " border:1px solid #fff59d; border-radius:10px;}")
        nh = QHBoxLayout(note)
        nh.setContentsMargins(16, 12, 16, 12)
        nh.setSpacing(12)
        nic = QLabel()
        nic.setPixmap(theme.ui_icon("lock", 22, "#b8860b").pixmap(22, 22))
        nic.setStyleSheet("background:transparent; border:none;")
        nh.addWidget(nic, 0, Qt.AlignmentFlag.AlignTop)
        ntx = QLabel("모든 작업은 <b>인터넷 없이 이 컴퓨터에서만</b> 이뤄집니다 "
                     "— 학생 정보는 밖으로 나가지 않습니다.<br>"
                     "<span style='color:#999;font-size:12px'>"
                     "(모델 내려받기는 처음 한 번, 맞춤법 검사만 인터넷을 씁니다.)</span>")
        ntx.setWordWrap(True)
        ntx.setStyleSheet("background:transparent; border:none;")
        nh.addWidget(ntx, 1)
        add(note, top=18)
        v.addStretch(1)
        scroll.setWidget(w)
        return scroll

    def _update_learn_summary(self) -> None:
        try:
            n = self.store.count()
        except Exception:
            n = 0
        if hasattr(self, "learn_summary"):
            self.learn_summary.setText(
                f"현재 학습된 문장: <b>{n}건</b>. 생성 모드에서 '표 내용 채택·학습'을 누르면 "
                "여기에 쌓이고, 다음 생성부터 반영됩니다." if n
                else "아직 학습된 문장이 없습니다. 아래에서 생기부 파일을 불러오세요.")

    def _refresh_glossary(self) -> None:
        if not hasattr(self, "glossary_list"):
            return
        from .. import glossary
        self.glossary_list.clear()
        for t in sorted(glossary.all_terms()):
            self.glossary_list.addItem(t)

    def _add_term(self) -> None:
        from .. import glossary
        term = self.term_add_edit.text().strip()
        if term and glossary.add(term):
            self.term_add_edit.clear()
            self._refresh_glossary()

    def _remove_term(self) -> None:
        from .. import glossary
        it = self.glossary_list.currentItem()
        if it and glossary.remove(it.text()):
            self._refresh_glossary()

    def _on_mode_changed(self, idx: int) -> None:
        if idx == self._learn_tab_index:   # 학습 모드 → 현황·용어 갱신
            self._update_learn_summary()
            self._refresh_glossary()

    # ---- 모델 선택 / 학습 백업·복원 ---------------------------------------
    def _populate_models(self) -> None:
        self.model_combo.blockSignals(True)
        self.model_combo.clear()
        models = config.list_models()
        if not models:
            self.model_combo.addItem("(설치된 모델 없음)", None)
            self.model_combo.setEnabled(False)
        else:
            self.model_combo.setEnabled(True)
            active = config.active_model_path()
            active_name = active.name if active else None
            for name in models:
                self.model_combo.addItem(name, name)
            if active_name:
                self.model_combo.setCurrentText(active_name)
        self.model_combo.blockSignals(False)

    def _on_model_changed(self, _idx: int) -> None:
        name = self.model_combo.currentData()
        if not name:
            return
        settings.set("active_model", name)
        self.engine.unload()  # 다음 생성 때 바뀐 모델을 로드
        self._check_model()

    def _on_backup(self) -> None:
        path, _ = QFileDialog.getSaveFileName(
            self, "학습 데이터 백업", "saenggibu_learning.sgbak",
            "학습 백업 (*.sgbak);;모든 파일 (*)")
        if not path:
            return
        try:
            n = self.store.export_to(path)
        except Exception as e:  # noqa: BLE001
            QMessageBox.warning(self, "백업 오류", f"백업에 실패했습니다: {e}")
            return
        QMessageBox.information(self, "백업 완료", f"학습 예시 {n}건을 백업했습니다.\n{path}")

    def _on_restore(self) -> None:
        path, _ = QFileDialog.getOpenFileName(
            self, "학습 데이터 복원", "", "학습 백업 (*.sgbak *.sqlite3);;모든 파일 (*)")
        if not path:
            return
        try:
            added = self.store.import_merge(path)
        except Exception as e:  # noqa: BLE001
            QMessageBox.warning(self, "복원 오류", str(e))
            return
        for i in range(self.tabs.count()):
            self.tabs.widget(i)._refresh_learn_count()
        self._update_learn_summary()
        QMessageBox.information(self, "복원 완료", f"학습 예시 {added}건을 병합했습니다.")

    def _open_import(self) -> None:
        from .import_dialog import ImportDialog
        dlg = ImportDialog(self.store, self.engine, self)
        dlg.exec()
        if dlg.imported_count:
            for i in range(self.tabs.count()):
                self.tabs.widget(i)._refresh_learn_count()
            self._update_learn_summary()

    # ---- 모델 상태 --------------------------------------------------------
    def _check_model(self) -> None:
        if downloader.model_exists():
            active = config.active_model_path()
            self.banner.setStyleSheet("background:#e8f5e9;")
            self.banner_label.setText(
                f"모델 준비됨: {active.name if active else ''}. "
                "첫 생성 시 메모리에 로딩됩니다(수십 초). 학습 데이터는 모델과 별도로 영구 보관됩니다."
            )
            self.dl_btn.setVisible(False)
        else:
            self.banner.setStyleSheet("background:#fff3e0;")
            self.banner_label.setText(
                "최초 1회 모델 파일(약 4.7GB)을 내려받아야 합니다. "
                "오른쪽 버튼을 눌러 주세요. (오프라인 사용 가능)"
            )
            self.dl_btn.setVisible(True)

    def _start_download(self) -> None:
        if self._dl_worker and self._dl_worker.isRunning():
            return
        self.dl_btn.setEnabled(False)
        self.progress.setVisible(True)
        self.progress.setValue(0)
        self.banner_label.setText("모델을 내려받는 중입니다…")

        self._dl_worker = DownloadWorker()
        self._dl_worker.progress.connect(self._on_dl_progress)
        self._dl_worker.finished_ok.connect(self._on_dl_done)
        self._dl_worker.failed.connect(self._on_dl_failed)
        self._dl_worker.start()

    def _on_dl_progress(self, downloaded: int, total: int) -> None:
        pct = int(downloaded * 100 / total) if total else 0
        self.progress.setValue(min(pct, 100))
        mb = downloaded / 1024 / 1024
        tot = total / 1024 / 1024
        self.banner_label.setText(f"내려받는 중… {mb:,.0f} / {tot:,.0f} MB")

    def _on_dl_done(self) -> None:
        self.progress.setVisible(False)
        self.dl_btn.setEnabled(True)
        self._check_model()

    def _on_dl_failed(self, msg: str) -> None:
        self.progress.setVisible(False)
        self.dl_btn.setEnabled(True)
        QMessageBox.warning(self, "다운로드 오류", msg)
        self._check_model()

    def closeEvent(self, event) -> None:  # noqa: N802
        if self._dl_worker and self._dl_worker.isRunning():
            self._dl_worker.cancel()
            self._dl_worker.wait(2000)
        for t in self.area_tabs:   # 영역별 학급 표 자동 저장
            t.save()
        self.store.close()
        super().closeEvent(event)
