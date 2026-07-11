"""씨드 코퍼스(+선택: 교사 예시 DB) → QLoRA SFT 학습용 JSONL.

앱의 실제 system/user 프롬프트와 '동일하게' 포맷해서 학습·추론 일관성을 보장한다.
각 줄: {"messages":[{role:system},{role:user},{role:assistant}]}  (Qwen chat 형식)

사용: PYTHONPATH=. .venv/bin/python train/build_dataset.py [--include-db]
출력: train/data/sft.jsonl
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

# 프로젝트 루트를 path에 추가
ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from app import config  # noqa: E402
from app.prompts import AREA_BY_KEY, build_user_prompt  # noqa: E402

OUT = ROOT / "train" / "data" / "sft.jsonl"


def _source_path() -> Path:
    """--source <jsonl> 로 코퍼스 소스 지정(기본: 씨드 코퍼스)."""
    if "--source" in sys.argv:
        return Path(sys.argv[sys.argv.index("--source") + 1])
    return config.SEED_CORPUS_PATH


def _out_path() -> Path:
    if "--out" in sys.argv:
        return Path(sys.argv[sys.argv.index("--out") + 1])
    return OUT


def make_record(area_key: str, subject: str, keywords: str, output: str) -> dict | None:
    area = AREA_BY_KEY.get(area_key)
    if area is None or not output.strip() or not keywords.strip():
        return None
    return {
        "messages": [
            {"role": "system", "content": area.system_prompt()},
            {"role": "user", "content": build_user_prompt(
                area, subject=subject, keywords=keywords, tone="", length_hint="")},
            {"role": "assistant", "content": output.strip()},
        ]
    }


def from_seed_corpus() -> list[dict]:
    recs = []
    for line in _source_path().read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        try:
            o = json.loads(line)
        except json.JSONDecodeError:
            continue
        r = make_record(o.get("area", ""), o.get("subject", ""),
                        o.get("keywords", ""), o.get("output", ""))
        if r:
            recs.append(r)
    return recs


def from_teacher_db() -> list[dict]:
    """교사가 채택/임포트한 예시도 학습에 포함(선택)."""
    import sqlite3
    if not config.DB_PATH.exists():
        return []
    conn = sqlite3.connect(str(config.DB_PATH))
    conn.row_factory = sqlite3.Row
    recs = []
    try:
        rows = conn.execute(
            "SELECT area, subject, keywords, output_text FROM examples WHERE rating>=1"
        ).fetchall()
    except sqlite3.Error:
        return []
    finally:
        conn.close()
    for r in rows:
        rec = make_record(r["area"], r["subject"] or "", r["keywords"], r["output_text"])
        if rec:
            recs.append(rec)
    return recs


def main() -> None:
    include_db = "--include-db" in sys.argv
    recs = from_seed_corpus()
    n_seed = len(recs)
    n_db = 0
    if include_db:
        db = from_teacher_db()
        n_db = len(db)
        recs += db

    # 중복 제거(같은 user+assistant)
    seen, uniq = set(), []
    for r in recs:
        key = (r["messages"][1]["content"], r["messages"][2]["content"])
        if key not in seen:
            seen.add(key)
            uniq.append(r)

    out = _out_path()
    out.parent.mkdir(parents=True, exist_ok=True)
    with open(out, "w", encoding="utf-8") as f:
        for r in uniq:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")

    print(f"소스 {n_seed} + 교사DB {n_db} → 중복제거 후 {len(uniq)}건")
    print(f"저장: {out}")
    if uniq:
        import statistics
        lens = [len(r["messages"][2]["content"]) for r in uniq]
        print(f"출력 길이(자): 최소 {min(lens)} / 평균 {statistics.mean(lens):.0f} / 최대 {max(lens)}")


if __name__ == "__main__":
    main()
