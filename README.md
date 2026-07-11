# 생기부 도우미 (Saenggibu Helper)

초·중·고 교사를 위한 **완전 오프라인 로컬 LLM** 생활기록부 작성 도우미.
학생 개인정보가 외부로 나가지 않도록 문장 생성·변형·학습을 모두 교사 PC에서 수행한다.
teaveloper 오프라인 앱 · Windows `.exe` 배포.

- **모델**: Qwen2.5-7B-Instruct (Q4_K_M GGUF, Apache-2.0) — CPU 추론.
  생기부 문체로 파인튜닝한 모델(`saenggibu-natural-*`)을 함께 제공/선택할 수 있다.
- **UI**: PySide6 데스크톱 단일 창. 상단 **모드 탭**(생성 / 학습 / 과정 안내) + 영역별 탭.
- **학습**: 교사가 **저장**한 결과를 로컬 DB에 누적 → 다음 생성 때 유사 예시를
  BM25로 검색해 few-shot 주입하고, 자주 쓰는 부사·평가표현을 빈도 프로파일로 반영한다.
  (인터넷·추가 모델 불필요)

## 대상 사양
- RAM 8GB / Intel i5 10세대 이상 / GPU 불필요
- 속도보다 품질·프라이버시 우선 (한 문장 세트 생성에 수십 초~수 분 소요될 수 있음)

## 화면 구성 (3개 모드 탭)
1. **✍ 생성 모드** — 영역별 탭에서 키워드·문장을 입력해 생기부 문장을 만든다.
   - **영역 탭**: 세부능력 및 특기사항(세특) · 행동특성 및 종합의견(행특) ·
     자율활동 · 동아리활동 · 봉사활동 · 진로활동 · 문구 다듬기
   - **두 가지 생성 방식**
     - *내 문장 변형(같은 의미)* — 교사가 쓴 대표 문장의 **명사·고유명사·숫자는 그대로 두고**
       서술어·어미·어순만 바꿔 의미가 같은 서로 다른 문장 N개를 만든다(동료점검 복붙 방지).
     - *키워드로 새로 생성* — 키워드로 새 문장을 만든 뒤 어순·표현을 재조합해 N개로 확장.
   - **학급 표(엑셀식 시트)**: 학급 탭을 여러 개 두고, 체크한 행 수만큼 서로 다른 문장을 채운다.
     엑셀 가져오기/내보내기, 맞춤법 검사, **💾 저장(= 파일 저장 + 자동 학습)**,
     **⛶ 전체화면**(시트만 크게, Esc로 해제), **＋ 학급**(탭 끝) 지원.
   - **실시간 형태소 점검**: 입력을 형태소로 나눠 색으로 표시(오타·미등록어 확인),
     드래그 후 *용어 등록*으로 전문용어(고유명사)를 사전에 등록하면 철자가 보존된다.
   - **규정 위반 경고**: 공인어학시험·수상·논문·대학명·부모정보 등 기재 불가 항목을 감지해 경고.
2. **📚 학습 모드** — 백업/복원, 기본 모델 선택, 용어 사전 관리, 학습 현황 확인.
3. **🗺 과정 안내** — 입력→변형/생성→검증→표 채움 과정 도식과 오프라인/온라인 처리 안내.

## 프라이버시 (무엇이 인터넷을 쓰나)
- **오프라인(기본)**: 문장 생성·변형·학습·형태소 점검·규정 검사 — 학생 데이터는 PC를 벗어나지 않는다.
- **온라인(선택)**: ① 최초 1회 모델 내려받기(HuggingFace) ② **맞춤법 검사**를 정확히 하려면
  네이버로 문장을 전송 — 처음 누를 때 동의를 묻고, 동의하지 않으면 오프라인 검사만 한다.

## 생성 알고리즘 개요
- **내 문장 변형**(`app/paraphrase.py`) = 통제 변형(controlled paraphrase)
  1. **마스킹**: 등록 용어·복합명사를 플레이스홀더로 가려 분절·변경을 원천 차단.
  2. **LLM 변형**: 보존 명사를 명시한 system 프롬프트 + 교사 과거 문장의 **어투 앵커** +
     빈도 프로파일을 주고, 이미 만든 변형과 다르게 만들도록 라운드로 반복 생성.
  3. **검증**(`_valid`): 한글+영문 깨짐·중간 끊김·비문 종결·연결어미 시작·지시문 따라쓰기·
     취업어 표류를 거르고, **원문에 없던 내용 명사/기재 불가 항목 유입을 차단**.
  4. **기계적 보충**(`_mechanical`): 서술어·명사 동의어 치환 + 절 순서 재배열 + 부사/관용/
     평가절을 결정론적으로 조합해 개수를 보장(모델이 부족해도 안전판).
  5. **명사형 종결 강제**(`app/postprocess.py`): 모든 종결을 `~함/임/됨/보임`으로 정규화.
