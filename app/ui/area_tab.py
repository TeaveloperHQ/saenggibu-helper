"""영역(세특/행특/창체/다듬기) 하나에 대응하는 탭 위젯.

출력 공간은 학급별 서브탭 로스터 표(체크박스 + 학번/이름/내용)이며,
'체크된 행 생성'을 누르면 체크된 행 수만큼 서로 다른 변형을 만들어
현재 학급 탭의 체크된 행 '내용'에 순차로 채운다.
"""
from __future__ import annotations

import html as _html

from PySide6.QtCore import Qt, QTimer
from PySide6.QtWidgets import (
    QComboBox, QHBoxLayout, QLabel, QLineEdit, QMenu, QMessageBox,
    QPlainTextEdit, QPushButton, QTextEdit, QVBoxLayout, QWidget,
)

from .. import theme
from ..engine import LlamaEngine
from ..memory_store import MemoryStore
from ..prompts import AreaSpec
from .class_tab import TabbedRoster
from .workers import GenerateWorker

TONE_PRESETS = ["기본", "간결하게", "구체적 사례 중심", "따뜻한 어조", "성장·변화 강조"]
LENGTH_PRESETS = ["보통(3~4문장)", "짧게(1~2문장)", "자세히(5문장 이상)"]


class AreaTab(QWidget):
    def __init__(self, area: AreaSpec, engine: LlamaEngine, store: MemoryStore):
        super().__init__()
        self.area = area
        self.engine = engine
        self.store = store
        self._worker: GenerateWorker | None = None
        self._build_ui()
        self._refresh_learn_count()

    # ---- UI ---------------------------------------------------------------
    def _build_ui(self) -> None:
        root = QVBoxLayout(self)
        root.setContentsMargins(14, 12, 14, 12)
        root.setSpacing(8)

        # 입력·옵션 상단 패널(전체화면 시 숨김 → 시트만 크게)
        self.top_panel = QWidget()
        top = QVBoxLayout(self.top_panel)
        top.setContentsMargins(0, 0, 0, 0)
        top.setSpacing(8)

        # 과목(세특 전용) — 편집 가능한 콤보. 최근 과목을 기억해 바로 고를 수 있게 한다.
        if self.area.subject_field:
            row = QHBoxLayout()
            row.addWidget(QLabel("과목"))
            self.subject_edit = QComboBox()
            self.subject_edit.setEditable(True)
            self.subject_edit.setInsertPolicy(QComboBox.InsertPolicy.NoInsert)
            self.subject_edit.setMinimumWidth(200)
            self.subject_edit.lineEdit().setPlaceholderText("예) 수학, 통합과학 (선택 필수)")
            self._reload_subjects()                     # 저장된 최근 과목 채우기
            self.subject_edit.setCurrentText("")        # 기본 빈칸 → 선택 강제
            row.addWidget(self.subject_edit, 1)
            top.addLayout(row)
        else:
            self.subject_edit = None

        # 옵션 줄: 톤 / 분량 — '키워드로 새로 생성' 모드에서만 의미 있음(변형 모드는 무시)
        opt = QHBoxLayout()
        self.gen_opts = QWidget()                       # 톤·분량 묶음(모드에 따라 표시)
        go = QHBoxLayout(self.gen_opts)
        go.setContentsMargins(0, 0, 0, 0)
        go.addWidget(QLabel("톤"))
        self.tone_combo = QComboBox()
        self.tone_combo.addItems(TONE_PRESETS)
        go.addWidget(self.tone_combo)
        go.addSpacing(12)
        go.addWidget(QLabel("분량"))
        self.length_combo = QComboBox()
        self.length_combo.addItems(LENGTH_PRESETS)
        go.addWidget(self.length_combo)
        opt.addWidget(self.gen_opts)
        opt.addStretch(1)
        self.learn_label = QLabel("")
        self.learn_label.setStyleSheet("color:#2e7d32;")
        opt.addWidget(self.learn_label)
        top.addLayout(opt)

        # 입력
        top.addWidget(QLabel("입력 (키워드·관찰 메모)"))
        self.input_edit = QPlainTextEdit()
        self.input_edit.setPlaceholderText(self.area.input_hint)
        self.input_edit.setFixedHeight(80)
        top.addWidget(self.input_edit)

        # 형태소 점검: 입력하면 형태소로 나눠 색으로 표시(오타·미등록어 미리 확인)
        self.morph_view = QTextEdit()
        self.morph_view.setReadOnly(True)
        self.morph_view.setFixedHeight(46)
        self.morph_view.setStyleSheet("background:#fafafa;border:1px solid #e0e0e0;")
        top.addWidget(self.morph_view)
        # 생기부 규정 위반 경고(기재 불가 항목: 어학시험·논문·수상·대학명 등)
        self.compliance_label = QLabel()
        self.compliance_label.setWordWrap(True)
        self.compliance_label.setStyleSheet("color:#c62828; font-size:11px;")
        self.compliance_label.hide()
        top.addWidget(self.compliance_label)
        lg = QHBoxLayout()
        legend = QLabel(
            "형태소 점검:  <b style='color:#e65100'>■ 사전에 없는 말</b>(오타·전문용어)　"
            "<b style='color:#1565c0'>■ 영문/한자</b>　<span style='color:#999'>■ 정상·조사</span>")
        legend.setStyleSheet("color:#666; font-size:11px;")
        lg.addWidget(legend, 1)
        self.term_btn = QPushButton(" 선택 단어 용어 등록")
        self.term_btn.setIcon(theme.ui_icon("tag", 15))
        self.term_btn.setToolTip(
            "주황색 단어가 오타가 아니라 진짜 용어(고유명사)면,\n"
            "그 단어를 드래그로 선택한 뒤 이 버튼을 누르세요.\n"
            "→ 사전에 등록되어 정상 표시되고, 생성에서 철자가 보존됩니다.")
        self.term_btn.clicked.connect(self._register_term)
        lg.addWidget(self.term_btn)
        top.addLayout(lg)
        self._morph_timer = QTimer(self)
        self._morph_timer.setSingleShot(True)
        self._morph_timer.setInterval(280)
        self._morph_timer.timeout.connect(self._update_morph)
        self.input_edit.textChanged.connect(self._morph_timer.start)
        # 우클릭 → '용어 등록' 메뉴(입력 칸·형태소 칸 모두)
        for w in (self.input_edit, self.morph_view):
            w.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
            w.customContextMenuRequested.connect(self._ctx_menu)

        # 버튼 줄
        btn_row = QHBoxLayout()
        self.mode_combo = QComboBox()
        self.mode_combo.addItems(["내 문장 변형(같은 의미)", "키워드로 새로 생성"])
        self.mode_combo.setToolTip(
            "내 문장 변형: 위에 쓴 문장의 명사·고유명사는 그대로 두고 서술어만 "
            "동의어로 바꿔 같은 의미의 다른 문장을 만듭니다(복붙 방지).\n"
            "키워드로 새로 생성: 모델이 키워드로 새 문장을 만듭니다.")
        self.mode_combo.currentIndexChanged.connect(self._sync_mode)
        btn_row.addWidget(self.mode_combo)
        self.gen_btn = QPushButton("체크된 행 채우기")
        self.gen_btn.setDefault(True)
        self.gen_btn.setToolTip(
            "체크한 행 수만큼 만들어 각 행의 '내용'에 채웁니다.\n"
            "같은 평가를 받는 여러 학생에게 조금씩 다르게 써 줄 때 사용하세요.")
        self.gen_btn.clicked.connect(self._on_generate)
        btn_row.addWidget(self.gen_btn)
        btn_row.addStretch(1)
        self.status_label = QLabel("행을 체크하고 '체크된 행 생성'을 누르세요.")
        self.status_label.setStyleSheet("color:#666;")
        btn_row.addWidget(self.status_label)
        top.addLayout(btn_row)

        root.addWidget(self.top_panel)

        # 출력 = 학급별 서브탭 로스터 표(체크박스 + 학번/이름/내용)
        self.sheet_label = QLabel("학급 표 (학급 탭별 · 체크된 행에 채워짐 · 엑셀 가져오기/내보내기)")
        root.addWidget(self.sheet_label)
        self.output = TabbedRoster(self.area.key)
        self.output.saveRequested.connect(self._on_save)          # 저장 = 표 저장 + 자동 학습
        self.output.fullscreenRequested.connect(self._toggle_fullscreen)
        self.output.rejectRequested.connect(self._on_reject)      # 셀 '버리기' = 부정 학습
        root.addWidget(self.output, 1)
        self._fullscreen = False
        self._sync_mode()                          # 톤·분량 초기 표시 상태 반영

    # ---- 과목 기억 / 모드별 옵션 -----------------------------------------
    def _subject_text(self) -> str:
        if not self.subject_edit:
            return ""
        return self.subject_edit.currentText().strip()

    def _reload_subjects(self) -> None:
        """settings에 저장된 최근 과목으로 콤보를 채운다(선택 항목은 유지)."""
        if not self.subject_edit:
            return
        from .. import settings
        subs = [s for s in settings.get("subjects", []) if s]
        cur = self.subject_edit.currentText()
        self.subject_edit.blockSignals(True)
        self.subject_edit.clear()
        self.subject_edit.addItems(subs)
        self.subject_edit.setCurrentText(cur)
        self.subject_edit.blockSignals(False)

    def _remember_subject(self, subj: str) -> None:
        """방금 쓴 과목을 최근 목록 맨 앞에 저장(중복 제거, 최대 5개)."""
        subj = (subj or "").strip()
        if not self.subject_edit or not subj:
            return
        from .. import settings
        subs = [s for s in settings.get("subjects", []) if s and s != subj]
        subs.insert(0, subj)
        settings.set("subjects", subs[:5])
        self._reload_subjects()

    def _sync_mode(self) -> None:
        """'내 문장 변형'에선 톤·분량을 숨긴다(변형은 원문 뜻·길이 보존이라 무의미)."""
        is_generate = self.mode_combo.currentIndex() == 1   # 키워드로 새로 생성
        self.gen_opts.setVisible(is_generate)

    def save(self) -> None:
        self.output.save()

    # ---- 전체화면(시트만 크게) --------------------------------------------
    def _toggle_fullscreen(self) -> None:
        self._fullscreen = not self._fullscreen
        on = self._fullscreen
        self.top_panel.setVisible(not on)         # 입력 패널 접기
        self.sheet_label.setVisible(not on)
        win = self.window()
        if hasattr(win, "set_focus_mode"):
            win.set_focus_mode(on)

    # ---- 형태소 점검(입력 시) ---------------------------------------------
    _MORPH_COLOR = {"ok": "#555", "susp": "#e65100", "foreign": "#1565c0",
                    "gram": "#bbb"}

    def _update_morph(self) -> None:
        from .. import spellcheck, compliance
        text = self.input_edit.toPlainText().strip()
        if not text:
            self.morph_view.clear()
            self.compliance_label.hide()
            return
        warn = compliance.summary(text)                  # 생기부 규정 위반 검사
        if warn:
            self.compliance_label.setText("⚠ 생기부 규정 확인 — " + warn)
            self.compliance_label.show()
        else:
            self.compliance_label.hide()
        toks = spellcheck.analyze_tokens(text)
        spans = []
        for form, kind in toks:
            color = self._MORPH_COLOR.get(kind, "#555")
            weight = "bold" if kind in ("susp", "foreign") else "normal"
            spans.append(
                f"<span style='color:{color};font-weight:{weight}'>"
                f"{_html.escape(form)}</span>")
        self.morph_view.setHtml(" ".join(spans))

    def _ctx_menu(self, pos) -> None:
        view = self.sender()
        menu = view.createStandardContextMenu()       # 기본 복사/붙여넣기 유지
        menu.addSeparator()
        sel = view.textCursor().selectedText().strip()
        act = menu.addAction("선택 단어 용어 등록")
        act.setEnabled(len(sel) >= 2)
        act.triggered.connect(self._register_term)
        menu.exec(view.mapToGlobal(pos))

    def _register_term(self) -> None:
        from .. import glossary
        sel = (self.morph_view.textCursor().selectedText()
               or self.input_edit.textCursor().selectedText())
        sel = sel.replace(" ", " ").strip(" \t/,.·\n")
        if len(sel) < 2:
            QMessageBox.information(
                self, "용어 등록",
                "등록할 단어를 드래그로 선택한 뒤 눌러 주세요.\n"
                "(형태소 점검 칸이나 입력 칸에서 선택)")
            return
        if glossary.add(sel):
            self._update_morph()                 # 등록 후 색 갱신(주황→회색)
            QMessageBox.information(
                self, "용어 등록",
                f"'{sel}'을(를) 용어로 등록했습니다.\n"
                "이제 정상으로 표시되고, 생성 결과에서 철자가 보존됩니다.")
        else:
            QMessageBox.information(self, "용어 등록",
                                   f"'{sel}'은(는) 이미 등록돼 있거나 너무 짧습니다.")

    # ---- 동작 -------------------------------------------------------------
    def _params(self, n: int) -> dict:
        tone = self.tone_combo.currentText()
        return {
            "subject": self._subject_text(),
            "keywords": self.input_edit.toPlainText(),
            "tone": "" if tone == "기본" else tone,
            "length_hint": self.length_combo.currentText(),
            "n_variations": n,
            "fill_mode": True,
            "paraphrase": self.mode_combo.currentIndex() == 0,   # 내 문장 변형
        }

    def _on_generate(self) -> None:
        # 생성 중이면 이 버튼은 '중지'로 동작한다(토글).
        if self._worker and self._worker.isRunning():
            self._worker.cancel()
            self.gen_btn.setEnabled(False)
            self.status_label.setText("중지하는 중… 지금까지 만든 것만 채웁니다.")
            return
        if not self.input_edit.toPlainText().strip():
            QMessageBox.information(self, "입력 필요", "키워드나 관찰 내용을 입력해 주세요.")
            return
        # 안전장치: '내 문장 변형' 모드에 키워드성 입력이 오면 차단(엉뚱한 결과 방지)
        if self.mode_combo.currentIndex() != 1:
            from ..paraphrase import looks_like_keywords
            if looks_like_keywords(self.input_edit.toPlainText().strip()):
                QMessageBox.information(
                    self, "문장 필요",
                    "'내 문장 변형(같은 의미)'은 완성된 문장을 넣어야 합니다.\n"
                    "키워드로 새 문장을 만들려면 '키워드로 새로 생성' 모드를 선택해 주세요.")
                return
        # 세특은 과목을 반드시 골라야 한다(과목 없이 만들면 엉뚱한 문장이 나올 수 있음)
        if self.area.subject_field and not self._subject_text():
            QMessageBox.information(
                self, "과목 선택 필요",
                "세부능력 및 특기사항은 '과목'을 먼저 선택(또는 입력)해 주세요.\n"
                "과목에 맞는 서술어·표현으로 생성됩니다.")
            self.subject_edit.setFocus()
            return
        self._remember_subject(self._subject_text())    # 쓴 과목 기억
        n = self.output.checked_count()
        if n == 0:
            QMessageBox.information(
                self, "행 선택 필요",
                "내용을 채울 행의 체크박스를 먼저 선택해 주세요.\n"
                "(체크된 행 수만큼 서로 다른 문장이 생성됩니다.)")
            return
        self.gen_btn.setText("⏹ 중지")            # 생성 중엔 '중지' 버튼으로
        self.status_label.setText(f"{n}개 생성 준비 중…")

        self._worker = GenerateWorker(self.engine, self.area, self._params(n))
        self._worker.status.connect(self.status_label.setText)
        self._worker.rows_ready.connect(self._on_rows)
        self._worker.failed.connect(self._on_failed)
        self._worker.start()

    def _reset_gen_btn(self) -> None:
        self.gen_btn.setText("체크된 행 채우기")
        self.gen_btn.setEnabled(True)

    def _on_rows(self, variants: list) -> None:
        filled = self.output.fill_checked(variants)
        self._reset_gen_btn()
        msg = f"{filled}개 행을 채웠습니다. 명사형으로 정리했습니다."
        if filled < self.output.checked_count():
            msg += " (서로 다른 표현이 부족해 일부만 채움 — 키워드를 늘리면 더 다양해집니다.)"
        self.status_label.setText(msg)

    def _on_failed(self, msg: str) -> None:
        self._reset_gen_btn()
        self.status_label.setText(msg)
        QMessageBox.warning(self, "오류", msg)

    def _on_save(self) -> None:
        # 저장 = ① 표 내용 파일 저장  ② 채워진 문장 자동 학습
        self.save()
        items = self.output.contents()
        subject = self._subject_text()
        keywords = self.input_edit.toPlainText().strip()
        for it in items:
            self.store.add_example(
                area=self.area.key, subject=subject, keywords=keywords, output_text=it,
            )
        self._refresh_learn_count()
        area_name = self.area.title
        if subject:
            area_name += f"·{subject}"
        if items:
            msg = (f"저장 완료 — '{area_name}' 영역에 {len(items)}개 문장을 "
                   f"학습했습니다. 다음 생성부터 반영됩니다.")
        else:
            msg = f"표를 저장했습니다. ('{area_name}' 내용 칸이 비어 학습할 문장은 없습니다.)"
        self.status_label.setText(msg)
        if self._fullscreen:                     # 전체화면에선 상태줄이 숨겨져 보이므로 토스트
            QMessageBox.information(self, "저장", msg)

    def _on_reject(self, text: str) -> None:
        """셀에서 '버리기'한 표현을 부정 예시로 저장 → 다음 생성부터 비슷한 문장 회피."""
        subject = self._subject_text()
        self.store.add_rejection(area=self.area.key, subject=subject, output_text=text)
        self.status_label.setText(
            "이 표현을 학습에서 제외했습니다. 다음 생성부터 비슷한 문장을 피합니다.")

    def _refresh_learn_count(self) -> None:
        c = self.store.count(self.area.key)
        self.learn_label.setText(f"학습된 예시 {c}건" if c else "학습된 예시 없음")
