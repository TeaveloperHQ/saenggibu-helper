"""과목별 코퍼스 진행 상태. 순차 학습 로드맵에서 '다음 채울 과목'을 알려준다.

사용: PYTHONPATH=. .venv/bin/python scripts/corpus_status.py
"""
from collections import Counter

from app import config
from app.memory_store import MemoryStore

# 세특 과목 순차 로드맵(핵심 교과 먼저). 과목별 목표 깊이까지 채운 뒤 다음 과목으로.
ROADMAP = [
    "국어", "수학", "영어", "과학", "사회",
    "통합과학", "통합사회", "물리", "화학", "생명과학", "지구과학",
    "한국사", "역사", "도덕", "정보", "기술가정",
    "체육", "음악", "미술", "한문", "일본어", "중국어", "진로와직업", "보건",
]
TARGET = 8  # 과목당 목표 예시 수(측정상 이 정도면 활동형식 커버리지 확보)


def main():
    s = MemoryStore(":memory:")
    s.load_seed_corpus(config.SEED_CORPUS_PATH)
    rows = s._seed_rows_for_area("seteuk")
    cnt = Counter((r.subject or "(미지정)") for r in rows)

    print(f"세특 총 {len(rows)}건 / 목표 과목당 {TARGET}건\n")
    print("과목별 진행:")
    nxt = None
    for subj in ROADMAP:
        c = cnt.get(subj, 0)
        bar = "■" * c + "·" * max(0, TARGET - c)
        done = "✅" if c >= TARGET else "  "
        print(f"  {done} {subj:8} {c:2}/{TARGET}  {bar}")
        if nxt is None and c < TARGET:
            nxt = subj
    # 로드맵 밖 과목도 표시
    extra = {k: v for k, v in cnt.items() if k not in ROADMAP and k != "(미지정)"}
    if extra:
        print("  (로드맵 외):", extra)

    print()
    if nxt:
        print(f">>> 다음 채울 과목: {nxt}  (현재 {cnt.get(nxt,0)} → 목표 {TARGET})")
    else:
        print(">>> 모든 로드맵 과목이 목표 도달 ✅  → 측정 후 포화 판단 / 다음 단계(교사데이터·파인튜닝)")


if __name__ == "__main__":
    main()
