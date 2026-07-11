"""LoRA 어댑터를 베이스 모델에 머지해 단일 모델로 저장(이후 GGUF 변환용).

사용: python train/merge_lora.py --adapter train/out/saenggibu-lora --out train/out/merged
"""
from __future__ import annotations

import argparse

import torch
from peft import PeftModel
from transformers import AutoModelForCausalLM, AutoTokenizer

BASE_MODEL = "Qwen/Qwen2.5-7B-Instruct"


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--base", default=BASE_MODEL)
    p.add_argument("--adapter", default="train/out/saenggibu-lora")
    p.add_argument("--out", default="train/out/merged")
    a = p.parse_args()

    print("베이스 로딩(fp16)…")
    base = AutoModelForCausalLM.from_pretrained(
        a.base, torch_dtype=torch.float16, device_map="cpu"
    )
    print("어댑터 결합·머지…")
    model = PeftModel.from_pretrained(base, a.adapter)
    model = model.merge_and_unload()
    model.save_pretrained(a.out, safe_serialization=True)
    AutoTokenizer.from_pretrained(a.base).save_pretrained(a.out)
    print(f"머지 완료: {a.out}  → 이후 llama.cpp로 GGUF 변환/양자화")


if __name__ == "__main__":
    main()
