"""변형 대량 생성기.

원리: 생기부 문장은 '독립적인 짧은 명사형 절'의 나열이라, 한국어 어순 자유성에
따라 절의 순서를 바꿔도 문법·의미가 보존된다. 따라서 모델이 만든 소수의 양질
기본 문장(base)을 (1) 절 순서 재배열 + (2) 동의어 치환으로 결정론적으로 조합해
수십~수백 개의 서로 다른 문장을 빠르게 만든다(추가 추론 없이).
"""
from __future__ import annotations

import itertools
import math
import random
import re

# 드롭인 동의어 그룹 — 같은 자리에 바꿔 넣어도 어색하지 않은 표현만 보수적으로 둔다.
SYNONYMS: list[list[str]] = [
    ["보임", "드러냄", "나타냄"],
    ["노력함", "힘씀"],
    ["적극적으로", "능동적으로", "자발적으로"],
    ["성실히", "성실하게", "꾸준히"],
    ["꼼꼼히", "세심하게"],
    ["끝까지", "빠짐없이"],
    ["태도", "자세"],
    ["능력", "역량"],
    ["친구들", "또래들"],
    ["수행함", "해냄", "완수함"],
    ["참여함", "임함"],
]

_PERM_CAP = 12   # base 당 절 순서 순열 상한
_SYN_CAP = 8     # 한 문장당 동의어 치환 변형 상한


def split_sentences(text: str) -> list[str]:
    """'. ' 단위로 문장(절)을 나눠 각 조각이 마침표로 끝나도록 반환."""
    parts = re.findall(r"[^.]+\.", text.replace("\n", " "))
    out = [p.strip() for p in parts if p.strip()]
    if not out and text.strip():
        out = [text.strip()]
    return out


def _norm(s: str) -> str:
    return re.sub(r"\s+", "", s)


def synonym_variants(text: str, cap: int = _SYN_CAP) -> list[str]:
    """동의어 그룹을 적용한 변형들(원문 포함). cap 으로 폭증 방지."""
    results = [text]
    for group in SYNONYMS:
        present = next((w for w in group if w in text), None)
        if present is None:
            continue
        expanded: list[str] = []
        for r in results:
            for w in group:
                expanded.append(r.replace(present, w))
        # 중복 제거 + 상한
        results = list(dict.fromkeys(expanded))[:cap]
    return results[:cap]


def _variants_for_base(base: str):
    """한 base 에서 (절 순서 순열 × 동의어 치환) 변형을 지연 생성."""
    clauses = split_sentences(base)
    if len(clauses) <= 1:
        # 절이 하나면 동의어 치환만
        yield from synonym_variants(base)
        return
    perms = itertools.islice(itertools.permutations(clauses), _PERM_CAP)
    for perm in perms:
        text = " ".join(perm)
        yield from synonym_variants(text)


def enrich_groups(groups: list[list[str]], adopted: list[str],
                  max_per_group: int = 6, min_shared: int = 2) -> list[list[str]]:
    """교사가 채택한 문장(절)을 가장 유사한 핵심 그룹의 표현 후보로 추가한다(재귀 학습).

    각 채택 절을 토큰 겹침으로 그룹에 배정 → 다음 변형 생성에 사람이 승인한 표현이 재투입됨.
    """
    from .memory_store import tokenize  # 같은 토크나이저 재사용

    if not adopted or not groups:
        return groups
    group_tokens = [set(t for ph in g for t in tokenize(ph)) for g in groups]
    seen = [set(_norm(p) for p in g) for g in groups]
    for clause in adopted:
        ct = set(tokenize(clause))
        if not ct:
            continue
        best, best_score = -1, 0
        for i, gt in enumerate(group_tokens):
            sc = len(ct & gt)
            if sc > best_score:
                best, best_score = i, sc
        if best >= 0 and best_score >= min_shared and len(groups[best]) < max_per_group:
            key = _norm(clause)
            if key not in seen[best]:
                groups[best].append(_ensure_period(clause))
                seen[best].add(key)
                group_tokens[best] |= ct
    return groups


def _ensure_period(s: str) -> str:
    s = s.strip()
    return s if s.endswith((".", "!", "?")) else s + "."


def combine_variants(groups: list[list[str]], n: int) -> list[str]:
    """핵심 내용별 표현 후보(groups)를 어순 재배열 + 표현 선택으로 조합해 변형 n 개 생성.

    groups = [[핵심1 표현a, 표현b, …], [핵심2 표현a, …], …]
    한국어 어순 자유성에 따라 핵심 문장들의 순서를 섞고, 각 핵심의 표현을 골라 조합한다.
    """
    groups = [[_ensure_period(p) for p in g if p.strip()] for g in groups]
    groups = [g for g in groups if g]
    k = len(groups)
    if k == 0:
        return []
    if k == 1:
        # 핵심이 하나뿐이면 표현 후보 + 동의어로만 변형
        out, seen = [], set()
        pool = list(dict.fromkeys(groups[0]))
        for base in pool:
            for v in synonym_variants(base):
                key = _norm(v)
                if key not in seen:
                    seen.add(key)
                    out.append(v)
                    if len(out) >= n:
                        return out
        return out

    # 전체 조합 공간 크기 = k! × ∏(표현 수)
    space = math.factorial(k)
    for g in groups:
        space *= len(g)
    target = min(n, space)

    rng = random.Random(42)  # 결정론적
    idxs = list(range(k))
    out, seen = [], set()
    attempts = 0
    max_attempts = target * 80 + 500
    while len(out) < target and attempts < max_attempts:
        attempts += 1
        perm = idxs[:]
        rng.shuffle(perm)
        text = " ".join(rng.choice(groups[i]) for i in perm)
        key = _norm(text)
        if key not in seen:
            seen.add(key)
            out.append(text)

    # 부족하면 동의어 치환으로 보충
    if len(out) < n:
        for t in list(out):
            for v in synonym_variants(t):
                key = _norm(v)
                if key not in seen:
                    seen.add(key)
                    out.append(v)
                    if len(out) >= n:
                        return out
    return out[:n]


def expand_variants(bases: list[str], n: int) -> list[str]:
    """기본 문장들을 조합해 서로 다른 변형 n 개를 만든다(라운드로빈으로 다양성 확보)."""
    bases = [b.strip() for b in bases if b.strip()]
    if not bases:
        return []
    if n <= len(bases):
        return bases[:n]

    gens = [_variants_for_base(b) for b in bases]
    seen: set[str] = set()
    out: list[str] = []
    while len(out) < n and gens:
        progressed = False
        for g in list(gens):
            try:
                v = next(g)
            except StopIteration:
                gens.remove(g)
                continue
            progressed = True
            key = _norm(v)
            if key not in seen:
                seen.add(key)
                out.append(v)
                if len(out) >= n:
                    break
        if not progressed:
            break
    return out[:n]
