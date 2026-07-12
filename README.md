# 생기부 도우미 (Saenggibu Helper) — C#/.NET

초·중·고 교사를 위한 **완전 오프라인 로컬 LLM** 생활기록부 작성 도우미.
학생 개인정보가 외부로 나가지 않도록 문장 생성·변형·학습을 모두 교사 PC에서 수행한다.
teaveloper 오프라인 앱 · Windows `.exe` 배포.

이 `main` 브랜치는 **Avalonia UI + LLamaSharp 기반 C#/.NET 8** 버전이다.
원본 파이썬(PySide6) 구현은 **`python` 브랜치**에 예비로 보존되어 있으며, C#의 결정론 로직은
파이썬을 진리값(golden set)으로 삼아 **바이트 단위 파리티**를 검증한다(아래 *파리티 검증* 참고).

- **모델**: Qwen2.5-7B-Instruct (Q4_K_M GGUF, Apache-2.0) — CPU 추론(LLamaSharp).
- **형태소 분석**: Kiwi C-API를 P/Invoke로 사용(kiwipiepy와 동일 모델·동일 결과).
- **UI**: Avalonia 데스크톱 단일 창. 상단 **모드 탭**(생성 / 학습 / 과정 안내) + 영역별 탭.
- **수업 메모 도구**: 트레이 상주 보조 앱(`Memo` → 배포 `수업메모.exe`). 모델을 안 올려 즉시 뜨고,
  **Ctrl+Alt+M**(Windows 전역 단축키) 또는 트레이 클릭으로 하단 팝업. 메인 앱 최초 실행 시
  **자동시작 등록 + 실행**되어 함께 설치된다(`Autostart.EnsureMemoInstalled`).
- **학습**: 교사가 **저장**한 결과를 로컬 SQLite에 누적 → 다음 생성 때 유사 예시를
  순수 C# BM25로 검색해 few-shot 주입한다. (인터넷·추가 모델 불필요)

## 대상 사양
- RAM 8GB / Intel i5 10세대 이상 / GPU 불필요
- 속도보다 품질·프라이버시 우선 (한 문장 세트 생성에 수십 초~수 분 소요될 수 있음)

## 화면 구성 (3개 모드 탭)
1. **✍ 생성 모드** — 영역별 탭에서 키워드·문장을 입력해 생기부 문장을 만든다.
   - **영역 탭**: 세부능력 및 특기사항(세특) · 행동특성 및 종합의견(행특) ·
     자율활동 · 동아리활동 · 봉사활동 · 진로활동
   - **두 가지 생성 방식**
     - *내 문장 변형(같은 의미)* — 교사가 쓴 대표 문장의 **명사·고유명사·숫자는 그대로 두고**
       서술어·어미·어순만 바꿔 의미가 같은 서로 다른 문장 N개를 만든다(동료점검 복붙 방지).
       실수로 키워드를 넣으면 차단하고 생성 모드로 안내한다.
     - *키워드로 새로 생성* — 키워드로 **독립적인 새 문장을 여러 번**(온도 로테이션) 만들어
       다양성을 확보하고, 요청 수가 많으면 어순·표현 재조합으로 확장한다.
   - **세특은 과목 필수** — 과목 없이 만들면 엉뚱한 문장이 나오므로 과목 선택을 강제한다.
   - **학급 표(엑셀식 시트)** — 아래 *엑셀식 시트* 참고.
   - **실시간 형태소 점검**: 입력을 Kiwi로 나눠 색으로 표시(오타·미등록어 확인),
     드래그 후 *용어 등록*으로 전문용어(고유명사)를 사전에 등록하면 철자가 보존된다.
   - **규정 위반 경고**: 공인어학시험·수상·논문·대학명·부모정보 등 기재 불가 항목을 감지해 경고.
2. **📚 학습 모드** — 백업/복원, 기본 모델(GGUF) 선택·다운로드, 용어 사전 관리, 학습 현황.
3. **🗺 과정 안내** — 입력→변형/생성→검증→표 채움 과정과 오프라인/온라인 처리 안내.

## 엑셀식 시트
학급 탭을 여러 개 두고(맨 끝 **＋** 로 추가), 선택한 행에 생성 문장을 채운다. 엑셀과 최대한 동일하게:

