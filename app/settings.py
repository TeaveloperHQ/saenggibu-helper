"""사용자별 영속 설정(활성 모델 등). 앱 설치 위치와 무관하게 사용자 데이터 폴더에 저장.

앱을 수정/재설치해도 이 설정과 학습 데이터는 유지된다.
"""
from __future__ import annotations

import json

from . import config

_PATH = config.DATA_DIR / "settings.json"


def load() -> dict:
    try:
        return json.loads(_PATH.read_text(encoding="utf-8"))
    except (FileNotFoundError, json.JSONDecodeError):
        return {}


def get(key: str, default=None):
    return load().get(key, default)


def set(key: str, value) -> None:  # noqa: A003
    data = load()
    data[key] = value
    _PATH.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
