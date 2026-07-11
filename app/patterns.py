"""교사 기술 패턴 분석 → 변형의 '구조 축'으로 분해.

생기부 문장의 구조는 코퍼스 실측상 4개 축으로 결정된다:
  구성(comp)   : 단문 / 2문장            — 활동·평가를 한 문장에 융합 vs 분리
  종결(end)    : 평가형 / 관찰형 / 성장형  — ~보임 / ~함 / ~키움
  순서(order)  : 활동먼저 / 맥락먼저        — 바로 활동 vs '~단원/활동에서' 맥락 선행
  연결(conn)   : 하며 / 하여 / 통해 / 고    — 절 연결 어미

기존 변형은 '부사만 바꾸는' 어휘 축이라 얕다. 이 4축을 서로 다르게 조합해
구조적으로 다른 변형을 만들고, 교사 빈도에 맞춰 가중한다(복붙 방지 + 교사 문체).
"""
from __future__ import annotations

import re
from collections import Counter

# --- 종결 계열 판별용 어휘(마지막 어절 기준) ---
_END_EVAL = re.compile(r"(보임|드러냄|돋보임|지님|발휘함|뛰어남|우수함|보여줌|엿보임|인상적임|강함)$")
_END_GROW = re.compile(r"(키움|기름|넓힘|성장함|향상됨|높임|길러냄|확장함|더함|쌓음)$")
_END_OBS = re.compile(r"(설명함|발표함|수행함|기여함|경험함|체득함|참여함|완수함|제작함|"
                      r"작성함|정리함|조사함|탐구함|실천함|전달함|공유함|진행함|이끎|나눔|함)$")
_CONN_PAT = [("을 통해", "통해"), ("를 통해", "통해"), ("는 등", "는등"),
             ("으로써", "으로써"), ("하여", "하여"), ("하며", "하며"), ("으며", "하며")]
_CTX_OPEN = re.compile(r"(에서|단원|활동|시간|과정에서)\s")
_MOT_OPEN = re.compile(r"^(관심|진로|평소|스스로|어릴|호기심)")

# 코퍼스(1849건) 실측 전역 프로파일 — 교사 데이터가 부족할 때 기본값.
DEFAULT_PROFILE = {
    "comp": {"단문": 0.52, "2문장": 0.48},
    "end":  {"평가형": 0.42, "관찰형": 0.31, "성장형": 0.27},
    "order": {"활동먼저": 0.80, "맥락먼저": 0.20},
    "conn": {"하며": 0.50, "하여": 0.16, "통해": 0.16, "는등": 0.08, "으로써": 0.06, "고": 0.04},
}
_AXES = ("comp", "end", "order", "conn")


def _split_sents(text: str) -> list[str]:
    return [s.strip() for s in re.split(r"(?<=[.])\s+", text.strip()) if s.strip()]


def _ending_type(last: str) -> str:
    if _END_EVAL.search(last):
        return "평가형"
    if _END_GROW.search(last):
        return "성장형"
    if _END_OBS.search(last):
        return "관찰형"
    return "관찰형"                     # 기본은 관찰형(직접 서술)로 귀속


def classify(text: str) -> dict:
    """문장 하나를 4축 라벨로 분해."""
    text = (text or "").strip()
    sents = _split_sents(text)
    comp = "단문" if len(sents) <= 1 else "2문장"
    last = text.rstrip(".").split()[-1] if text.split() else ""
    end = _ending_type(last)
    first = sents[0] if sents else text
    if _CTX_OPEN.search(first[:22]):
        order = "맥락먼저"
    else:
        order = "활동먼저"
    conn = next((nm for pat, nm in _CONN_PAT if pat in text), "고")
    return {"comp": comp, "end": end, "order": order, "conn": conn}


