"""Tier A 골든셋 생성 — Python(진리)에서 기대 출력을 뽑아 csharp/golden/golden.json 저장.

C# Cli 러너가 이 파일을 읽어 자기 구현 결과와 exact-match 로 비교한다.
부동소수(BM25 점수)는 런간 합산순서로 미세 변동할 수 있어, 점수 대신 '랭킹(인덱스 순서)'을
비교 대상으로 삼는다.

사용: PYTHONHASHSEED=0 PYTHONPATH=<repo> python csharp/tools/gen_golden.py
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO))

from app.memory_store import (Example, _bm25_scores, _boost_subject,  # noqa: E402
                              tokenize)
import app.glossary as glossary_mod  # noqa: E402
from app.compliance import check as compliance_check  # noqa: E402
from app.compliance import summary as compliance_summary  # noqa: E402
from app.importer import extract_keywords, parse_records  # noqa: E402
from app.paraphrase import (_bigrams, _clean_line, _fix_spacing,  # noqa: E402
                            _is_eval_sent, _too_similar)
from app.patterns import classify  # noqa: E402
from app.roster_data import parse_student_label  # noqa: E402
from app.postprocess import nominalize_sentence, to_nominal_endings  # noqa: E402
from app.prompts import AREA_BY_KEY, AREAS, build_user_prompt  # noqa: E402
from app.variation import split_sentences  # noqa: E402


def bm25_rank(query, subject, docs, k):
    ex = [Example(-1, "x", d["subject"], d["keywords"], "", 1, 0.0) for d in docs]
    corpus = [tokenize(f"{d.keywords} {d.subject}") for d in ex]
    scores = _bm25_scores(tokenize(query), corpus)
    scores = _boost_subject(scores, ex, subject)
    order = sorted(range(len(ex)), key=lambda i: scores[i], reverse=True)
    top = order[:k]
    positive = [i for i in top if scores[i] > 0]
    return positive or top


# ── 입력 세트 ───────────────────────────────────────────────────────────
TOKENIZE_IN = [
    "일차함수 그래프 발표",
    "Arduino 신호등 제작 3회",
    "산과 염기 pH 실험",
    "책임감", "협동", "탐구력과 논리적 사고",
    "friend 친구 123 테스트",
    "",
]

NOMINALIZE_SENT_IN = [
    "그래프로 표현하는 데 성공하였다.",
    "친구에게 설명하는 태도를 보였다.",
    "발표를 준비했다.",
    "탐구를 꾸준히 한다.",
    "역량을 길렀다.",
    "결과를 그래프로 만들었다.",
    "어려운 친구를 도왔다.",
    "실험 원리를 깨달았다.",
    "책을 끝까지 읽었다.",
    "자유롭다.",
    "책임감이 강하다.",
    "맡은 역할을 끝까지 완수함.",
    "실험을 설계함",
    "높은 성취를 높였다.",
    "함께 나누었다.",
    "결론에 이르렀다.",
]

TO_NOMINAL_IN = [
    "산과 염기 실험을 설계하였다. 결과를 발표했다.",
    "책임감이 강하다; 성실하게 참여한다.",
    "여러 지시약의 색 변화를 확인하였다.\n원인을 토의하며 변인을 점검했다.",
    "맡은 역할을 성실히 수행함. 친구를 배려하는 태도를 보임.",
]

SPLIT_IN = [
    "실험을 설계함. 결과를 발표함. 원리를 정리함.",
    "한 문장만 있음.",
    "줄바꿈\n포함 문장. 두번째.",
    "마침표 없는 문장",
    "",
]

CLASSIFY_IN = [
    "산과 염기 단원에서 지시약 실험을 설계하며 탐구하는 태도를 보임.",
    "실험을 직접 설계함. 결과를 그래프로 정리해 발표함.",
    "맡은 역할을 끝까지 수행함.",
    "또래를 배려하는 마음을 지님.",
    "탐구를 통해 논리적 사고를 키움.",
    "활동에 성실히 참여하는 자세를 지님.",
]

BM25_CASES = [
    {"query": "일차함수 그래프 발표", "subject": "수학", "k": 3,
     "docs": [
         {"keywords": "이차함수 그래프 그리기", "subject": "수학"},
         {"keywords": "산 염기 지시약 실험", "subject": "과학"},
         {"keywords": "일차함수 기울기 발표 활동", "subject": "수학"},
         {"keywords": "역사 연표 정리", "subject": "역사"},
     ]},
    {"query": "지시약 실험 설계", "subject": "", "k": 2,
     "docs": [
         {"keywords": "지시약 색 변화 실험", "subject": "과학"},
         {"keywords": "일차함수 발표", "subject": "수학"},
         {"keywords": "실험 설계와 변인 통제", "subject": "과학"},
     ]},
    {"query": "책임감 성실", "subject": "국어", "k": 3,
     "docs": [
         {"keywords": "책임감 강함 성실", "subject": "수학"},
         {"keywords": "성실한 태도 발표", "subject": "국어"},
         {"keywords": "협동과 배려", "subject": "국어"},
     ]},
]


FIX_SPACING_IN = [
    "1 인 1 역을 맡음", "2 학기 발표", "이끌어 냄", "들어 감",
    "가지고 1인1역", "3 회 실험 진행", "생각해 봄",
]
CLEAN_LINE_IN = [
    "1) 첫 변형 / 두번째 절.", "- 불릿 문장", "  \"따옴표 문장\"  ",
    "3. 번호 문장 / 나눔 표현", "•블릿 없는 것", "[2] 대괄호 번호",
]
IS_EVAL_SENT_IN = [
    "논리적 사고력을 보임", "실험을 설계함", "리더십을 발휘함",
    "책임감이 강함", "발표를 함", "성실한 태도가 돋보임",
]
TOO_SIMILAR_PAIRS = [
    ("맡은 역할을 성실히 수행함", "맡은 역할을 수행함"),
    ("탐구하는 태도를 보임", "전혀 다른 내용의 문장임"),
    ("", ""),
    ("같은 문장", "같은 문장"),
]
BIGRAMS_IN = ["가나다라", "ab cd", "한", "실험 설계"]

EXTRACT_KW_IN = [
    "산과 염기 반응을 지시약으로 확인하는 실험을 설계하였으며 결과를 분석함.",
    "맡은 역할을 성실히 수행하며 책임감을 보임.",
    "친구들과 협력하여 발표 자료를 준비하고 발표에 적극 참여함.",
    "가나",
]
PARSE_REC_CASES = [
    ("첫 기록 문장입니다.\n\n둘째 문단 기록.", "auto"),
    ("한 줄 기록\n다른 줄 기록\n짧", "line"),
    ("문단A 계속\n이어짐\n\n문단B 내용", "para"),
    ("", "auto"),
    ("단일 문단만 있음", "auto"),
]
GLOSSARY_WORDS_IN = [
    ["아이오딘화 칼륨", "산 염기", "pH"],
    ["일차함수 그래프", "x", "이차 방정식"],
]
STUDENT_LABEL_IN = ["10101 김철수", "홍길동", "3반 이영희", "  20201   박 민수  ", "", "10101"]

COMPLIANCE_IN = [
    "TOEIC 900점을 받았으며 영어 실력이 뛰어남.",
    "교내 수학 경시대회 대회에 참가하여 준비함.",
    "모의고사 백분위 95를 기록함.",
    "논문을 작성하고 특허를 출원함.",
    "○○대학교 견학과 장학금 수혜.",
    "산과 염기 반응을 지시약으로 확인하는 실험을 설계함.",
    "어머니가 의사로 근무하심.",
    "자격증을 취득하고 어학연수를 다녀옴.",
]


def glossary_words(terms):
    glossary_mod._terms = {" ".join(t.split()) for t in terms if t.strip()}
    return sorted(glossary_mod.words())


def system_cases():
    return [{"area": a.key, "out": a.system_prompt()} for a in AREAS]


def user_cases():
    cases = []
    combos = [
        ("seteuk", "수학", "일차함수 그래프 발표 / 오개념 점검", "간결하게", "3문장", 1),
        ("seteuk", "", "실험 설계 발표", "", "", 1),
        ("haengteuk", "", "1인1역 성실 / 토론 적극", "", "", 1),
        ("polish", "", "이 학생은 책임감이 강하다.", "", "", 1),
        ("seteuk", "과학", "산 염기 실험 / 변인 점검", "", "", 5),
        ("jinro", "", "공학 계열 관심 / 독서 탐구", "따뜻하게", "", 1),
    ]
    for key, subj, kw, tone, lh, n in combos:
        area = AREA_BY_KEY[key]
        cases.append({
            "area": key, "subject": subj, "keywords": kw, "tone": tone,
            "length_hint": lh, "n": n,
            "out": build_user_prompt(area, subject=subj, keywords=kw, tone=tone,
                                     length_hint=lh, n_variations=n),
        })
    return cases


def main() -> int:
    golden = {
        "tokenize": [{"in": s, "out": tokenize(s)} for s in TOKENIZE_IN],
        "nominalize_sentence": [{"in": s, "out": nominalize_sentence(s)} for s in NOMINALIZE_SENT_IN],
        "to_nominal": [{"in": s, "out": to_nominal_endings(s)} for s in TO_NOMINAL_IN],
        "split_sentences": [{"in": s, "out": split_sentences(s)} for s in SPLIT_IN],
        "classify": [{"in": s, "out": classify(s)} for s in CLASSIFY_IN],
        "bm25_rank": [
            {**c, "out": bm25_rank(c["query"], c["subject"], c["docs"], c["k"])}
            for c in BM25_CASES
        ],
        "system_prompt": system_cases(),
        "build_user_prompt": user_cases(),
        "fix_spacing": [{"in": s, "out": _fix_spacing(s)} for s in FIX_SPACING_IN],
        "clean_line": [{"in": s, "out": _clean_line(s)} for s in CLEAN_LINE_IN],
        "is_eval_sent": [{"in": s, "out": _is_eval_sent(s)} for s in IS_EVAL_SENT_IN],
        "too_similar": [{"a": a, "b": b, "out": _too_similar(a, b)} for a, b in TOO_SIMILAR_PAIRS],
        "bigrams": [{"in": s, "out": sorted(_bigrams(s))} for s in BIGRAMS_IN],
        "extract_keywords": [{"in": s, "out": extract_keywords(s)} for s in EXTRACT_KW_IN],
        "parse_records": [{"in": s, "mode": m, "out": parse_records(s, m)} for s, m in PARSE_REC_CASES],
        "glossary_words": [{"in": ts, "out": glossary_words(ts)} for ts in GLOSSARY_WORDS_IN],
        "parse_student_label": [{"in": s, "out": list(parse_student_label(s))} for s in STUDENT_LABEL_IN],
        "compliance_check": [{"in": s, "out": [list(t) for t in compliance_check(s)]} for s in COMPLIANCE_IN],
        "compliance_summary": [{"in": s, "out": compliance_summary(s)} for s in COMPLIANCE_IN],
    }
    dest = REPO / "csharp" / "golden" / "golden.json"
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(golden, ensure_ascii=False, indent=1), encoding="utf-8")
    counts = {k: len(v) for k, v in golden.items()}
    print("wrote", dest)
    print("cases:", counts)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
