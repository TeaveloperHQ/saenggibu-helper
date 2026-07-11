"""사용자 용어집 — 교사가 등록한 고유명사·전문용어(다단어 묶음 지원).

등록 단위는 '구'다(예: '아이오딘화 칼륨'을 한 묶음으로). 저장된 용어는:
  ① 구를 이루는 각 단어를 kiwi 사전에 등록 → 형태소 점검에서 정상(회색)
  ② 형태소 점검에서 연속 토큰을 등록 구로 '묶어서' 한 단위로 표시
  ③ 생성 결과에서 철자 보존(term anchoring)
  ④ 파일에 영구 저장
"""
from __future__ import annotations

import json

from . import config

_PATH = config.DATA_DIR / "user_terms.json"
_terms: set[str] | None = None        # 구(공백 포함 가능) 집합


def _save(t: set[str]) -> None:
    try:
        _PATH.write_text(json.dumps(sorted(t), ensure_ascii=False), encoding="utf-8")
    except OSError:
        pass


def all_terms() -> set[str]:
    """등록된 용어(구) 집합."""
    global _terms
    if _terms is None:
        try:
            _terms = {" ".join(s.split()) for s in
                      json.loads(_PATH.read_text(encoding="utf-8")) if s.strip()}
        except (OSError, json.JSONDecodeError):
            _terms = set()
    return _terms


def words() -> set[str]:
    """모든 구를 이루는 개별 단어(2자 이상) — kiwi 등록·정상표시용."""
    out = set()
    for t in all_terms():
        for w in t.split():
            if len(w) >= 2:
                out.add(w)
    return out


def phrases() -> list[str]:
    """다단어 구만(단어 수 많은 것 우선) — 형태소 묶음 표시용."""
    return sorted((t for t in all_terms() if " " in t),
                  key=lambda s: len(s.split()), reverse=True)


def add(term: str) -> bool:
    """용어(구) 등록. 새로 추가되면 True."""
    term = " ".join((term or "").split())          # 공백 정규화
    if len(term) < 2:
        return False
    t = all_terms()
    if term in t:
        return False
    t.add(term)
    _save(t)
    for w in term.split():                          # 각 단어를 kiwi에 등록
        if len(w) >= 2:
            _register_kiwi(w)
    return True


def remove(term: str) -> bool:
    t = all_terms()
    term = " ".join((term or "").split())
    if term not in t:
        return False
    t.discard(term)
    _save(t)
    return True


def _register_kiwi(word: str) -> None:
    from .spellcheck import _get_kiwi
    k = _get_kiwi()
    if k is not None:
        try:
            k.add_user_word(word, "NNP", 0.0)
        except Exception:
            pass


def register_all_with_kiwi() -> None:
    for w in words():
        _register_kiwi(w)
