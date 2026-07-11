"""생기부 문장 종결을 모두 명사형(함/임/됨/음 …)으로 강제 변환한다.

학습/모델 의존 없이 결정론적 규칙으로 동작한다.
원칙: '문장이 평서형 종결 '~다' 로 끝날 때만' 변환한다.
      이미 명사형(받침 ㅁ/ㄻ, 예: 함·임·됨·앎)으로 끝난 문장은 건드리지 않는다.

한국어 활용이 복잡하므로 (1) 받침 단위 자모 조작 + (2) 자주 쓰이는 활용/불규칙
어미 치환표를 함께 사용한다. 모델 프롬프트가 이미 명사형을 유도하므로 본 로직은
평서형으로 새어 나온 종결을 잡는 '안전망'이다.
"""
from __future__ import annotations

import re

# ── 한글 자모 분해/조합 ────────────────────────────────────────────────
_BASE = 0xAC00
_CHO = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ"
_JUNG = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ"
_JONG = ["", "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ", "ㄹ", "ㄺ", "ㄻ",
         "ㄼ", "ㄽ", "ㄾ", "ㄿ", "ㅀ", "ㅁ", "ㅂ", "ㅄ", "ㅅ", "ㅆ",
         "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ"]


def _is_syllable(ch: str) -> bool:
    return len(ch) == 1 and 0xAC00 <= ord(ch) <= 0xD7A3


def _decompose(ch: str) -> tuple[str, str, str]:
    code = ord(ch) - _BASE
    cho = code // 588
    jung = (code % 588) // 28
    jong = code % 28
    return _CHO[cho], _JUNG[jung], _JONG[jong]


def _compose(cho: str, jung: str, jong: str) -> str:
    return chr(_BASE + _CHO.index(cho) * 588 + _JUNG.index(jung) * 28 + _JONG.index(jong))


def _has_m_final(ch: str) -> bool:
    """음절의 받침이 ㅁ 을 포함하면(ㅁ, ㄻ) 이미 명사형으로 본다."""
    if not _is_syllable(ch):
        return False
    _, _, jong = _decompose(ch)
    return jong in ("ㅁ", "ㄻ")


# ── 어미 치환표 (긴 접미사 우선, 위에서부터 검사) ───────────────────────
# 각 항목: 문장 핵심(core)이 이 접미사로 끝나면 → 치환.
_SUFFIX_RULES: list[tuple[str, str]] = [
    # 하다 계열(가장 흔함) — '였다' 규칙보다 먼저 와야 한다(하였다→하임 방지)
    ("하였다", "함"), ("하였음", "함"), ("했다", "함"), ("한다", "함"),
    ("하다", "함"),
    # 되다 계열
    ("되었다", "됨"), ("되었음", "됨"), ("됐다", "됨"), ("됬다", "됨"),
    ("된다", "됨"), ("되다", "됨"),
    # ㅂ 불규칙 형용사 (…음이 아니라 …움)
    ("롭다", "로움"), ("답다", "다움"), ("럽다", "러움"), ("겁다", "거움"),
    ("갑다", "가움"), ("렵다", "려움"), ("쉽다", "쉬움"), ("깝다", "까움"),
    ("덥다", "더움"), ("춥다", "추움"), ("맵다", "매움"), ("곱다", "고움"),
    ("돕다", "도움"), ("굽다", "구움"),
    # ㄷ 불규칙 (자주 쓰이는 것) — 사전형 + 과거형(애매하지 않은 것만)
    ("깨닫다", "깨달음"), ("듣다", "들음"),
    ("깨달았다", "깨달음"), ("깨달았음", "깨달음"),
    # 르 불규칙 과거: 기르다→길렀다→기름 (일반 규칙이 '길렀음'으로 잘못 처리)
    ("길렀다", "기름"), ("길렀음", "기름"), ("이르렀다", "이름"),
    # 모음어간 + 었/았 축약(2음절) → 명사형
    ("냈다", "냄"), ("냈음", "냄"),
    ("웠다", "움"), ("줬다", "줌"), ("췄다", "춤"), ("졌다", "짐"),
    ("겼다", "김"), ("렸다", "림"), ("혔다", "힘"), ("폈다", "핌"),
    ("쳤다", "침"), ("뤘다", "룸"), ("셨다", "심"), ("켰다", "킴"),
    ("꼈다", "낌"),
    # 모음어간 + 었/았 (별도 음절) 풀어쓴 형태
    ("이었다", "임"), ("주었다", "줌"), ("이루었다", "이룸"),
    # '이/히' 계열 + 었다 → 임 (보였다→보임, 높였다→높임, 쓰였다→쓰임)
    ("였다", "임"), ("였음", "임"),
    # ㅂ 불규칙 과거(ㅗ어간): 도왔다→도움 등 (일반 '왔다→옴'보다 먼저)
    ("도왔다", "도움"), ("고왔다", "고움"),
    # 1음절 축약(가았다=갔다 …) → 명사형
    ("갔다", "감"), ("왔다", "옴"), ("봤다", "봄"), ("났다", "남"),
    ("섰다", "섬"), ("탔다", "탐"), ("샀다", "삼"), ("찼다", "참"),
    ("짰다", "짬"), ("썼다", "씀"),
    # (받침/ㄹ/모음 어간 + 었/았다 → 음/ㄻ/ㅁ 은 _convert_core 에서 어간 분해로 처리)
    # 현재형 ㄴ다/는다
    ("인다", "임"), ("낸다", "냄"), ("는다", "음"), ("ㄴ다", "ㅁ"),
    # 계사/존재/형용사 사전형
    ("이다", "임"), ("있다", "있음"), ("없다", "없음"), ("같다", "같음"),
]