- **학습·검색**(`app/memory_store.py`): 순수 파이썬 BM25(어절+글자 bigram) — 자바 의존 없이 exe 안전.
  같은 과목 예시를 우선(subject boost)하고, 교사 예시를 씨드 코퍼스보다 가깝게 배치.

## 개발 환경에서 실행
```bash
python3.12 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
.venv/bin/python run.py
```
최초 실행 시 상단 배너의 **모델 내려받기** 버튼으로 GGUF(약 4.7GB)를 받는다.
(또는 직접 받아 사용자 데이터 폴더의 `models/` 에 둔다.)

### 8GB PC 튜닝 (환경변수)
| 변수 | 기본 | 설명 |
|------|------|------|
| `SGB_N_CTX` | 4096 | 컨텍스트 길이. RAM 부족 시 2048로 |
| `SGB_N_THREADS` | CPU-1 | 추론 스레드 |
| `SGB_N_BATCH` | 256 | 배치 크기 |
| `SGB_N_GPU_LAYERS` | 0 | 기본 CPU 전용 |

## 파인튜닝 (선택 · `train/`)
생기부 자연 문체를 강화하려면 SFT→DPO 파이프라인을 쓴다.
```
train/build_dataset.py   자연 코퍼스(train/data/natural.jsonl)로 SFT 데이터 구성
train/train_qlora.py     QLoRA SFT
train/build_dpo.py       선호쌍 구성 → train/train_dpo.py   DPO
train/merge_lora.py      LoRA 병합 후 GGUF 양자화(q4_k_m)
```
결과 GGUF를 사용자 `models/` 에 두면 **학습 모드 → 기본 모델**에서 선택할 수 있다.

## Windows exe 빌드
> PyInstaller는 **대상 OS에서 빌드**해야 한다. Windows exe는 Windows에서 빌드.
```powershell
python -m pip install -r requirements.txt pyinstaller
pyinstaller build/saenggibu.spec --noconfirm
# 산출물: dist/생기부도우미/생기부도우미.exe
```
- 앱 아이콘: `app/assets/app.ico`(teaveloper 죽방 엠블럼 + 말풍선). 스펙의 `icon=` 에 지정됨.
- GGUF 모델은 용량이 커서 exe에 포함하지 않는다. 다음 중 하나로 제공한다.
  1. 산출물 폴더 옆 `models/` 에 GGUF를 넣어 배포(오프라인 즉시 사용)
  2. 그대로 배포 → 교사 PC 최초 실행 시 앱이 내려받기

## 데이터 위치
- 학습 DB: `%LOCALAPPDATA%\SaenggibuHelper\memory.sqlite3` (Windows) /
  `~/.local/share/saenggibu-helper/memory.sqlite3` (리눅스)
- 모델: 사용자 데이터 폴더의 `models/` (우선) 또는 실행 파일 옆 `models/`
- 설정: 같은 폴더의 `settings.json` (`active_model`, 맞춤법 온라인 동의 등)

## 구조
```
app/
  config.py        설정·경로·모델 사양·LLM 파라미터
  theme.py         teaveloper 공통 테마(QSS)·브랜드 아이콘/엠블럼
  prompts.py       영역별 system/user 프롬프트 + 공통 작성 원칙
  engine.py        llama.cpp 래퍼(지연 로딩·few-shot 구성·스트리밍)
  paraphrase.py    통제 변형(마스킹·LLM·검증·기계적 보충)  ← 핵심
  variation.py     키워드 생성 모드의 어순·동의어 조합 확장
  postprocess.py   명사형 종결 강제(결정론적)
  compliance.py    생기부 기재 불가 항목 검사
  spellcheck.py    형태소 점검 + (동의 시)네이버 맞춤법
  glossary.py/terms.py  전문용어(고유명사) 등록·보존
  memory_store.py  SQLite 예시 저장 + BM25 few-shot 검색(학습)
  downloader.py    최초 1회 모델 다운로드(이어받기)
  importer.py      엑셀/CSV 학급 명단 가져오기
  ui/              PySide6 메인창·영역 탭·학급 시트·워커 스레드
assets/seed_corpus.jsonl   내장 씨드 코퍼스(문장 '형식' 학습용)
build/saenggibu.spec       PyInstaller 스펙
train/                     SFT·DPO 파인튜닝 파이프라인
scripts/                   코퍼스·생성 품질 평가 스크립트
```

## 주의 / 면책
생성 결과는 **초안**이다. 생기부 기재요령·사실관계·맞춤법은 교사가 반드시
최종 검토·수정한다. 모델 라이선스(Qwen2.5 = Apache-2.0)를 배포물에 포함한다.
