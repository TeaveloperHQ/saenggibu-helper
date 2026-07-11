"""과목별 학습 효과 측정. 같은 입력을 (코퍼스 없음) vs (과목 코퍼스)로 그리디 생성해
그 과목 고유 서술어가 실제로 반영되는지 비교한다."""
import os
import tempfile

from app import config
from app.engine import LlamaEngine
from app.memory_store import MemoryStore
from app.prompts import AREA_BY_KEY

store_empty = MemoryStore(os.path.join(tempfile.mkdtemp(), "e.sqlite3"))
store_corpus = MemoryStore(os.path.join(tempfile.mkdtemp(), "c.sqlite3"))
store_corpus.load_seed_corpus(config.SEED_CORPUS_PATH)
eng = LlamaEngine(store_corpus)
area = AREA_BY_KEY["seteuk"]

# (과목, 키워드, 기대되는 과목 서술어)
CASES = [
    ("화학", "산 염기 반응 / 지시약 / 결과", ["관찰", "측정", "검증", "분석"]),
    ("체육", "농구 경기 / 팀 전략 / 역할", ["협력", "맞춤", "함께"]),
    ("역사", "조선 후기 사회 / 자료 / 정리", ["탐구", "비교", "평가", "정리"]),
]


def gen(store, k, subject, keywords):
    eng._store = store
    old = config.SEED_FEWSHOT_K
    config.SEED_FEWSHOT_K = k
    try:
        out = eng.generate(area, subject=subject, keywords=keywords,
                           n_variations=1, temperature=0.0, max_tokens=200)
    finally:
        config.SEED_FEWSHOT_K = old
    return out.strip()


for subject, keywords, expect in CASES:
    print("=" * 66)
    print(f"[{subject}] {keywords}   기대 서술어: {expect}")
    a = gen(store_empty, 0, subject, keywords)
    b = gen(store_corpus, 2, subject, keywords)
    ha = sum(w in a for w in expect)
    hb = sum(w in b for w in expect)
    print(f"  ① 코퍼스 없음 (기대어 {ha}/{len(expect)}): {a}")
    print(f"  ② 과목 코퍼스 (기대어 {hb}/{len(expect)}): {b}")
print("측정 완료")
