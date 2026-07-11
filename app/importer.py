"""교사가 그동안 작성한 생기부를 일괄 불러와 학습(예시 뱅크)에 넣는다.

과거 기록에는 '키워드'가 없으므로, 각 기록에서 핵심 단어를 추출해 의사 키워드를
만들고(BM25 검색·few-shot 입력용), 기록 원문을 출력 예시로 저장한다.
지원: 붙여넣은 텍스트, .txt, .csv (엑셀은 CSV로 저장해 사용).
"""
from __future__ import annotations

import csv
import re
from collections import Counter
from pathlib import Path

from .memory_store import MemoryStore

# 키워드로 부적절한 흔한 단어
_STOP = {
    "학생", "활동", "모습", "태도", "자세", "과정", "내용", "수업", "시간", "부분",
    "생각", "경우", "정도", "대해", "통해", "위해", "다양", "여러", "자신", "매우",
    "항상", "특히", "또한", "그리고", "이를", "관련", "다른", "함께", "스스로",
}
# 어미/조사 꼬리 제거용
_TAIL_LONG = ("하였으며", "하면서", "하였고", "하며", "하고", "으로", "에서", "이며",
              "하는", "한다", "였으며", "되어", "되며")
_TAIL_SHORT = ("함", "임", "됨", "음", "며", "고", "을", "를", "이", "가", "은", "는",
               "과", "와", "에", "의", "도", "만")


def extract_keywords(text: str, max_k: int = 6) -> str:
    """기록에서 핵심 단어 몇 개를 뽑아 ' / ' 로 잇는다(의사 키워드)."""
    words = re.findall(r"[가-힣]{2,}", text)
    cleaned: list[str] = []
    for w in words:
        for suf in _TAIL_LONG:
            if w.endswith(suf) and len(w) > len(suf):
                w = w[: -len(suf)]
                break
        for suf in _TAIL_SHORT:
            if w.endswith(suf) and len(w) > 2:
                w = w[: -len(suf)]
                break
        if len(w) >= 2 and w not in _STOP:
            cleaned.append(w)
    if not cleaned:
        return text[:30]
    counts = Counter(cleaned)
    top = [w for w, _ in counts.most_common(max_k)]
    return " / ".join(dict.fromkeys(top))


def parse_records(text: str, mode: str = "auto", *, min_len: int = 5) -> list[str]:
    """텍스트를 기록 단위로 나눈다. mode: auto|line|para."""
    text = (text or "").replace("\r\n", "\n").strip()
    if not text:
        return []
    if mode == "para" or (mode == "auto" and re.search(r"\n\s*\n", text)):
        chunks = re.split(r"\n\s*\n", text)
    else:
        chunks = text.split("\n")
    out = []
    for c in chunks:
        c = c.strip()
        if len(c) >= min_len:
            out.append(c)
    return out


def parse_csv(path: Path, *, min_len: int = 5) -> list[str]:
    """CSV의 각 행에서 가장 긴 셀을 기록으로 본다(여분 열 무시)."""
    records: list[str] = []
    with open(path, newline="", encoding="utf-8-sig", errors="ignore") as f:
        for row in csv.reader(f):
            if not row:
                continue
            cell = max((c.strip() for c in row), key=len, default="")
            if len(cell) >= min_len:
                records.append(cell)
    return records


_CONTENT_HDR = ("특기사항", "세부능력", "행동특성", "종합의견", "내용", "기재", "특기", "의견")


def _col_profiles(rows: list[tuple]) -> list[dict]:
    """각 열의 헤더·예시·평균길이 프로파일."""
    ncol = max((len(r) for r in rows), default=0)
    header = [(str(rows[0][i]).strip() if i < len(rows[0]) and rows[0][i] is not None else "")
              for i in range(ncol)]
    profs = []
    for c in range(ncol):
        vals = [str(r[c]).strip() for r in rows[1:]
                if c < len(r) and r[c] is not None and str(r[c]).strip()]
        avg = sum(len(v) for v in vals) / len(vals) if vals else 0
        sample = max(vals, key=len)[:60] if vals else ""
        profs.append({"i": c, "header": header[c], "sample": sample, "avg": avg})
    return profs


