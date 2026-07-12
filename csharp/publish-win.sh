#!/usr/bin/env bash
# 생기부 도우미 Windows 배포 — 메인 앱 + 수업 메모 도구를 한 폴더에 함께 산출.
# 메인 앱 최초 실행 시 옆의 '수업메모.exe'를 자동시작 등록하고 실행한다(Autostart.EnsureMemoInstalled).
#
# 사용:  ./publish-win.sh [출력폴더]   (기본: publish/win-x64)
set -euo pipefail
cd "$(dirname "$0")"
OUT="${1:-publish/win-x64}"
FLAGS=(-c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$OUT")

echo "▶ 메인 앱 publish → $OUT"
dotnet publish Gui/Gui.csproj "${FLAGS[@]}"
echo "▶ 수업 메모 도구 publish → $OUT"
dotnet publish Memo/Memo.csproj "${FLAGS[@]}"

# 한글 exe명으로 리네임(자동시작 탐색은 '수업메모.exe' 우선)
[ -f "$OUT/Memo.exe" ] && mv -f "$OUT/Memo.exe" "$OUT/수업메모.exe"
[ -f "$OUT/Gui.exe" ]  && mv -f "$OUT/Gui.exe"  "$OUT/생기부도우미.exe"

echo "✅ 완료: $OUT"
ls -1 "$OUT"/*.exe 2>/dev/null || true
echo "  · 생기부도우미.exe (메인) — 실행 시 수업메모.exe 자동시작 등록+실행"
echo "  · 수업메모.exe (트레이 상주 · Ctrl+Alt+M)"
echo "  · GGUF 모델은 용량 문제로 미포함 — 앱 옆 models/ 에 두거나 최초 실행 시 다운로드"
