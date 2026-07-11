"""교사가 그동안 작성한 생기부를 불러와 학습에 추가하는 대화상자."""
from __future__ import annotations

from PySide6.QtWidgets import (
    QComboBox, QDialog, QFileDialog, QHBoxLayout, QLabel, QLineEdit,
    QMessageBox, QPlainTextEdit, QPushButton, QVBoxLayout,
)

from .. import importer
from ..memory_store import MemoryStore
from ..prompts import AREAS


class ImportDialog(QDialog):
    def __init__(self, store: MemoryStore, engine=None, parent=None):
        super().__init__(parent)
        self.store = store
        self.engine = engine
        self.imported_count = 0
        self.setWindowTitle("내 생기부 불러오기 (학습)")
        self.resize(620, 520)
        self._build_ui()

    def _build_ui(self) -> None:
        root = QVBoxLayout(self)

        guide = QLabel(
            "그동안 작성하신 생기부를 붙여넣거나 파일(.txt/.csv)로 불러오세요.\n"
            "각 기록이 학습되어 다음 생성·변형부터 선생님의 문체가 반영됩니다.")
        guide.setWordWrap(True)
        root.addWidget(guide)

        # 영역 + 과목
        row = QHBoxLayout()
        row.addWidget(QLabel("영역"))
        self.area_combo = QComboBox()
        for a in AREAS:
            self.area_combo.addItem(a.title, a.key)
        row.addWidget(self.area_combo)
        row.addSpacing(10)
        row.addWidget(QLabel("과목(선택)"))
        self.subject_edit = QLineEdit()
        self.subject_edit.setPlaceholderText("세특이면 과목 예) 수학")
        row.addWidget(self.subject_edit, 1)
        root.addLayout(row)

        # 구분 방식 + 파일
        row2 = QHBoxLayout()
        row2.addWidget(QLabel("기록 구분"))
        self.mode_combo = QComboBox()
        self.mode_combo.addItem("자동", "auto")
        self.mode_combo.addItem("줄 단위(한 줄 = 한 기록)", "line")
        self.mode_combo.addItem("빈 줄 단위(문단 = 한 기록)", "para")
        row2.addWidget(self.mode_combo)
        row2.addStretch(1)
        self.file_btn = QPushButton("파일에서 불러오기…")
        self.file_btn.clicked.connect(self._on_pick_file)
        row2.addWidget(self.file_btn)
        root.addLayout(row2)

        root.addWidget(QLabel("또는 여기에 붙여넣기"))
        self.text_edit = QPlainTextEdit()
        self.text_edit.setPlaceholderText(
            "기록을 붙여넣으세요. 한 줄에 하나 또는 빈 줄로 구분된 문단 형태 모두 가능합니다.")
        root.addWidget(self.text_edit, 1)

        # 미리보기/실행
        self.preview_label = QLabel("")
        self.preview_label.setStyleSheet("color:#555;")
        root.addWidget(self.preview_label)

        btns = QHBoxLayout()
        self.count_btn = QPushButton("기록 개수 확인")
        self.count_btn.clicked.connect(self._on_count)
        btns.addWidget(self.count_btn)
        btns.addStretch(1)
        self.cancel_btn = QPushButton("닫기")
        self.cancel_btn.clicked.connect(self.reject)
        btns.addWidget(self.cancel_btn)
        self.import_btn = QPushButton("학습에 추가")
        self.import_btn.setDefault(True)
        self.import_btn.clicked.connect(self._on_import)
        btns.addWidget(self.import_btn)
        root.addLayout(btns)

    # ---- 동작 -------------------------------------------------------------
    def _records(self) -> list[str]:
        return importer.parse_records(
            self.text_edit.toPlainText(), self.mode_combo.currentData())

    def _llm_picker(self, desc: str, ncol: int):
        """엑셀에서 생기부 본문 열을 모델에게 묻는다(어떤 양식이든 대응)."""
        if not self.engine:
            return None
        sysmsg = ("너는 엑셀 표에서 학생 생활기록부(생기부) 평가 서술이 든 열을 찾는다. "
                  "설명 없이 열 번호 숫자만 답한다.")
        usr = (f"아래 표에서 학생 평가 서술(세부능력 및 특기사항·행동특성·종합의견 등 "
               f"긴 문장)이 담긴 열의 번호만 답하라. 0부터 {ncol - 1} 중 하나의 숫자만.\n\n"
               f"{desc}\n\n답:")
        try:
            return self.engine.complete(sysmsg, usr, max_tokens=8)
        except Exception:
            return None

    def _on_pick_file(self) -> None:
        from pathlib import Path
        from PySide6.QtCore import Qt
        from PySide6.QtGui import QCursor, QGuiApplication
        start = str(Path.home() / "Downloads")
        if not Path(start).exists():
            start = str(Path.home())
        # 리눅스(Wayland/포털)에서 하위폴더 탐색이 막히는 경우가 있어 비네이티브 사용
        paths, _ = QFileDialog.getOpenFileNames(    # 여러 파일 선택 가능
            self, "생기부 파일 선택(여러 개 가능)", start,
            "엑셀/텍스트/CSV (*.xlsx *.xlsm *.txt *.csv *.tsv);;모든 파일 (*)",
            options=QFileDialog.Option.DontUseNativeDialog)
        if not paths:
            return
        picker = self._llm_picker if self.engine else None
        QGuiApplication.setOverrideCursor(QCursor(Qt.CursorShape.WaitCursor))
        try:
            recs = importer.load_records_from_files(
                paths, self.mode_combo.currentData(), picker=picker)
        except Exception as e:  # noqa: BLE001
            QMessageBox.warning(self, "파일 오류", f"파일을 읽지 못했습니다: {e}")
            return
        finally:
            QGuiApplication.restoreOverrideCursor()
        # 파일 내용을 붙여넣기 칸에 채워 검토 가능하게
        self.text_edit.setPlainText("\n\n".join(recs))
        self.preview_label.setText(
            f"{len(paths)}개 파일에서 {len(recs)}개 기록을 불러왔습니다. "
            "검토 후 '학습에 추가'를 누르세요.")

    def _on_count(self) -> None:
        recs = self._records()
        sample = recs[0][:40] + "…" if recs else ""
        self.preview_label.setText(f"기록 {len(recs)}개 감지됨.  예) {sample}")

    def _on_import(self) -> None:
        recs = self._records()
        if not recs:
            QMessageBox.information(self, "내용 없음", "불러올 기록이 없습니다.")
            return
        r = importer.import_records(
            self.store,
            area=self.area_combo.currentData(),
            subject=self.subject_edit.text().strip(),
            records=recs,
        )
        n = r["added"]
        self.imported_count += n
        extra = []
        if r["dup"]:
            extra.append(f"이미 있는 기록 {r['dup']}개 제외")
        if r["blocked"]:
            extra.append(f"생기부 규정 위반(대회·수상 등) {r['blocked']}개 제외")
        msg = f"{n}개 기록을 '{self.area_combo.currentText()}' 영역에 학습했습니다."
        if extra:
            msg += "\n(" + ", ".join(extra) + ")"
        QMessageBox.information(self, "학습 완료", msg)
        self.text_edit.clear()
        self.preview_label.setText(f"누적 {self.imported_count}건 학습됨.")