- **행/열 편집**(우클릭): 위/아래 행 삽입·행 삭제, 좌/우 열 삽입·열 삭제, 열 이름 변경(머리글 더블클릭).
  학번·이름은 고정, 내용 열은 최소 1개 유지.
- **열 숨기기**: 우클릭 → 숨기기 / 숨긴 열 모두 표시. 숨긴 경계는 **테마색 실선**으로 머리글까지 표시.
- **행 높이·열 너비**: 행번호·머리글 경계 드래그(행별 개별 조절). 내용 열은 **열 너비에 맞춰 줄바꿈 + 행높이 자동맞춤**.
- **복사/붙여넣기**(Ctrl+C / Ctrl+V, TSV), **Delete**(셀 비우기), **Ctrl+휠**(화면 확대/축소).
- **좌상단 코너 전체선택**: 학번=숫자·이름=한글 패턴으로 **학적 있는 행만** 능동 선택.
- **생성 대상 열**: 모든 열을 표시하고 기본은 내용이 가장 많은 열을 고르되, 1·2학기 분리 등 최종 선택은 교사가 한다.
- **찾기·바꾸기**: **현재 시트** 또는 **전체 학급** 범위로 내용 열 일괄 치환.
- **엑셀 가져오기/내보내기**(.xlsx), **맞춤법 검사**, **💾 저장(= 파일 저장 + 자동 학습)**, **⛶ 전체화면**.

## 프라이버시 (무엇이 인터넷을 쓰나)
- **오프라인(기본)**: 문장 생성·변형·학습·형태소 점검·규정 검사 — 학생 데이터는 PC를 벗어나지 않는다.
- **온라인(선택)**: ① 최초 1회 모델 내려받기(HuggingFace) ② **맞춤법 검사**를 정확히 하려면
  네이버로 문장을 전송 — 동의하지 않으면 오프라인 검사만 한다.

## 생성 알고리즘 개요
- **내 문장 변형**(`Core/Paraphrase.cs`) = 통제 변형(controlled paraphrase)
  1. **마스킹**: 등록 용어·복합명사를 플레이스홀더로 가려 분절·변경을 원천 차단.
  2. **LLM 변형**: 보존 명사를 명시한 system 프롬프트 + 교사 과거 문장의 어투 앵커를 주고 라운드로 반복 생성.
  3. **검증**: 한글+영문 깨짐·중간 끊김·비문 종결·지시문 따라쓰기를 거르고, 원문에 없던 내용/기재 불가 항목 유입 차단.
  4. **기계적 보충**(`Core/Variation.cs`): 동의어 치환 + 절 순서 재배열로 개수를 보장(모델 부족 시 안전판).
  5. **명사형 종결 강제**(`Core/Postprocess.cs`): 모든 종결을 `~함/임/됨/보임`으로 정규화.
- **학습·검색**(`Core/MemoryStore.cs` + `Core/Bm25.cs`): 순수 C# BM25(어절+글자 bigram),
  같은 과목 예시 우선(subject boost), 교사 예시를 씨드 코퍼스보다 가깝게 배치.

## 개발 환경에서 실행 (.NET 8 SDK)
```bash
cd csharp
dotnet run --project Gui -c Release           # 메인 앱(개발 실행)
dotnet run --project Cli -c Release           # 골든 회귀 러너(파리티 검증)
```
런타임에 다음이 필요하며 환경변수로 경로를 지정한다(배포 시 앱 옆에 번들).

| 변수 | 설명 |
|------|------|
| `SGB_GGUF` | Qwen2.5-7B GGUF 모델 경로(없으면 학습 모드에서 다운로드) |
| `SGB_KIWI_MODEL` | Kiwi 모델 디렉터리(kiwipiepy_model) |
| `SGB_SEED` | 내장 씨드 코퍼스(`assets/seed_corpus.jsonl`) |
| `SGB_DATA` | 학습 DB·명단·설정 저장 폴더 |
| `SGB_N_CTX` / `SGB_N_THREADS` / `SGB_N_BATCH` / `SGB_N_GPU_LAYERS` | LLM 파라미터(기본 4096 / CPU-1 / 256 / 0) |

