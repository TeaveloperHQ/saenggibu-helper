"""코퍼스 효과 A/B 측정. 온도 0(그리디)로 무작위성 제거, few-shot 차이만 비교."""
import os
import tempfile

from app import config
from app.engine import LlamaEngine
from app.memory_store import MemoryStore
from app.prompts import AREA_BY_KEY

# 두 종류 store: 코퍼스 없음 / 코퍼스 있음
store_empty = MemoryStore(os.path.join(tempfile.mkdtemp(), "empty.sqlite3"))
store_corpus = MemoryStore(os.path.join(tempfile.mkdtemp(), "corpus.sqlite3"))
store_corpus.load_seed_corpus(config.SEED_CORPUS_PATH)

eng = LlamaEngine(store_corpus)

INPUTS = [
    ("흔한 케이스", "seteuk", "수학", "이차함수 그래프 발표 / 오개념 스스로 점검 / 친구에게 풀이 설명"),
    ("희귀 케이스", "seteuk", "국악", "국악 장단 익히기 / 모둠 연주 / 발표"),
]


def gen(store, k, subject, keywords, area):
    eng._store = store
    old = config.SEED_FEWSHOT_K
    config.SEED_FEWSHOT_K = k
    try:
        retrieved = store.retrieve_seed(area=area.key, query=f"{subject} {keywords}", k=k) if k else []
        out = eng.generate(area, subject=subject, keywords=keywords,
                           n_variations=1, temperature=0.0, max_tokens=256)
    finally:
        config.SEED_FEWSHOT_K = old
    return retrieved, out.strip()


for label, akey, subject, keywords in INPUTS:
    area = AREA_BY_KEY[akey]
    print("=" * 70)
    print(f"[{label}] 과목={subject} / 키워드={keywords}")
    print("=" * 70)

    conditions = [
        ("① 코퍼스 없음", store_empty, 0),
        ("② 코퍼스 K=2(현재)", store_corpus, 2),
        ("③ 코퍼스 K=4", store_corpus, 4),
    ]
    for name, store, k in conditions:
        retrieved, out = gen(store, k, subject, keywords, area)
        print(f"\n--- {name} ---")
        if retrieved:
            print("  주입된 예시:", " | ".join(r.keywords for r in retrieved))
        print("  출력:", out)
    print()

store_empty.close()
store_corpus.close()
print("측정 완료")
