"""SFT 코퍼스(natural.jsonl) → DPO 선호 데이터(dpo.jsonl).

각 줄: {"prompt":[system,user], "chosen":[assistant], "rejected":[assistant]}
chosen = 간결·자연·명사형 정답(코퍼스).
rejected = 실패 모드로 변형:
  (1) 반복·장황  (2) 평서형 종결(명사형 규칙 위반)  (3) 주어 노출 + 평서형
DPO가 '간결·명사형·주어생략'을 선호하도록 학습시킨다.

사용: PYTHONPATH=. python train/build_dpo.py --source train/data/natural.jsonl --out train/data/dpo.jsonl
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from app.prompts import AREA_BY_KEY, build_user_prompt  # noqa: E402

# 명사형 → 평서형(마지막 어절). 규칙 위반 rejected 생성용.
PYUNG = {
    "보임": "보였다", "드러냄": "드러냈다", "함": "하였다", "임": "이었다", "됨": "되었다",
    "기름": "길렀다", "키움": "키웠다", "넓힘": "넓혔다", "지님": "지녔다", "이끔": "이끌었다",
    "얻음": "얻었다", "쌓음": "쌓았다", "느낌": "느꼈다", "높임": "높였다", "세움": "세웠다",
    "다짐": "다짐하였다", "앎": "알았다", "맞음": "맞았다", "삼음": "삼았다", "줌": "주었다",
}
SUF = [  # 접미 규칙(긴 어절 우선)
    ("경험함", "경험하였다"), ("체득함", "체득하였다"), ("성장함", "성장하였다"),
    ("마련함", "마련하였다"), ("발휘함", "발휘하였다"), ("완성함", "완성하였다"),
    ("모색함", "모색하였다"), ("제안함", "제안하였다"), ("탐색함", "탐색하였다"),
    ("설계함", "설계하였다"), ("실천함", "실천하였다"), ("함양함", "함양하였다"),
    ("구체화함", "구체화하였다"), ("보완함", "보완하였다"), ("정리함", "정리하였다"),
    ("분석함", "분석하였다"), ("수행함", "수행하였다"), ("완수함", "완수하였다"),
    ("성공함", "성공하였다"), ("증명함", "증명하였다"), ("터득함", "터득하였다"),
    ("이해함", "이해하였다"), ("함", "하였다"), ("임", "이었다"), ("됨", "되었다"),
    ("힘", "혔다"), ("움", "웠다"), ("름", "렀다"), ("님", "녔다"), ("냄", "냈다"),
]


def to_pyungseo(text: str) -> str:
    core = text.rstrip(".").rstrip()
    words = core.split()
    if not words:
        return text
    last = words[-1]
    if last in PYUNG:
        words[-1] = PYUNG[last]
        return " ".join(words) + "."
    for suf, rep in SUF:
        if last.endswith(suf):
            words[-1] = last[: -len(suf)] + rep
            return " ".join(words) + "."
    return core + "였다."


def rej_repetitive(text: str) -> str:
    core = text.rstrip(".").rstrip()
    # 같은 내용을 늘어지게 반복(반복·장황 negative)
    return (core + ". 이와 같은 모습을 다양한 활동에서 꾸준히 반복적으로 보이며, "
            + core + ".")


def rej_verbose(text: str) -> str:
    core = text.rstrip(".").rstrip()
    return ("여러 가지 다양한 활동을 두루 거치는 가운데 " + core
            + ", 그리고 이러한 태도를 여러 상황에서 거듭 보이며 같은 역량을 반복해 발휘함.")


def main() -> None:
    src = Path(sys.argv[sys.argv.index("--source") + 1]) if "--source" in sys.argv \
        else ROOT / "train" / "data" / "natural.jsonl"
    out = Path(sys.argv[sys.argv.index("--out") + 1]) if "--out" in sys.argv \
        else ROOT / "train" / "data" / "dpo.jsonl"

    pairs = []
    for line in src.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line:
            continue
        o = json.loads(line)
        area = AREA_BY_KEY.get(o.get("area", ""))
        if area is None:
            continue
        good = o["output"].strip()
        sysmsg = area.system_prompt()
        user = build_user_prompt(area, subject=o.get("subject", ""),
                                 keywords=o.get("keywords", ""), tone="", length_hint="")
        prompt = [{"role": "system", "content": sysmsg},
                  {"role": "user", "content": user}]
        # 실패 모드별 rejected (반복/장황은 핵심 이슈라 2종, 규칙위반 2종)
        rejected = [
            rej_repetitive(good),
            rej_verbose(good),
            to_pyungseo(good),
            "이 학생은 " + to_pyungseo(good),
        ]
        for rj in rejected:
            if rj.strip() and rj.strip() != good:
                pairs.append({
                    "prompt": prompt,
                    "chosen": [{"role": "assistant", "content": good}],
                    "rejected": [{"role": "assistant", "content": rj.strip()}],
                })

    with open(out, "w", encoding="utf-8") as f:
        for p in pairs:
            f.write(json.dumps(p, ensure_ascii=False) + "\n")
    print(f"DPO 쌍 {len(pairs)}개 저장: {out}")
    # 샘플
    s = pairs[0]
    print("\n[샘플]")
    print(" chosen :", s["chosen"][0]["content"])
    print(" reject1:", pairs[0]["rejected"][0]["content"][:80])
    print(" reject2:", pairs[2]["rejected"][0]["content"][:80] if len(pairs) > 2 else "")


if __name__ == "__main__":
    main()