def _heuristic_col(profs: list[dict]) -> int | None:
    """헤더 키워드 우선, 없으면 평균 글자수가 가장 긴 열(서술 본문)."""
    for p in profs:
        if any(k in p["header"] for k in _CONTENT_HDR):
            return p["i"]
    cand = [p for p in profs if p["avg"] > 0]
    return max(cand, key=lambda p: p["avg"])["i"] if cand else None


def _llm_col(picker, profs: list[dict]) -> int | None:
    """모델(picker)에게 생기부 본문 열 번호를 묻는다.
    단, 모델이 '짧은 글 열'(예: 과목명)을 잘못 고르는 경우가 있어,
    선택한 열이 충분히 긴 서술일 때만 신뢰한다(아니면 None → 휴리스틱)."""
    if picker is None or not profs:
        return None
    desc = "\n".join(f"[{p['i']}] 헤더:'{p['header']}' 예시:'{p['sample']}'" for p in profs)
    try:
        ans = picker(desc, len(profs))
        m = re.search(r"\d+", ans or "")
        if not m:
            return None
        idx = int(m.group())
        prof = next((p for p in profs if p["i"] == idx), None)
        if prof is None:
            return None
        max_avg = max((p["avg"] for p in profs), default=0)
        if prof["avg"] >= max(30, 0.5 * max_avg):   # 긴 서술 열일 때만 신뢰
            return idx
    except Exception:
        return None
    return None


def parse_xlsx(path: Path, *, min_len: int = 5, picker=None) -> list[str]:
    """엑셀에서 생기부 본문을 추출한다. 어떤 양식이든 모델(picker)이 본문 열을
    판별하고(파일당 1회), 실패하면 휴리스틱(헤더 키워드 / 가장 긴 글 열)로 폴백."""
    import openpyxl
    wb = openpyxl.load_workbook(path, data_only=True, read_only=True)
    records: list[str] = []
    for ws in wb.worksheets:
        rows = list(ws.iter_rows(values_only=True))
        if len(rows) < 2:
            continue
        profs = _col_profiles(rows)
        col = _llm_col(picker, profs)
        if col is None:
            col = _heuristic_col(profs)
        if col is None:
            continue
        for r in rows[1:]:
            v = r[col] if col < len(r) else None
            v = str(v).strip() if v is not None else ""
            if len(v) >= min_len:
                records.append(v)
    wb.close()
    return records


def load_records_from_file(path: str | Path, mode: str = "auto", picker=None) -> list[str]:
    p = Path(path)
    if p.suffix.lower() in (".xlsx", ".xlsm"):
        return parse_xlsx(p, picker=picker)
    if p.suffix.lower() == ".csv":
        return parse_csv(p)
    return parse_records(p.read_text(encoding="utf-8", errors="ignore"), mode)


def load_records_from_files(paths, mode: str = "auto", picker=None) -> list[str]:
    """여러 파일의 기록을 한 번에 모은다(중복 제거)."""
    out, seen = [], set()
    for p in paths:
        try:
            recs = load_records_from_file(p, mode, picker=picker)
        except Exception:
            continue
        for r in recs:
            r = r.strip()
            if r and r not in seen:
                seen.add(r)
                out.append(r)
    return out


def import_records(store: MemoryStore, *, area: str, subject: str,
                   records: list[str]) -> dict:
    """기록들을 예시 뱅크에 저장. 이미 있는 기록(중복)과 생기부 규정 위반(대회·수상·
    어학시험 등)은 제외한다 — 학습(few-shot) 오염 방지.
    반환: {'added','dup','blocked'}."""
    from . import compliance
    try:
        existing = {ex.output_text.strip() for ex in store._rows_for_area(area)}
    except Exception:
        existing = set()
    added = dup = blocked = 0
    for rec in records:
        rec = rec.strip()
        if not rec:
            continue
        if rec in existing:                        # DB에 이미 있는 기록
            dup += 1
            continue
        if compliance.check(rec):                  # 규정 위반/주의(대회·수상·어학시험 등)
            blocked += 1                           # 학습 재료 오염 방지 위해 warn도 제외
            continue
        store.add_example(
            area=area, subject=subject,
            keywords=extract_keywords(rec), output_text=rec,
        )
        existing.add(rec)
        added += 1
    return {"added": added, "dup": dup, "blocked": blocked}
