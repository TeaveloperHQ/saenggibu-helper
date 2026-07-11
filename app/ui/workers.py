"""백그라운드 작업 스레드. GUI 스레드를 막지 않도록 생성/다운로드를 분리한다."""
from __future__ import annotations

import re

from PySide6.QtCore import QThread, Signal

from .. import config, downloader
from ..engine import LlamaEngine, ModelNotFoundError

from ..postprocess import (format_variants, has_prompt_echo, strip_prompt_echo,
                           to_nominal_endings)
from ..terms import restore_terms
from ..variation import expand_variants, _norm
from ..prompts import AreaSpec


class GenerateWorker(QThread):
    chunk = Signal(str)        # 스트리밍 토큰 조각
    status = Signal(str)       # 진행 상태 메시지
    finished_ok = Signal(str)  # 전체 결과 텍스트(단일 텍스트 모드)
    rows_ready = Signal(list)  # 변형 결과 리스트(표 채움 모드)
    failed = Signal(str)       # 오류 메시지

    def __init__(self, engine: LlamaEngine, area: AreaSpec, params: dict):
        super().__init__()
        self._engine = engine
        self._area = area
        self._params = params
        self._buf: list[str] = []
        self._cancel = False

    def cancel(self) -> None:
        self._cancel = True

    def run(self) -> None:
        try:
            if self._params.get("fill_mode"):
                # 표 채움 모드: 체크된 행 수(n)만큼
                n = max(1, int(self._params.get("n_variations", 1)))
                if self._params.get("paraphrase"):
                    # 내 문장 변형 — 명사 유지·서술어 변형 + 교사 학습문장으로 자유도 보완
                    from ..paraphrase import llm_paraphrase
                    out = llm_paraphrase(
                        self._engine, self._params.get("keywords", ""), n,
                        progress=self.status.emit,
                        area=self._area, subject=self._params.get("subject", ""),
                        should_cancel=lambda: self._cancel)
                    self.rows_ready.emit(out)
                else:
                    self.rows_ready.emit(self._make_variations(n))
                return
            n = int(self._params.get("n_variations", 1))
            if n <= 1:
                for piece in self._engine.generate_stream(
                    self._area, progress=self.status.emit, **self._params
                ):
                    self._buf.append(piece)
                    self.chunk.emit(piece)
                out = to_nominal_endings(strip_prompt_echo("".join(self._buf)))
            else:
                out = format_variants(self._make_variations(n))
            self.finished_ok.emit(out)
        except ModelNotFoundError:
            self.failed.emit("모델 파일이 없습니다. 먼저 모델을 내려받아 주세요.")
        except Exception as e:  # noqa: BLE001 - GUI 에 그대로 표시
            self.failed.emit(f"생성 중 오류가 발생했습니다: {e}")

    def _make_variations(self, n: int) -> list[str]:
        """변형 모드: 자연 모델로 기본 변형을 여러 번 단일 생성(안 깨짐) 후,
        더 필요하면 그 문장들을 어순·표현 재조합으로 확장한다.
        시간은 기본 문장 수(≤VARIATION_BASE_MAX)만큼으로 고정 — N에 무관.
        교사 채택 예시는 각 단일 생성의 few-shot 으로 자동 반영(재귀 학습)."""
        base_target = min(n, config.VARIATION_BASE_MAX)
        single = {k: (self._params.get(k) or "")
                  for k in ("subject", "keywords", "tone", "length_hint")}
        temps = config.VARIATION_TEMPS
        bases: list[str] = []
        seen: set[str] = set()
        fallback: list[str] = []          # 필터에 걸려도 보관(전부 걸릴 때 대비)
        for i in range(base_target * 4):
            if len(bases) >= base_target or self._cancel:   # 교사 '중지'
                break
            self.status.emit(f"생성 중… ({len(bases) + 1}/{base_target})")
            try:
                raw = "".join(self._engine.generate_stream(
                    self._area, n_variations=1,
                    temperature=temps[i % len(temps)], **single)).strip()
            except Exception:             # 단일 생성 실패는 건너뛰고 계속
                continue
            if not raw:
                continue
            # 프롬프트 지시문 에코 제거 + 명사형 정리 + 고유명사 보존
            v = restore_terms(
                to_nominal_endings(strip_prompt_echo(raw)), single.get("keywords", ""))
            if len(re.findall(r"[가-힣]", v)) < 6:   # 에코 제거 후 너무 짧으면 폐기
                continue
            key = _norm(v)
            if key and key not in seen and v not in fallback:
                fallback.append(v)
            # 명백한 쓰레기(한자 혼입·역할누출·영어과다)만 폐기.
            # 전문 용어(아이오딘 등)를 가짜로 보는 형태소 검사는 생성단계에서 안 씀.
            if not self._looks_clean(raw):
                continue
            if key and key not in seen:
                seen.add(key)
                bases.append(v)
        if not bases:                     # 모두 필터링됨 → 그래도 결과는 낸다
            bases = fallback
        if not bases:                     # 모델이 아무것도 못 만든 진짜 실패만 오류
            raise RuntimeError("문장을 생성하지 못했습니다. 키워드를 바꿔 다시 시도해 주세요.")
        if n <= len(bases):
            return bases[:n]
        self.status.emit("변형 조합 중…")
        return expand_variants(bases, n)

    @staticmethod
    def _looks_clean(text: str) -> bool:
        """깨진 생성(한자 혼입·역할 토큰 누출·영어 과다·한글 부족)을 걸러낸다."""
        if not text:
            return False
        if has_prompt_echo(text):                  # 프롬프트 지시문 따라쓰기(에코)
            return False
        if re.search(r"[一-鿿]", text):           # 한자(중국어) 혼입
            return False
        if re.search(r"(?i)\b(assistant|user|system)\b", text):  # 역할 토큰 누출
            return False
        if len(re.findall(r"[A-Za-z]", text)) > 8:         # 영어 과다 혼입
            return False
        if len(re.findall(r"[가-힣]", text)) < 10:          # 한글이 너무 적음
            return False
        return True


class DownloadWorker(QThread):
    progress = Signal(int, int)  # (downloaded, total)
    finished_ok = Signal()
    failed = Signal(str)

    def __init__(self):
        super().__init__()
        self._cancel = False

    def cancel(self) -> None:
        self._cancel = True

    def run(self) -> None:
        try:
            downloader.download_model(
                progress=lambda d, t: self.progress.emit(d, t),
                should_cancel=lambda: self._cancel,
            )
            self.finished_ok.emit()
        except InterruptedError as e:
            self.failed.emit(str(e))
        except Exception as e:  # noqa: BLE001
            self.failed.emit(f"모델 다운로드 실패: {e}")
