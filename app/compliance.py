"""생기부 규정 위반 검사 — 교육부 '학교생활기록부 기재요령' 기재 불가 항목 기반.

교사 입력·생성 문장에 기재 금지 내용(공인어학시험·교외수상·논문·특허·모의고사·
대학명·장학금·해외활동 등)이 있으면 경고한다. 하드 차단이 아니라 '경고'다 —
맥락에 따라 교사가 판단하도록. (출처: 2025 학교생활기록부 기재요령, 교육부훈령 519호)
"""
from __future__ import annotations

import re

# (레벨, 분류, 정규식). 레벨: 'block'=명백한 위반, 'warn'=맥락 확인 필요
_RULES: list[tuple[str, str, re.Pattern]] = [
    # 공인어학시험 — 참여·성적·수상 모두 기재 불가
    ("block", "공인어학시험",
     re.compile(r"(TOEIC|TOEFL|TEPS|IELTS|OPIC|TOEIC\s*Speaking|HSK|JPT|JLPT|"
                r"DELE|DELF|DALF|TORFL|TESTDAF|DSH|DSD|토익|토플|텝스|아이엘츠|"
                r"한자능력검정|한자급수|실용한자)", re.IGNORECASE)),
    # 모의고사·전국연합학력평가 성적
    ("block", "모의고사·수능성적",
     re.compile(r"(모의고사|전국연합\s*학력평가|학력평가\s*\d|수능\s*\d|"
                r"백분위|석차\s*등급|원점수|등급컷)")),
    # 학술활동 — 논문·학회지·지식재산권
    ("block", "논문·학회지·특허",
     re.compile(r"(논문|학회지|학술지|저널|특허|실용신안|상표\s*등록|"
                r"디자인\s*등록|지식재산권|저서\s*출간|책\s*출간|출판)")),
    # 대회·수상·표창 — 교내외 대회 참가·준비 사실도 수상 여부 무관 기재 불가
    ("warn", "대회·수상·표창",
     re.compile(r"(표창장|감사장|공로상|수상\s*(함|한|하여|경력)|입상|"
                r"경시\s*대회|올림피아드|대회\s*(참가|참여|출전|준비|나감)|"
                r"(참가|참여|출전)\S*\s*대회)")),
    # 자격증 취득 사실(국가기술자격은 기재 가능 — 교사 확인용 경고)
    ("warn", "자격증 취득",
     re.compile(r"(자격증.{0,4}(취득|획득|딴|따)|자격을?\s*(취득|획득)|"
                r"급수\s*(취득|인증)|기능사\s*자격|기사\s*자격)")),
    # 해외 활동
    ("warn", "해외 활동",
     re.compile(r"(어학연수|해외\s*봉사|해외\s*연수|해외\s*활동|유학)")),
    # 장학금
    ("warn", "장학금",
     re.compile(r"(장학금|장학생|장학\s*재단)")),
    # 특정 대학·기관·상호명
    ("warn", "대학·기관·상호명",
     re.compile(r"([가-힣A-Za-z]{2,}\s*대학교|[가-힣]{2,}\s*대학원|"
                r"[가-힣A-Za-z]{2,}\s*학원|주식회사|㈜|\(주\))")),
    # 부모의 사회·경제적 지위
    ("warn", "부모 정보",
     re.compile(r"(아버지|어머니|부모(님)?)\s*(가|는|의|께서)?\s*"
                r"[가-힣]*\s*(대표|사장|교수|의사|변호사|공무원|회사|직장|근무|운영)")),
]


def check(text: str) -> list[tuple[str, str, str]]:
    """(레벨, 분류, 매칭문자열) 목록. 위반 없으면 빈 리스트."""
    text = text or ""
    out: list[tuple[str, str, str]] = []
    seen: set[str] = set()
    for level, cat, pat in _RULES:
        for m in pat.finditer(text):
            token = m.group(0).strip()
            key = f"{cat}:{token}"
            if token and key not in seen:
                seen.add(key)
                out.append((level, cat, token))
    return out


def summary(text: str) -> str:
    """경고 요약 한 줄(없으면 빈 문자열)."""
    v = check(text)
    if not v:
        return ""
    blocks = [f"{c}('{t}')" for lv, c, t in v if lv == "block"]
    warns = [f"{c}('{t}')" for lv, c, t in v if lv == "warn"]
    parts = []
    if blocks:
        parts.append("기재 불가: " + ", ".join(blocks))
    if warns:
        parts.append("확인 요망: " + ", ".join(warns))
    return " / ".join(parts)
