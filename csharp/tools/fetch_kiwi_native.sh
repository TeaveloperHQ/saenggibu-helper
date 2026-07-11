#!/usr/bin/env bash
# Kiwi C-API 네이티브 라이브러리(libkiwi) 다운로드 — kiwipiepy와 동일 버전(0.23.2)이라
# 형태소·태그가 exact 일치. 모델은 pip의 kiwipiepy_model 을 그대로 쓴다(중복 다운로드 불필요).
#
# 사용: bash csharp/tools/fetch_kiwi_native.sh [dest_dir]
#   dest_dir 기본값: csharp/native/kiwi
#
# 배포(win-x64) 시에는 kiwi_win_x64_v0.23.2.zip 을 받아 exe 옆에 kiwi.dll 로 동봉한다.
set -euo pipefail

VER="0.23.2"
DEST="${1:-$(dirname "$0")/../native/kiwi}"
BASE="https://github.com/bab2min/Kiwi/releases/download/v${VER}"

# 현재 OS/arch에 맞는 C-API 아카이브 선택
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64)  ASSET="kiwi_lnx_x86_64_v${VER}.tgz" ;;
  Linux-aarch64) ASSET="kiwi_lnx_aarch64_v${VER}.tgz" ;;
  Darwin-arm64)  ASSET="kiwi_mac_arm64_v${VER}.tgz" ;;
  Darwin-x86_64) ASSET="kiwi_mac_x86_64_v${VER}.tgz" ;;
  *) echo "지원하지 않는 플랫폼. Windows는 kiwi_win_x64_v${VER}.zip 을 수동으로." >&2; exit 1 ;;
esac

mkdir -p "$DEST"
echo "다운로드: $ASSET"
curl -sL -o "$DEST/clib.tgz" "$BASE/$ASSET"
tar xzf "$DEST/clib.tgz" -C "$DEST"
echo "완료. 라이브러리: $DEST/lib/lib/libkiwi.so"
echo "실행 시: export LD_LIBRARY_PATH=\"$DEST/lib/lib:\$LD_LIBRARY_PATH\""
echo "모델 경로(kiwipiepy_model): python -c \"import kiwipiepy_model,os;print(os.path.dirname(kiwipiepy_model.__file__))\""
