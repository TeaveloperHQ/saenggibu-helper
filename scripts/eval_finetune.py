"""파인튜닝 모델 vs 기존 Qwen 한국어 품질 비교(그리디).

같은 생기부 입력을 두 GGUF로 생성해 어색함·서술어·형식을 눈으로 비교한다.
사용: PYTHONPATH=. .venv/bin/python scripts/eval_finetune.py <base.gguf> <finetuned.gguf>
"""
import sys

from llama_cpp import Llama

from app.prompts import AREA_BY_KEY, build_user_prompt

CASES = [
    ("seteuk", "과학", "광합성 실험 / 변인 통제 / 결과 분석 / 모둠 발표"),
    ("seteuk", "국어", "토론 / 근거 제시 / 경청 / 자기 의견 정리"),
    ("haengteuk", "", "책임감 / 1인1역 끝까지 / 친구 배려 / 2학기 적극적"),
]


def load(path):
    return Llama(model_path=path, n_ctx=2048, n_threads=6, verbose=False)


def gen(llm, area, subject, keywords):
    msgs = [
        {"role": "system", "content": area.system_prompt()},
        {"role": "user", "content": build_user_prompt(
            area, subject=subject, keywords=keywords, tone="", length_hint="")},
    ]
    out = llm.create_chat_completion(messages=msgs, temperature=0.0, max_tokens=200)
    return out["choices"][0]["message"]["content"].strip()


def main():
    base_path, ft_path = sys.argv[1], sys.argv[2]
    print("기존 Qwen 로딩…"); base = load(base_path)
    print("파인튜닝 로딩…"); ft = load(ft_path)
    for akey, subject, kw in CASES:
        area = AREA_BY_KEY[akey]
        print("\n" + "=" * 70)
        print(f"[{area.title}] 과목={subject or '-'} / {kw}")
        print("-" * 70)
        print("기존  :", gen(base, area, subject, kw))
        print("파튜닝:", gen(ft, area, subject, kw))


if __name__ == "__main__":
    main()
