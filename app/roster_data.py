"""학급 명단 읽기 — 저장된 로스터(roster_*.json)에서 학급→학생을 뽑는다.

메모 도구가 학생을 고를 때 쓴다. Qt·모델과 무관한 가벼운 순수 로직이라
트레이 도구에서도 부담 없이 임포트한다. 영역별 로스터를 학급 이름으로 병합한다.
"""
from __future__ import annotations

import glob
import json
from pathlib import Path

from . import config


def classes_and_students(area: str | None = None) -> dict[str, list[str]]:
    """{학급: [학생 표시명, …]}. 학생 표시명 = '학번 이름'(학번 없으면 이름만).

    area를 주면 그 영역 로스터(roster_{area}.json)에 **실제 등록된 학급·학생만**
    돌려준다(메모가 고아 학급을 만들지 않도록). area가 없으면 모든 영역을 병합한다.
    """
    if area:
        files = [str(config.DATA_DIR / f"roster_{area}.json")]
    else:
        files = glob.glob(str(config.DATA_DIR / "roster_*.json"))
    merged: dict[str, dict[tuple[str, str], bool]] = {}
    for f in files:
        try:
            data = json.loads(Path(f).read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        if not isinstance(data, dict):
            continue
        for klass, d in data.items():
            rows = d.get("rows", []) if isinstance(d, dict) else []
            bag = merged.setdefault(klass, {})
            for row in rows:
                if not isinstance(row, list):
                    continue
                num = (row[0] if len(row) > 0 else "").strip()
                name = (row[1] if len(row) > 1 else "").strip()
                if num or name:
                    bag[(num, name)] = True
    out: dict[str, list[str]] = {}
    for klass in sorted(merged):
        studs = sorted(merged[klass], key=lambda t: ((t[0] or "999"), t[1]))
        labels = [(f"{num} {name}".strip() if num else name)
                  for num, name in studs if (num or name)]
        out[klass] = labels
    return out


def roster_records(area: str) -> list[dict[str, str]]:
    """그 영역에 등록된 학생 레코드 목록 [{'klass','num','name'}, …].

    메모의 학급·번호·이름 동기화(자동완성·상호 채움)에 쓴다. 등록된 시트에서만
    뽑으므로 고아를 만들지 않는다. 학번·이름이 모두 빈 행은 건너뛴다.
    """
    path = config.DATA_DIR / f"roster_{area}.json"
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return []
    if not isinstance(data, dict):
        return []
    out: list[dict[str, str]] = []
    seen: set[tuple[str, str, str]] = set()
    for klass, d in data.items():
        rows = d.get("rows", []) if isinstance(d, dict) else []
        for row in rows:
            if not isinstance(row, list):
                continue
            num = (row[0] if len(row) > 0 else "").strip()
            name = (row[1] if len(row) > 1 else "").strip()
            if not (num or name):
                continue
            key = (klass, num, name)
            if key in seen:
                continue
            seen.add(key)
            out.append({"klass": klass, "num": num, "name": name})
    return out


def parse_student_label(label: str) -> tuple[str, str]:
    """'학번 이름' 표시명을 (학번, 이름)으로 분리. 학번 없으면 ('', 이름)."""
    s = (label or "").strip()
    if not s:
        return "", ""
    parts = s.split(None, 1)
    if len(parts) == 2 and parts[0].isdigit():
        return parts[0], parts[1]
    return "", s


def add_memo_to_roster(*, area: str, klass: str, num: str, name: str,
                       text: str) -> str:
    """메모를 해당 영역 로스터에 반영한다.

    학생 행이 있으면 첫 '내용' 열에 이어붙이고('append'), 없으면 새 행을
    삽입한다('insert'). 학번이 있으면 학번으로, 없으면 이름으로 행을 찾는다.

    **학급은 절대 새로 만들지 않는다** — 그 영역에 등록된 시트가 아니면
    'no_class'를 돌려주고 아무 것도 쓰지 않는다(고아 학급 방지). 학급/이름이
    비면 ''을 돌려준다.
    """
    text = (text or "").strip()
    if not text or not klass or not (num or name):
        return ""
    path = config.DATA_DIR / f"roster_{area}.json"
    try:
        data = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}
    except (OSError, json.JSONDecodeError):
        data = {}
    if not isinstance(data, dict):
        data = {}
    entry = data.get(klass)
    if not isinstance(entry, dict):                 # 등록 안 된 학급 → 생성 금지
        return "no_class"
    headers = entry.setdefault("headers", ["내용"]) or ["내용"]
    rows = entry.setdefault("rows", [])

    target = None
    for row in rows:
        if not isinstance(row, list):
            continue
        rnum = (row[0] if len(row) > 0 else "").strip()
        rname = (row[1] if len(row) > 1 else "").strip()
        if num and rnum == num:
            target = row
            break
        if not num and name and rname == name:
            target = row
            break

    if target is None:                      # 학생 없음 → 행 삽입
        newrow = [num, name] + [""] * len(headers)
        newrow[2] = text
        rows.append(newrow)
        result = "insert"
    else:                                   # 학생 있음 → 내용 추가(이어붙이기)
        while len(target) < 3:
            target.append("")
        cur = (target[2] or "").strip()
        target[2] = f"{cur}\n{text}" if cur else text
        result = "append"

    try:
        path.write_text(json.dumps(data, ensure_ascii=False, indent=2),
                        encoding="utf-8")
    except OSError:
        return ""
    return result
