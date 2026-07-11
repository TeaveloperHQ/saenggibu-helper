"""GGUF 모델 다운로드. 인터넷 표준 라이브러리(urllib)만 사용한다.

최초 1회 실행 시 모델이 없으면 받는다. 진행률 콜백을 지원해 GUI 에서 표시한다.
중단 시 .part 파일로 이어받기(resume) 한다.
"""
from __future__ import annotations

import hashlib
import urllib.request
from collections.abc import Callable
from pathlib import Path

from . import config

ProgressCb = Callable[[int, int], None]  # (downloaded_bytes, total_bytes)


def model_exists() -> bool:
    """사용 가능한(동봉 또는 다운로드된) base 모델이 하나라도 있는지."""
    p = config.active_model_path()
    return p is not None and p.exists() and p.stat().st_size > 1_000_000


def download_model(progress: ProgressCb | None = None,
                   should_cancel: Callable[[], bool] | None = None) -> Path:
    dest = config.MODEL_PATH
    if model_exists():
        return dest

    part = dest.with_suffix(dest.suffix + ".part")
    existing = part.stat().st_size if part.exists() else 0

    req = urllib.request.Request(config.MODEL_URL)
    if existing:
        req.add_header("Range", f"bytes={existing}-")

    with urllib.request.urlopen(req, timeout=60) as resp:
        # 서버가 이어받기(206)를 실제로 지원할 때만 append. 200(전체)이면 처음부터
        # 다시 받는다(그러지 않으면 .part 앞에 옛 바이트가 남아 파일이 손상된다).
        resumed = bool(existing) and getattr(resp, "status", 200) == 206
        base = existing if resumed else 0
        total = config.MODEL_APPROX_BYTES
        clen = resp.headers.get("Content-Length")
        if clen:
            total = base + int(clen)

        mode = "ab" if resumed else "wb"
        downloaded = base
        with open(part, mode) as f:
            while True:
                if should_cancel and should_cancel():
                    raise InterruptedError("사용자가 다운로드를 취소했습니다.")
                buf = resp.read(1024 * 256)
                if not buf:
                    break
                f.write(buf)
                downloaded += len(buf)
                if progress:
                    progress(downloaded, total)

    # 무결성 검증: GGUF 매직바이트 + 최소 크기(손상·HTML 오류페이지·중단 탐지)
    if not _looks_like_gguf(part):
        part.unlink(missing_ok=True)
        raise ValueError("다운로드한 파일이 올바른 모델(GGUF)이 아닙니다. 다시 시도해 주세요.")
    min_ok = int(config.MODEL_APPROX_BYTES * 0.9)
    if part.stat().st_size < min_ok:
        raise ValueError("모델 다운로드가 완료되지 않았습니다(파일이 너무 작음). "
                         "다시 눌러 이어받기를 시도해 주세요.")
    if config.MODEL_SHA256 and _sha256(part) != config.MODEL_SHA256:
        part.unlink(missing_ok=True)
        raise ValueError("다운로드한 모델의 무결성 검증에 실패했습니다.")

    part.rename(dest)
    return dest


def _looks_like_gguf(path: Path) -> bool:
    """파일 앞 4바이트가 GGUF 매직인지(손상·오류페이지 탐지)."""
    try:
        with open(path, "rb") as f:
            return f.read(4) == b"GGUF"
    except OSError:
        return False


def _sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()
