"""Qwen2.5-7B-Instruct QLoRA 파인튜닝 (생기부 도메인).

GPU(T4 16GB 이상)에서 실행. CPU 불가(bitsandbytes 4비트=CUDA 전용).
데이터: train/data/sft.jsonl ({"messages":[...]})

사용:
  python train/train_qlora.py \
    --data train/data/sft.jsonl --out train/out/saenggibu-lora
"""
from __future__ import annotations

import argparse

import torch
from datasets import load_dataset
from peft import LoraConfig
from transformers import AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig
from trl import SFTConfig, SFTTrainer

BASE_MODEL = "Qwen/Qwen2.5-7B-Instruct"  # Apache-2.0


def parse_args():
    p = argparse.ArgumentParser()
    p.add_argument("--model", default=BASE_MODEL)
    p.add_argument("--data", default="train/data/sft.jsonl")
    p.add_argument("--out", default="train/out/saenggibu-lora")
    p.add_argument("--epochs", type=float, default=3.0)
    p.add_argument("--lr", type=float, default=2e-4)
    p.add_argument("--batch", type=int, default=1)
    p.add_argument("--grad_accum", type=int, default=16)
    p.add_argument("--max_seq_len", type=int, default=512)
    p.add_argument("--lora_r", type=int, default=16)
    p.add_argument("--lora_alpha", type=int, default=32)
    return p.parse_args()


def main():
    a = parse_args()

    # T4(Turing)는 bf16 하드웨어 미지원 → fp16 사용(에뮬레이션 회피로 ~4배 빠름)
    bnb = BitsAndBytesConfig(
        load_in_4bit=True,
        bnb_4bit_quant_type="nf4",
        bnb_4bit_compute_dtype=torch.float16,
        bnb_4bit_use_double_quant=True,
    )
    tok = AutoTokenizer.from_pretrained(a.model)
    if tok.pad_token is None:
        tok.pad_token = tok.eos_token

    # device_map="auto"는 accelerate가 GPU 배치에 실패하면 CPU로 떨어져
    # bitsandbytes가 CPU 백엔드로 학습(매우 느림). 명시적으로 GPU0에 강제 배치.
    if not torch.cuda.is_available():
        raise SystemExit("CUDA not available — refusing to train on CPU")
    model = AutoModelForCausalLM.from_pretrained(
        a.model, quantization_config=bnb, torch_dtype=torch.float16,
        device_map={"": 0},
    )
    model.config.use_cache = False

    peft_cfg = LoraConfig(
        r=a.lora_r, lora_alpha=a.lora_alpha, lora_dropout=0.05, bias="none",
        task_type="CAUSAL_LM",
        target_modules=["q_proj", "k_proj", "v_proj", "o_proj",
                        "gate_proj", "up_proj", "down_proj"],
    )

    ds = load_dataset("json", data_files=a.data, split="train")

    # messages → 채팅 템플릿 렌더링한 'text' 컬럼 (trl 버전 무관 안전)
    def to_text(ex):
        return {"text": tok.apply_chat_template(ex["messages"], tokenize=False)}

    ds = ds.map(to_text, remove_columns=ds.column_names)

    cfg = SFTConfig(
        output_dir=a.out,
        num_train_epochs=a.epochs,
        per_device_train_batch_size=a.batch,
        gradient_accumulation_steps=a.grad_accum,
        learning_rate=a.lr,
        lr_scheduler_type="cosine",
        warmup_ratio=0.05,
        logging_steps=10,
        save_strategy="epoch",
        fp16=True,
        gradient_checkpointing=True,
        gradient_checkpointing_kwargs={"use_reentrant": False},
        optim="paged_adamw_8bit",
        max_seq_length=a.max_seq_len,
        dataset_text_field="text",
        packing=True,
        report_to="none",
    )

    trainer = SFTTrainer(
        model=model,
        args=cfg,
        train_dataset=ds,
        peft_config=peft_cfg,
        processing_class=tok,
    )
    trainer.train()
    trainer.save_model(a.out)
    tok.save_pretrained(a.out)
    print(f"\n완료. LoRA 어댑터 저장: {a.out}")


if __name__ == "__main__":
    main()
