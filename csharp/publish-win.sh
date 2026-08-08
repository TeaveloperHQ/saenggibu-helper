#!/usr/bin/env bash
# 생기부 도우미 Windows 배포 — 교사가 클릭 한 번에 쓰도록 필요한 모든 것을 한 폴더로 묶는다.
#   ① 메인 앱 + 수업 메모 도구(self-contained exe) — .NET 런타임 불필요
#   ② Kiwi 네이티브(kiwi.dll) + Kiwi 모델(kiwi_model/) — 형태소 분석
#   ③ seed_corpus.jsonl — 문장 형식 씨드
#   ④ (선택) GGUF LLM 모델 — INCLUDE_GGUF=/경로/model.gguf 주면 동봉(완전 오프라인 패키지),
#      안 주면 앱이 최초 실행 시 학습 모드에서 다운로드
#
# 앱은 exe 옆(ProcessPath 폴더)에서 kiwi.dll·kiwi_model·seed_corpus.jsonl·models/ 를
# 자동으로 찾는다(SGB_* 환경변수 불필요).
#
# 사용:  ./publish-win.sh [출력폴더]            (기본: publish/win-x64)
#        INCLUDE_GGUF=~/models/x.gguf ./publish-win.sh
set -euo pipefail
cd "$(dirname "$0")"
OUT="${1:-publish/win-x64}"
KIWI_VER="0.23.2"
FLAGS=(-c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$OUT")

echo "▶ 메인 앱 publish → $OUT"
dotnet publish Gui/Gui.csproj "${FLAGS[@]}"
echo "▶ 수업 메모 도구 publish → $OUT"
dotnet publish Memo/Memo.csproj "${FLAGS[@]}"

# 한글 exe명으로 리네임(자동시작 탐색은 '수업메모.exe' 우선)
[ -f "$OUT/Memo.exe" ] && mv -f "$OUT/Memo.exe" "$OUT/수업메모.exe"
[ -f "$OUT/Gui.exe" ]  && mv -f "$OUT/Gui.exe"  "$OUT/생기부도우미.exe"

# ── ② Kiwi 네이티브(win-x64 kiwi.dll) ──
echo "▶ Kiwi 네이티브(kiwi.dll v${KIWI_VER}) 동봉"
TMP="$(mktemp -d)"
curl -sL -o "$TMP/kiwi.zip" \
  "https://github.com/bab2min/Kiwi/releases/download/v${KIWI_VER}/kiwi_win_x64_v${KIWI_VER}.zip"
unzip -oq "$TMP/kiwi.zip" -d "$TMP/kiwi"
DLL="$(find "$TMP/kiwi" -name 'kiwi.dll' | head -1)"
[ -n "$DLL" ] && cp -f "$DLL" "$OUT/kiwi.dll" && echo "  · kiwi.dll" || echo "  ! kiwi.dll 못 찾음(수동 확인 필요)"
rm -rf "$TMP"

# ── ② Kiwi 모델 — 미동봉. 앱이 최초 실행 시 우리 저장소(핀 고정 SAS)에서 받아 데이터폴더에 푼다.
echo "▶ Kiwi 모델 미동봉 — 앱이 최초 실행 시 다운로드(Config.KiwiModelUrl)"

# ── ③ seed_corpus ──
cp -f ../assets/seed_corpus.jsonl "$OUT/seed_corpus.jsonl" && echo "▶ seed_corpus.jsonl 동봉"

# ── ④ (선택) GGUF ──
if [ -n "${INCLUDE_GGUF:-}" ] && [ -f "$INCLUDE_GGUF" ]; then
  mkdir -p "$OUT/models"; cp -f "$INCLUDE_GGUF" "$OUT/models/"
  echo "▶ GGUF 동봉 → models/$(basename "$INCLUDE_GGUF")  ($(du -sh "$INCLUDE_GGUF" | cut -f1)) — 완전 오프라인 패키지"
else
  echo "▶ GGUF 미동봉 — 앱 최초 실행 시 학습 모드에서 다운로드(INCLUDE_GGUF=경로 로 동봉 가능)"
fi

echo ""
echo "✅ 완료: $OUT"
ls -1 "$OUT" | sed 's/^/   /'
echo ""
echo "교사 배포: 이 폴더를 zip으로 묶어 제공 → 압축 풀고 '생기부도우미.exe' 실행."
