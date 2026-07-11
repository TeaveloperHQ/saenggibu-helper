"""앱 전역 설정. 모든 경로·모델 사양·LLM 파라미터를 여기서 관리한다.

배포(exe) 환경과 개발 환경 모두에서 동작하도록 경로를 동적으로 결정한다.
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

APP_NAME = "생기부 도우미"
APP_VERSION = "0.1.0"

# ----------------------------------------------------------------------------
# 경로
# ----------------------------------------------------------------------------
def _base_dir() -> Path:
    """실행 파일/스크립트 기준 디렉터리.

    PyInstaller 로 묶이면 sys.frozen 이 True 이고 실행 파일 옆 경로를 쓴다.
    """
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent.parent


def _data_dir() -> Path:
    """사용자별 데이터(학습 DB, 설정) 저장 위치.

    Windows: %LOCALAPPDATA%/생기부도우미
    그 외:   ~/.local/share/saenggibu-helper
    """
    if os.name == "nt":
        root = Path(os.environ.get("LOCALAPPDATA", Path.home() / "AppData" / "Local"))
        return root / "SaenggibuHelper"
    return Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local" / "share")) / "saenggibu-helper"


BASE_DIR = _base_dir()
DATA_DIR = _data_dir()
# 모델은 사용자 데이터 폴더에 둔다 → 앱을 수정/재설치해도 다시 받을 필요 없음(영속).
MODELS_DIR = DATA_DIR / "models"
# 배포물에 모델을 동봉(오프라인 설치본)한 경우 실행 파일 옆 models/ 도 함께 탐색한다.
BUNDLED_MODELS_DIR = BASE_DIR / "models"
DB_PATH = DATA_DIR / "memory.sqlite3"     # 교사별 학습 데이터(영속)

DATA_DIR.mkdir(parents=True, exist_ok=True)
MODELS_DIR.mkdir(parents=True, exist_ok=True)

# ----------------------------------------------------------------------------
# 모델 사양
# ----------------------------------------------------------------------------
# Qwen2.5-7B-Instruct Q4_K_M (Apache-2.0). 단일 파일 GGUF.
MODEL_FILENAME = "qwen2.5-7b-instruct-q4_k_m.gguf"
MODEL_URL = (
    "https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/"
    "Qwen2.5-7B-Instruct-Q4_K_M.gguf?download=true"
)
MODEL_SHA256 = ""  # 선택: 무결성 검증용. 비우면 검사 생략.
MODEL_APPROX_BYTES = 4_683_073_344  # 진행률 표시용 근사치(~4.7GB)

MODEL_PATH = MODELS_DIR / MODEL_FILENAME   # 기본 모델 다운로드 대상(영속 폴더)


def list_models() -> dict[str, "Path"]:
    """사용 가능한 GGUF 모델 {파일명: 경로}. 사용자 폴더가 동봉본보다 우선."""
    found: dict[str, Path] = {}
    for d in (BUNDLED_MODELS_DIR, MODELS_DIR):  # 뒤(사용자 폴더)가 우선되도록
        if d.exists():
            for p in sorted(d.glob("*.gguf")):
                found[p.name] = p
    return found


def resolve_model(filename: str) -> "Path | None":
    return list_models().get(filename)


def active_model_path() -> "Path | None":
    """현재 선택된 base 모델 경로. 없으면 기본 → 첫 번째 사용 가능 모델 순으로."""
    from . import settings  # 지연 import(순환 방지)

    models = list_models()
    chosen = settings.get("active_model")
    if chosen and chosen in models:
        return models[chosen]
    if MODEL_FILENAME in models:
        return models[MODEL_FILENAME]
    return next(iter(models.values()), None)

# ----------------------------------------------------------------------------
# LLM 파라미터 (램 8GB / CPU i5 10세대 기준 보수적 설정)
# ----------------------------------------------------------------------------
N_CTX = int(os.environ.get("SGB_N_CTX", "4096"))      # 컨텍스트 길이
N_THREADS = int(os.environ.get("SGB_N_THREADS", str(max(2, (os.cpu_count() or 4) - 1))))
N_BATCH = int(os.environ.get("SGB_N_BATCH", "256"))
N_GPU_LAYERS = int(os.environ.get("SGB_N_GPU_LAYERS", "0"))  # 기본 CPU 전용
USE_MMAP = True
USE_MLOCK = False  # 8GB 환경에서는 mlock 끄는 게 안전

# 생성 기본값
DEFAULT_TEMPERATURE = 0.7
DEFAULT_TOP_P = 0.9
DEFAULT_MAX_TOKENS = 768

# few-shot 으로 주입할 과거(교사 본인) 예시 개수
FEWSHOT_K = 3
# 추가로 주입할 내장 씨드 코퍼스(문장 형식) 예시 개수
SEED_FEWSHOT_K = 2

# 변형 생성 시 모델이 직접 만드는 기본 문장 수(나머지는 어순 재배열·동의어로 조합 확장)
VARIATION_BASE_MAX = 5
# 변형 기본 생성 온도(작은 Q4 모델은 ≥0.85에서 언어 전환·붕괴 → 안전 범위로 제한)
VARIATION_TEMPS = (0.6, 0.7, 0.8)

# 내장 씨드 코퍼스(문장 형식 학습용). 앱과 함께 배포.
SEED_CORPUS_PATH = BASE_DIR / "assets" / "seed_corpus.jsonl"