def _nominalize_stem(stem: str) -> str | None:
    """어간(받침/모음/ㄹ)을 명사형으로. 한글 음절이 아니면 None."""
    if not stem:
        return None
    last = stem[-1]
    if not _is_syllable(last):
        return None
    cho, jung, jong = _decompose(last)
    if jong == "":               # 받침 없음 → ㅁ 받침 추가 (크다→큼, 나누→나눔)
        return stem[:-1] + _compose(cho, jung, "ㅁ")
    if jong == "ㄹ":             # ㄹ → ㄻ (살다→삶, 만들→만듦, 길다→긺)
        return stem[:-1] + _compose(cho, jung, "ㄻ")
    return stem + "음"           # 그 외 받침 → '음' 추가 (좋다→좋음, 먹→먹음)


# 과거 시제 어미: 떼어낸 뒤 어간을 명사형화 (만들었다→만듦, 나누었다→나눔, 먹었다→먹음)
_PAST_SUFFIXES = ("었다", "았다", "었음", "았음")


def _convert_core(core: str) -> str:
    """'…다' 로 끝나는 문장 핵심을 명사형으로 변환."""
    for suf, rep in _SUFFIX_RULES:
        if core.endswith(suf):
            return core[: -len(suf)] + rep
    for suf in _PAST_SUFFIXES:
        if core.endswith(suf):
            nom = _nominalize_stem(core[: -len(suf)])
            return nom if nom is not None else core
    nom = _nominalize_stem(core[:-1])   # 사전형: '다' 제거 후 어간 명사형화
    return nom if nom is not None else core


# 문장 끝에서 떼어낼 꼬리(종결부호·따옴표·괄호)
_TAIL_RE = re.compile(r"[\s.。!?…\"'’”」』）)\]]*$")


def nominalize_sentence(sentence: str) -> str:
    """문장 하나를 명사형 종결로. '…다' 로 끝날 때만 변환한다."""
    if not sentence.strip():
        return sentence
    m = _TAIL_RE.search(sentence)
    tail = m.group(0) if m else ""
    core = sentence[: len(sentence) - len(tail)]
    if not core:
        return sentence
    last = core[-1]
    if _has_m_final(last):       # 이미 명사형(함/임/됨/앎 …)
        return sentence
    if not core.endswith("다"):  # 평서형 종결이 아니면 손대지 않음
        return sentence
    return _convert_core(core) + tail


# 문장 경계 분리(구분자 보존). 마침표/줄바꿈 기준.
_SPLIT_RE = re.compile(r"(?<=[.。!?])\s+|\n+")


_NUM_MARK_RE = re.compile(r"(?m)^\s*\d+\s*[).\]]\s*")


def split_variants(text: str) -> list[str]:
    """'1) … 2) …' 형태의 번호 매긴 변형들을 분리한다."""
    parts = _NUM_MARK_RE.split(text)
    items = [p.strip() for p in parts if p.strip()]
    return items


def format_variants(variants: list[str]) -> str:
    """변형 목록을 교사가 보기 좋게(번호·구분선) 정리한다. 각 변형은 명사형 변환."""
    blocks = []
    for i, v in enumerate(variants, 1):
        blocks.append(f"[변형 {i}]\n{to_nominal_endings(v)}")
    return "\n\n".join(blocks)


# 모델이 프롬프트 지시문을 그대로 따라 쓴(에코) 신호 — 생기부 본문엔 나올 수 없는 문구
_PROMPT_ECHO = re.compile(
    r"위 키워드|철자를 바꾸지|그대로 정확히 사용|키워드/관찰|다듬을 초안|"
    r"한 줄에 하나|의미는 같되|새로운 정보를 지어|접속어로 시작|예시 형식|"
    r"핵심 내용을 항목별|문체/톤|분량\s*:|과목\s*:|\{\{|\}\}")


def has_prompt_echo(text: str) -> bool:
    """생성 결과에 프롬프트 지시문이 새어 나왔는지."""
    return bool(_PROMPT_ECHO.search(text or ""))


def strip_prompt_echo(text: str) -> str:
    """프롬프트 지시문이 섞이면 그 앞에서 잘라낸다(안전망). 잘려서 문장이 어색하면
    호출부에서 폐기하도록 has_prompt_echo로 먼저 걸러야 한다."""
    if not text:
        return text
    m = _PROMPT_ECHO.search(text)
    if m:
        text = text[:m.start()]
    return text.strip().strip("\"'“”").strip().rstrip(",·-—•/ ").strip()


def to_nominal_endings(text: str) -> str:
    """본문 전체의 모든 문장 종결을 명사형으로 변환."""
    if not text:
        return text
    # 생기부에 어울리지 않는 구두점 정리(세미콜론·콜론 → 마침표)
    text = re.sub(r"\s*[;:]\s*", ". ", text)
    # 줄바꿈은 보존하면서 처리하기 위해 줄 단위로 나눈 뒤 문장 분리
    out_lines: list[str] = []
    for line in text.split("\n"):
        if not line.strip():
            out_lines.append(line)
            continue
        # 마침표 뒤 공백으로 문장 분리(구분자 유지)
        parts = re.split(r"(?<=[.。!?])(\s+)", line)
        rebuilt = "".join(
            nominalize_sentence(p) if i % 2 == 0 else p
            for i, p in enumerate(parts)
        )
        out_lines.append(rebuilt)
    return "\n".join(out_lines)
