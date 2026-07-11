"""retrieve 골든 — Python이 SQLite DB를 시드하고 retrieve/retrieve_seed 결과를 뽑는다.
C# 러너가 같은 .sqlite3 파일을 열어 자기 구현으로 재현·비교한다(end-to-end 데이터 계층).

사용: PYTHONHASHSEED=0 PYTHONPATH=<repo> python csharp/tools/gen_golden_retrieve.py
"""
from __future__ import annotations

import json
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO))

from app.memory_store import MemoryStore  # noqa: E402

GOLDEN = REPO / "csharp" / "golden"
DB = GOLDEN / "retrieve.sqlite3"

EXAMPLES = [
    # (area, subject, keywords, output_text)
    ("seteuk", "수학", "일차함수 그래프 기울기 발표", "일차함수의 기울기를 그래프로 설명하며 발표함."),
    ("seteuk", "수학", "이차함수 최댓값 탐구", "이차함수의 최댓값을 여러 방법으로 탐구함."),
    ("seteuk", "과학", "산 염기 지시약 실험 설계", "산과 염기 반응을 지시약으로 확인하는 실험을 설계함."),
    ("seteuk", "과학", "변인 통제 실험 재설계", "예상과 다른 결과의 원인을 변인 통제로 재점검함."),
    ("seteuk", "국어", "토론 논거 정리 발표", "토론에서 논거를 체계적으로 정리해 발표함."),
    ("haengteuk", "", "1인1역 성실 책임감", "맡은 1인1역을 끝까지 수행하며 책임감을 보임."),
    ("haengteuk", "", "토론 적극 성장", "2학기 들어 토론에 적극적으로 참여하며 성장함."),
]

SEED = [
    ("seteuk", "수학", "함수 발표", "함수의 성질을 그래프로 정리해 발표함."),
    ("seteuk", "과학", "실험 설계", "실험을 직접 설계하고 결과를 해석함."),
    ("haengteuk", "", "배려 협동", "친구를 배려하며 협동하는 태도를 보임."),
]

RETRIEVE_CASES = [
    {"area": "seteuk", "query": "그래프 발표", "subject": "수학", "k": 3},
    {"area": "seteuk", "query": "실험 설계", "subject": "과학", "k": 2},
    {"area": "seteuk", "query": "토론 발표", "subject": "", "k": 3},
    {"area": "haengteuk", "query": "책임감 성실", "subject": "", "k": 2},
]
SEED_CASES = [
    {"area": "seteuk", "query": "발표", "subject": "수학", "k": 2},
    {"area": "seteuk", "query": "실험", "subject": "", "k": 2},
]


def main() -> int:
    GOLDEN.mkdir(parents=True, exist_ok=True)
    for f in (DB, Path(str(DB) + "-wal"), Path(str(DB) + "-shm")):
        if f.exists():
            f.unlink()

    store = MemoryStore(db_path=str(DB))
    for area, subj, kw, out in EXAMPLES:
        store.add_example(area=area, subject=subj, keywords=kw, output_text=out)

    # seed 코퍼스: 임시 jsonl 작성 후 load
    with tempfile.NamedTemporaryFile("w", suffix=".jsonl", delete=False, encoding="utf-8") as tf:
        for area, subj, kw, out in SEED:
            tf.write(json.dumps({"area": area, "subject": subj, "keywords": kw,
                                 "output": out}, ensure_ascii=False) + "\n")
        seed_path = tf.name
    store.load_seed_corpus(seed_path)

    retr = []
    for c in RETRIEVE_CASES:
        res = store.retrieve(area=c["area"], query=c["query"], k=c["k"], subject=c["subject"])
        retr.append({**c, "out": [e.output_text for e in res]})

    seedr = []
    for c in SEED_CASES:
        res = store.retrieve_seed(area=c["area"], query=c["query"], k=c["k"], subject=c["subject"])
        seedr.append({**c, "out": [e.output_text for e in res]})

    store.close()  # WAL 체크포인트 → C#가 읽을 수 있게

    out = {"db": "retrieve.sqlite3", "retrieve": retr, "retrieve_seed": seedr}
    (GOLDEN / "golden_retrieve.json").write_text(
        json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
    print("wrote golden_retrieve.json + retrieve.sqlite3")
    print("retrieve cases:", len(retr), "seed cases:", len(seedr))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
