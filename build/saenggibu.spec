# -*- mode: python ; coding: utf-8 -*-
"""PyInstaller 스펙. Windows 에서 빌드하면 두 개의 exe 를 한 폴더에 만든다.

  dist/생기부도우미/
      생기부도우미.exe   ← 메인 앱(아이콘 클릭으로 실행). run.py
      수업메모.exe        ← 트레이 상주 메모 도구(부팅 시 자동 실행). quicknote.py

  두 exe 는 공통 라이브러리(PySide6 등)를 MERGE 로 공유해 용량 중복을 없앤다.
  메모 도구는 모델(llama_cpp)이 필요 없어 가볍게 뜬다.

  주의: PyInstaller 는 '대상 OS' 에서 빌드해야 한다. Windows exe 는 반드시
        Windows 에서 빌드한다.

  사용:  (Windows, 프로젝트 루트에서)
      python -m pip install -r requirements.txt pyinstaller
      pyinstaller build/saenggibu.spec --noconfirm

  모델(GGUF)은 용량이 커서 exe 에 포함하지 않는다. 빌드 산출물 옆 models/ 에
  두거나, 최초 실행 시 앱이 내려받는다.
"""
from PyInstaller.utils.hooks import collect_dynamic_libs, collect_submodules

block_cipher = None

# --- 메인 앱: llama_cpp 네이티브 .dll/.so 와 서브모듈을 빠짐없이 포함 ---
binaries = collect_dynamic_libs("llama_cpp")
hiddenimports = collect_submodules("llama_cpp") + collect_submodules("PySide6")

main = Analysis(
    ["../run.py"],
    pathex=["."],
    binaries=binaries,
    datas=[
        ("../assets/seed_corpus.jsonl", "assets"),
        ("../app/assets", "app/assets"),      # 브랜드 아이콘·엠블럼(SVG/ICO/PNG)
    ],
    hiddenimports=hiddenimports,
    hookspath=[],
    runtime_hooks=[],
    excludes=["tkinter", "matplotlib", "numpy.tests"],
    cipher=block_cipher,
)

# --- 메모 도구: 모델 불필요(가벼움). 아이콘 렌더용 app/assets 만 포함 ---
memo = Analysis(
    ["../quicknote.py"],
    pathex=["."],
    binaries=[],
    datas=[("../app/assets", "app/assets")],
    hiddenimports=collect_submodules("PySide6"),
    hookspath=[],
    runtime_hooks=[],
    excludes=["tkinter", "matplotlib", "numpy.tests", "llama_cpp"],
    cipher=block_cipher,
)

# 공통 의존성은 메인에서 한 번만 담고 메모는 참조만(용량 중복 제거)
MERGE((main, "run", "생기부도우미"), (memo, "quicknote", "수업메모"))

pyz_main = PYZ(main.pure, main.zipped_data, cipher=block_cipher)
pyz_memo = PYZ(memo.pure, memo.zipped_data, cipher=block_cipher)

exe_main = EXE(
    pyz_main,
    main.scripts,
    [],
    exclude_binaries=True,
    name="생기부도우미",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,            # llama_cpp DLL UPX 압축은 깨질 수 있어 비활성
    console=False,        # GUI 앱: 콘솔창 숨김
    icon="../app/assets/app.ico",    # 💬 말풍선 브랜드 아이콘
)

exe_memo = EXE(
    pyz_memo,
    memo.scripts,
    [],
    exclude_binaries=True,
    name="수업메모",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=False,        # 트레이 상주: 콘솔창 숨김
    icon="../app/assets/memo.ico",   # 메모지 브랜드 아이콘(메인과 구분)
)

coll = COLLECT(
    exe_main, main.binaries, main.zipfiles, main.datas,
    exe_memo, memo.binaries, memo.zipfiles, memo.datas,
    strip=False,
    upx=False,
    name="생기부도우미",
)
