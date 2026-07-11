"""llama.cpp 기반 로컬 추론 엔진.

- GGUF 모델을 지연 로딩(첫 생성 시 1회)한다.
- 영역 system prompt + 교사 본인의 과거 예시(few-shot) + 현재 입력으로
  채팅 메시지를 구성해 스트리밍 생성한다.
"""
from __future__ import annotations

import threading
from collections.abc import Callable, Iterator

from . import config
from .memory_store import Example, MemoryStore
from .prompts import AreaSpec, build_user_prompt


class ModelNotFoundError(RuntimeError):
    pass


class LlamaEngine:
    def __init__(self, store: MemoryStore):
        self._llm = None
        self._lock = threading.Lock()
        self._store = store

    # ---- 로딩 -------------------------------------------------------------
    @property
    def is_loaded(self) -> bool:
        return self._llm is not None

    def unload(self) -> None:
        """로드된 모델을 내려 다음 생성 때 (바뀐) 활성 모델을 다시 로드하게 한다."""
        with self._lock:
            self._llm = None

    def ensure_loaded(self, progress: Callable[[str], None] | None = None) -> None:
        if self._llm is not None:
            return
        with self._lock:
            if self._llm is not None:
                return
            model_path = config.active_model_path()
            if model_path is None or not model_path.exists():
                raise ModelNotFoundError(str(config.MODEL_PATH))
            if progress:
                progress("모델을 메모리에 올리는 중입니다… (최초 1회, 수십 초 소요)")
            from llama_cpp import Llama  # 무거운 import 는 필요 시점까지 미룬다

            self._llm = Llama(
                model_path=str(model_path),
                n_ctx=config.N_CTX,
                n_threads=config.N_THREADS,
                n_batch=config.N_BATCH,
                n_gpu_layers=config.N_GPU_LAYERS,
                use_mmap=config.USE_MMAP,
                use_mlock=config.USE_MLOCK,
                verbose=False,
            )
            if progress:
                progress("모델 준비 완료.")

    # ---- 메시지 구성 ------------------------------------------------------
    def _build_messages(self, area: AreaSpec, *, subject: str, keywords: str,
                        tone: str, length_hint: str, n_variations: int = 1) -> list[dict]:
        messages: list[dict] = [{"role": "system", "content": area.system_prompt()}]

        query = f"{subject} {keywords}"
        # 1순위: 교사 본인이 채택한 예시(개인 문체 학습) — 같은 과목 우선
        teacher: list[Example] = self._store.retrieve(
            area=area.key, query=query, k=config.FEWSHOT_K, subject=subject
        )
        # 2순위: 남는 슬롯을 내장 씨드 코퍼스(문장 '형식'·과목 서술어 학습)로 채움
        need = max(0, config.FEWSHOT_K - len(teacher)) + config.SEED_FEWSHOT_K
        seed = self._store.retrieve_seed(
            area=area.key, query=query, k=need, subject=subject
        )

        fewshot = seed + teacher  # 교사 예시를 더 가깝게(뒤에) 배치
        if not fewshot and area.cold_start[2]:
            cs_subject, cs_keywords, cs_output = area.cold_start
            fewshot = [Example(-1, area.key, cs_subject, cs_keywords, cs_output, 1, 0.0)]

        for ex in fewshot:
            messages.append({
                "role": "user",
                "content": build_user_prompt(
                    area, subject=ex.subject, keywords=ex.keywords, tone="", length_hint=""
                ),
            })
            messages.append({"role": "assistant", "content": ex.output_text})

        if fewshot:
            note = ("아래 대화에는 생기부 문장의 모범 작성 예시가 포함되어 있다. "
                    "예시의 '내용'이 아니라 '문장 형식·어투·연결·종결 방식'을 그대로 따라, "
                    "지금 교사가 준 키워드로 자연스러운 생기부 문장을 작성하라.")
            if teacher:
                note += " 특히 교사가 직접 채택한 예시의 문체를 최우선으로 따르라."
            messages.insert(1, {"role": "system", "content": note})

        messages.append({
            "role": "user",
            "content": build_user_prompt(
                area, subject=subject, keywords=keywords, tone=tone,
                length_hint=length_hint, n_variations=n_variations,
            ),
        })
        return messages

    # ---- 생성 -------------------------------------------------------------
    def generate_stream(self, area: AreaSpec, *, subject: str, keywords: str,
                        tone: str = "", length_hint: str = "", n_variations: int = 1,
                        temperature: float = config.DEFAULT_TEMPERATURE,
                        max_tokens: int = config.DEFAULT_MAX_TOKENS,
                        progress: Callable[[str], None] | None = None) -> Iterator[str]:
        """토큰 조각을 순차적으로 yield 한다."""
        self.ensure_loaded(progress)
        messages = self._build_messages(
            area, subject=subject, keywords=keywords, tone=tone,
            length_hint=length_hint, n_variations=n_variations,
        )
        repeat_penalty, frequency_penalty = 1.1, 0.0
        if n_variations > 1:
            # 변형 모드: 핵심별 표현 후보(짧은 목록)만 생성 → 후처리가 조합
            max_tokens = min(config.N_CTX - 1024, 768)
            temperature = max(temperature, 0.85)
            repeat_penalty, frequency_penalty = 1.25, 0.3
        with self._lock:
            stream = self._llm.create_chat_completion(
                messages=messages,
                temperature=temperature,
                top_p=config.DEFAULT_TOP_P,
                max_tokens=max_tokens,
                repeat_penalty=repeat_penalty,
                frequency_penalty=frequency_penalty,
                stream=True,
            )
            for chunk in stream:
                delta = chunk["choices"][0]["delta"]
                piece = delta.get("content")
                if piece:
                    yield piece

    def generate(self, area: AreaSpec, **kwargs) -> str:
        return "".join(self.generate_stream(area, **kwargs))

    def complete(self, system: str, user: str, *, max_tokens: int = 24,
                 temperature: float = 0.0) -> str:
        """영역 프롬프트와 무관한 단발 완성(예: 표에서 생기부 열 판별)."""
        self.ensure_loaded()
        with self._lock:
            out = self._llm.create_chat_completion(
                messages=[{"role": "system", "content": system},
                          {"role": "user", "content": user}],
                temperature=temperature, max_tokens=max_tokens)
        return out["choices"][0]["message"]["content"].strip()

    def adopted_clauses(self, area: AreaSpec, query: str, k: int = 8,
                        subject: str = "") -> list[str]:
        """교사가 과거 채택한(유사한) 문장들을 짧은 명사형 절로 분해해 반환.

        변형 생성 시 표현 후보로 재투입되어 '재귀 학습' 루프를 만든다.
        """
        from .postprocess import to_nominal_endings
        from .variation import split_sentences

        out: list[str] = []
        for ex in self._store.retrieve(area=area.key, query=query, k=k, subject=subject):
            for c in split_sentences(ex.output_text):
                c = to_nominal_endings(c).strip()
                if c:
                    out.append(c)
        return out
