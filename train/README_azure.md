# Azure에서 Qwen2.5-7B QLoRA 학습 (생기부 도메인)

목표: 누적 코퍼스로 Qwen2.5-7B-Instruct(Apache-2.0)를 LoRA 파인튜닝 →
머지 → GGUF(Q4_K_M) → `LLLM/models/`에 넣으면 앱이 자동 인식.

**학습은 평생 1회, 빌드 단계에서만.** 교사 PC는 추론(CPU)만 함.

---

## 0. 사전 조건 (이미 확인됨)
- Azure 로그인됨 / 구독 = **종량제(PayAsYouGo)** ✅
- **T4 쿼터 증설(NCASv3_T4 = 0→4) 요청 제출됨** → 승인(InProgress→Succeeded) 후 진행
  ```bash
  SUB=$(az account show --query id -o tsv)
  az vm list-usage -l koreacentral \
    --query "[?contains(name.value,'NCAS') && contains(name.value,'T4')].{Used:currentValue,Limit:limit}" -o table
  # Limit 4 로 바뀌면 진행
  ```

## 1. VM 생성 (T4 1장, CUDA 사전설치된 Data Science VM)
```bash
RG=saenggibu-train
LOC=koreacentral
VM=t4train
az group create -n $RG -l $LOC

az vm create -g $RG -n $VM \
  --image microsoft-dsvm:ubuntu-hpc:2204:latest \
  --size Standard_NC4as_T4_v3 \
  --admin-username azureuser --generate-ssh-keys \
  --os-disk-size-gb 128 --public-ip-sku Standard
# 접속
ssh azureuser@$(az vm show -d -g $RG -n $VM --query publicIps -o tsv)
```
> 대안: 더 빠른 A10(`Standard_NV6ads_A10_v5`/`NVadsA10v5` 쿼터), 또는 Azure ML 컴퓨트 인스턴스(T4)에 노트북.

## 2. 코드·데이터 업로드 (로컬에서)
```bash
IP=$(az vm show -d -g $RG -n $VM --query publicIps -o tsv)
scp -r train assets app azureuser@$IP:~/saenggibu/
```
> `assets/seed_corpus.jsonl`, `app/`(프롬프트), `train/`(스크립트·sft.jsonl) 전부 필요.

## 3. 환경 설치 (VM에서)
```bash
cd ~/saenggibu
python3 -m venv .venv && source .venv/bin/activate
pip install -U pip
pip install -r train/requirements-train.txt
nvidia-smi   # T4 보이는지 확인
```

## 4. (선택) 데이터셋 최신화 + 학습
```bash
# 코퍼스를 그새 더 채웠다면 다시 빌드(아니면 업로드된 sft.jsonl 사용)
PYTHONPATH=. python train/build_dataset.py            # → train/data/sft.jsonl
# 학습 (T4 기준 약 20~40분)
PYTHONPATH=. python train/train_qlora.py \
  --data train/data/sft.jsonl --out train/out/saenggibu-lora --epochs 3
```
Qwen2.5-7B-Instruct는 게이트 없음(다운로드에 HF 로그인 불필요).

## 5. 머지 + GGUF 변환·양자화
```bash
# 5-1. 어댑터를 베이스에 머지
PYTHONPATH=. python train/merge_lora.py \
  --adapter train/out/saenggibu-lora --out train/out/merged

# 5-2. llama.cpp로 GGUF 변환 + Q4_K_M 양자화
cd ~ && git clone https://github.com/ggerganov/llama.cpp
cd llama.cpp && pip install -r requirements.txt
cmake -B build && cmake --build build -j --target llama-quantize
python convert_hf_to_gguf.py ~/saenggibu/train/out/merged \
  --outfile ~/saenggibu-qwen2.5-7b-f16.gguf --outtype f16
./build/bin/llama-quantize ~/saenggibu-qwen2.5-7b-f16.gguf \
  ~/saenggibu-qwen2.5-7b-instruct-q4_k_m.gguf Q4_K_M
```

## 6. 결과 GGUF 회수 (로컬에서)
```bash
scp azureuser@$IP:~/saenggibu-qwen2.5-7b-instruct-q4_k_m.gguf \
  "$HOME/.local/share/saenggibu-helper/models/"
# 앱 재시작 → '기본 모델' 드롭다운에서 새 모델 선택
```
사용자 데이터 폴더(models/)에 넣으면 config.list_models()가 자동 인식.

## 7. ⚠️ 과금 정지 (반드시!)
```bash
az group delete -n $RG --yes --no-wait     # VM·디스크·IP 전부 삭제
# 또는 잠깐 멈춤만:  az vm deallocate -g $RG -n $VM
```
T4 종량제 ~$0.5–0.6/hr. 학습 1회 1시간 미만이면 **$1 미만**.

---

## 배포(teaveloper)
- 만들어진 `saenggibu-qwen2.5-7b-instruct-q4_k_m.gguf`(Apache-2.0 파생)를
  기존 Qwen GGUF 대신 배포물 `models/`에 넣거나 다운로드 URL 교체.
- 교사 PC는 그대로 CPU 추론. 라이선스 자유(Apache-2.0) 유지.

## 재학습(데이터 늘었을 때)
코퍼스/교사데이터가 쌓이면 1·2단계 없이 같은 VM에서 4~6단계만 반복.
`--include-db` 로 교사 채택 데이터까지 학습에 포함 가능.
