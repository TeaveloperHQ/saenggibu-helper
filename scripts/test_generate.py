"""실모델 생성 E2E 테스트 (CLI). `.venv/bin/python scripts/test_generate.py`"""
import os
import tempfile
import time

from app.engine import LlamaEngine
from app.memory_store import MemoryStore
from app.prompts import AREA_BY_KEY


def main():
    # 콜드스타트(교사 예시 0건) 경로를 검증하기 위해 매번 빈 임시 DB 사용
    store = MemoryStore(os.path.join(tempfile.mkdtemp(), "test.sqlite3"))
    engine = LlamaEngine(store)

    area = AREA_BY_KEY["seteuk"]
    params = dict(
        subject="수학",
        keywords="일차함수 단원 / 기울기와 절편의 의미를 그래프로 발표 / "
                 "자신의 오개념을 스스로 찾아 수정 / 친구 질문에 풀이 과정을 차근차근 설명",
        tone="구체적 사례 중심",
        length_hint="보통(3~4문장)",
    )

    print("=== 모델 로딩 ===", flush=True)
    t0 = time.time()
    engine.ensure_loaded(progress=lambda m: print("  ", m, flush=True))
    print(f"로딩 {time.time()-t0:.1f}s\n", flush=True)

    print("=== 세특 생성 (스트리밍) ===", flush=True)
    t1 = time.time()
    out = []
    for piece in engine.generate_stream(area, **params):
        out.append(piece)
        print(piece, end="", flush=True)
    text = "".join(out)
    dt = time.time() - t1
    print(f"\n\n--- {len(text)}자 / {dt:.1f}s / {len(text)/max(dt,1):.1f}자per초 ---", flush=True)

    # 학습 효과 확인: 위 결과를 채택 저장 후 재검색되는지
    store.add_example(area=area.key, subject="수학", keywords=params["keywords"], output_text=text)
    hits = store.retrieve(area="seteuk", query="이차함수 그래프 발표", k=1)
    print("학습 저장 후 재검색 OK:", bool(hits), flush=True)
    store.close()


if __name__ == "__main__":
    main()
