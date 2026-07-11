"""용어 보존(term anchoring) — 안전판.

작은 모델이 고유명사를 가끔 '한글+영문 혼합'으로 깨뜨린다(예: '아이오딘'→'아이오din').
이런 **손상 토큰만** 골라 키워드 용어로 되돌린다. 정상 한글(활용형 포함)은
절대 건드리지 않으므로 문장을 훼손하지 않는다.
"""
from __future__ import annotations

import difflib
import re

_HANGUL = re.compile(r"[가-힣]")
_LATIN = re.compile(r"[A-Za-z]")


def _candidates(keywords: str) -> list[str]:
    """키워드 구절 + 개별 단어(2자 이상)."""
    out: set[str] = set()
    for part in re.split(r"[/,·]", keywords or ""):
        part = part.strip()
        if len(part) >= 2:
            out.add(part)
        for w in part.split():
            if len(w) >= 2:
                out.add(w)
    return sorted(out, key=len, reverse=True)


def _corrupted(tok: str) -> bool:
    """한글과 영문이 한 토큰에 섞이면 깨진 것으로 본다."""
    return bool(_HANGUL.search(tok) and _LATIN.search(tok))


def restore_terms(text: str, keywords: str, *, threshold: float = 0.5) -> str:
    if not text:
        return text
    cands = set(_candidates(keywords))
    from . import glossary                        # 교사가 등록한 용어(개별 단어)도 보존
    cands |= {w for w in glossary.words() if len(w) >= 2}
    cands = sorted(cands, key=len, reverse=True)
    if not cands:
        return text
    out = []
    for tok in text.split():
        m = re.match(r"^(.*?)([.,)\]'\"]*)$", tok)
        core, punct = m.group(1), m.group(2)
        if not _corrupted(core):
            out.append(tok)
            continue
        best = max(cands, key=lambda c: difflib.SequenceMatcher(None, core, c).ratio())
        r = difflib.SequenceMatcher(None, core, best).ratio()
        out.append(best + punct if r >= threshold else tok)
    return " ".join(out)
