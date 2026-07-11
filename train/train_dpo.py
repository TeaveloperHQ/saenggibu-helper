"""DPO 선호 학습 (SFT 모델 위에서).

SFT로 학습·병합한 모델을 정책 시작점으로, dpo.jsonl(prompt/chosen/rejected)로
'간결·명사형·주어생략'을 선호하도록 다듬는다. PEFT(LoRA) 사용 → ref 모델은
어댑터 비활성 상태가 자동으로 담당. GPU(T4) 전용.

사용:
  python train/train_dpo.py --model <merged_sft_dir> \
    --data train/data/dpo.jsonl --out train/out/dpo --epochs 1
"""
from __future__ import annotations

import argparse

import torch
from datasets import load_dataset
from peft import LoraConfig
from transformers import AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig
from trl import DPOConfig, DPOTrainer


def parse_args():
    p = argparse.ArgumentParser()
    p.add_argument("--model", required=True)   # 병합된 SFT 모델 경로
    p.add_argument("--data", default="train/data/dpo.jsonl")
    p.add_argument("--out", default="train/out/dpo")
    p.add_argument("--epochs", type=float, default=1.0)
    p.add_argument("--lr", type=float, default=5e-6)
    p.add_argument("--beta", type=float, default=0.1)
    p.add_argument("--batch", type=int, default=1)
    p.add_argument("--grad_accum", type=int, default=8)
    p.add_argument("--lora_r", type=int, default=16)
    p.add_argument("--lora_alpha", type=int, default=32)
    return p.parse_args()


def main():
    a = parse_args()
    if not torch.cuda.is_available():
        raise SystemExit("CUDA not available — refusing to train on CPU")

    bnb = BitsAndBytesConfig(
        load_in_4bit=True,
        bnb_4bit_quant_type="nf4",
        bnb_4bit_compute_dtype=torch.float16,
        bnb_4bit_use_double_quant=True,
    )
    tok = AutoTokenizer.from_pretrained(a.model)
    if tok.pad_token is None:
        tok.pad_token = tok.eos_token

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

    cfg = DPOConfig(
        output_dir=a.out,
        num_train_epochs=a.epochs,
        per_device_train_batch_size=a.batch,
        gradient_accumulation_steps=a.grad_accum,
        learning_rate=a.lr,
        beta=a.beta,
        lr_scheduler_type="cosine",
        warmup_ratio=0.1,
        logging_steps=10,
        save_strategy="no",
        fp16=True,
        bf16=False,
        gradient_checkpointing=True,
        gradient_checkpointing_kwargs={"use_reentrant": False},
        optim="paged_adamw_8bit",
        max_length=768,
        max_prompt_length=512,
        remove_unused_columns=False,
        report_to="none",
    )

    trainer = DPOTrainer(
        model=model,
        ref_model=None,            # PEFT: 어댑터 비활성 = 참조 모델
        args=cfg,
        train_dataset=ds,
        processing_class=tok,
        peft_config=peft_cfg,
    )
    trainer.train()
    trainer.save_model(a.out)
    tok.save_pretrained(a.out)
    print(f"\nDPO 완료. 어댑터 저장: {a.out}")


if __name__ == "__main__":
    main()