def analyze(texts) -> dict:
    """예시 묶음에서 축별 빈도 프로파일 산출(정규화). 표본이 적으면 기본값과 섞는다."""
    counters = {a: Counter() for a in _AXES}
    n = 0
    for t in texts:
        t = (t or "").strip()
        if not t:
            continue
        n += 1
        lab = classify(t)
        for a in _AXES:
            counters[a][lab[a]] += 1
    if n < 8:                                    # 표본 부족 → 전역 기본값
        return {a: dict(DEFAULT_PROFILE[a]) for a in _AXES}
    prof = {}
    for a in _AXES:
        tot = sum(counters[a].values()) or 1
        # 표본 프로파일과 기본값을 표본 수에 따라 가중 혼합(작을수록 기본값 쪽)
        w = min(1.0, n / 60.0)
        keys = set(counters[a]) | set(DEFAULT_PROFILE[a])
        prof[a] = {k: w * (counters[a].get(k, 0) / tot)
                   + (1 - w) * DEFAULT_PROFILE[a].get(k, 0.0) for k in keys}
    return prof


def _weighted_pick(dist: dict, rng, exclude=()):
    items = [(k, v) for k, v in dist.items() if k not in exclude and v > 0]
    if not items:
        items = [(k, v) for k, v in dist.items() if v > 0] or [(next(iter(dist)), 1.0)]
    tot = sum(v for _, v in items)
    r = rng.random() * tot
    acc = 0.0
    for k, v in items:
        acc += v
        if r <= acc:
            return k
    return items[-1][0]


def plan(n: int, profile: dict, rng) -> list[dict]:
    """교사 빈도에 맞춰 n개의 '서로 다른' 구조 타깃을 뽑는다.
    (comp,end) 조합이 겹치지 않도록 우선 분산 → 그 뒤 order/conn을 변주."""
    profile = profile or DEFAULT_PROFILE
    used_pairs = Counter()
    out = []
    for i in range(max(1, n)):
        # (comp,end)는 이미 많이 쓴 조합을 피해 다양성 우선
        for _ in range(6):
            comp = _weighted_pick(profile.get("comp", DEFAULT_PROFILE["comp"]), rng)
            end = _weighted_pick(profile.get("end", DEFAULT_PROFILE["end"]), rng)
            if used_pairs[(comp, end)] <= min(used_pairs.values() or [0]):
                break
        used_pairs[(comp, end)] += 1
        order = _weighted_pick(profile.get("order", DEFAULT_PROFILE["order"]), rng)
        conn = _weighted_pick(profile.get("conn", DEFAULT_PROFILE["conn"]), rng)
        out.append({"comp": comp, "end": end, "order": order, "conn": conn})
    return out


_END_HINT = {
    "평가형": "끝을 역량 평가로 맺어라(예: ~하는 모습을 보임 / ~역량을 드러냄 / ~자세를 지님)",
    "관찰형": "끝을 활동 서술로 맺어라(예: ~을 발표함 / ~에 기여함 / ~을 정리함)",
    "성장형": "끝을 성장으로 맺어라(예: ~을 키움 / ~을 넓힘 / ~으로 성장함)",
}
_CONN_HINT = {
    "하며": "절을 '~하며'로 이어", "하여": "절을 '~하여'로 이어",
    "통해": "'~을 통해'로 연결해", "는등": "'~하는 등'으로 나열해",
    "으로써": "'~함으로써'로 이어", "고": "절을 '~하고'로 이어",
}


def instruction(t: dict) -> str:
    """구조 타깃 하나를 LLM용 한 줄 지시로."""
    comp = ("한 문장으로 압축해" if t["comp"] == "단문"
            else "두 문장으로 나눠(활동 문장 + 평가/성장 문장),")
    order = ("'~단원에서/~활동에서'처럼 맥락으로 시작해 " if t["order"] == "맥락먼저" else "")
    conn = _CONN_HINT.get(t["conn"], "절을 자연스럽게 이어")
    end = _END_HINT.get(t["end"], "")
    return f"{order}{comp} {conn}, {end}"