> Kiwi C-API 네이티브 라이브러리는 `LD_LIBRARY_PATH`(리눅스) 또는 앱 폴더(Windows)에서 로드된다.

## Windows exe 빌드 (리눅스에서 크로스 빌드 가능)
메인 앱과 수업 메모 도구를 **한 폴더에 함께** 산출하는 스크립트:
```bash
cd csharp
./publish-win.sh [출력폴더]        # 기본: publish/win-x64
```
산출물(같은 폴더):
- `생기부도우미.exe` — 메인 앱. 실행 시 옆의 `수업메모.exe`를 **자동시작 등록 + 실행**.
- `수업메모.exe` — 트레이 상주 메모 도구(Ctrl+Alt+M).

단일 명령으로 하나만 빌드하려면:
```bash
dotnet publish Gui/Gui.csproj -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
- 단일 실행 파일(self-contained)로 산출된다. .NET 런타임 설치 불필요.
- LLamaSharp CPU 백엔드(llama.cpp)·Kiwi 네이티브 라이브러리가 함께 포함된다.
- GGUF 모델은 용량이 커서 exe에 포함하지 않는다 — 앱 옆 `models/` 에 두거나 최초 실행 시 앱이 내려받는다.
- **자동시작**: `Autostart`가 HKCU\...\Run 에 `수업메모.exe`를 등록(관리자 권한 불필요). 해제는 레지스트리에서 제거.

## 파리티 검증 (골든셋)
C#의 결정론 경로(형태소·프롬프트·BM25·후처리·규정·난수 등)는 파이썬을 진리값으로 삼아 검증한다.
```bash
dotnet run --project Cli -c Release        # → "Tier A 골든 회귀: 131 PASS / 0 FAIL"
```
- 기대값 `csharp/golden/golden.json` 은 `python` 브랜치의 파이썬에서 생성한다
  (`csharp/tools/gen_golden.py`, `gen_prompts_cs.py` — 실행에는 파이썬 `app/` 필요).
- C# 빌드·실행·골든 러너 자체는 커밋된 `golden.json`/`PromptsData.cs` 를 쓰므로 파이썬 없이 동작한다.

## 데이터 위치
- 학습 DB(SQLite)·학급 명단·설정: `SGB_DATA` 폴더(미지정 시 OS 사용자 데이터 폴더).
- 모델: `SGB_GGUF` 또는 앱 옆 `models/`.

## 구조
```
csharp/
  Core/    이식된 결정론 로직 + 엔진
    Paraphrase.cs     통제 변형(마스킹·LLM·검증·기계적 보충)  ← 핵심
    Variation.cs      키워드 생성/확장(어순·동의어 조합)
    Postprocess.cs    명사형 종결 강제(결정론)
    Compliance.cs     생기부 기재 불가 항목 검사
    Prompts.cs / PromptsData.cs   영역별 system/user 프롬프트(코드젠)
    Engine.cs / LlamaEngine.cs    few-shot 구성 + LLamaSharp 추론
    KiwiNative.cs     Kiwi C-API P/Invoke(형태소 분석·결합)
    MemoryStore.cs / Bm25.cs      SQLite 예시 저장 + BM25 few-shot 검색(학습)
    PyRandom.cs       CPython MT19937 재현(난수 파리티)
    Importer.cs       엑셀(.xlsx) 명단 가져오기/내보내기(ClosedXML)
    RosterData.cs     학급 명단·시트 읽기/기록
    Glossary.cs / Spellcheck.cs / Downloader.cs / Settings.cs / Config.cs 등
  Gui/     Avalonia 데스크톱 UI(MainWindow.cs, Icons.cs, Assets/)
  Cli/     골든 회귀 러너 + 진단 서브커맨드
  golden/  golden.json(파이썬 진리값)
  tools/   파이썬 코드젠·골든 생성기(python 브랜치의 app/ 필요)
assets/seed_corpus.jsonl   내장 씨드 코퍼스(문장 '형식' 학습용)
```

## 주의 / 면책
생성 결과는 **초안**이다. 생기부 기재요령·사실관계·맞춤법은 교사가 반드시
최종 검토·수정한다. 모델 라이선스(Qwen2.5 = Apache-2.0)를 배포물에 포함한다.
