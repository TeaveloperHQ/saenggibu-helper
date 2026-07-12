"""통제 변형(controlled paraphrase) — 같은 의미, 다른 표현.

모델로 새 문장을 만들지 않는다. 교사 문장을 형태소로 분해해 **명사·고유명사·조사는
그대로 두고**, 다음만 바꿔 같은 의미의 다른 문장을 만든다:
  ① 서술어 동의어 치환(분석↔해석, 발표↔공유 …)
  ② 연결어미 변경(~하고 ↔ ~하며)   ③ 절 순서 바꾸기
①은 사전이 있어야 하지만 ②③은 거의 모든 문장에 적용된다 → 사전에 없어도 변형됨.

핵심: kiwi.tokenize → 치환/재배열 → kiwi.join 으로 어미까지 자연스럽게 재결합.
"""
from __future__ import annotations

import random
import re

# kiwi.join이 숫자-한글 사이에 넣는 공백 제거(예: '1 인 1 역'→'1인1역', '2 학기'→'2학기').
# 단어 경계는 보존: '가지고 1인1역'의 '가지고 1' 같은 띄어쓰기는 건드리지 않는다.
_NUM_KO = re.compile(r"(\d)\s+([가-힣])")          # 숫자 뒤 한글: 1 인→1인
_COMPOUND = re.compile(r"(\d[가-힣]+)\s+(\d)")     # 숫자로 시작한 복합어 내부만: 1인 1→1인1
# 복합동사 '~어/아' 뒤 명사형 동사가 띄어진 것 복원: '이끌어 냄'→'이끌어냄', '들어 감'→'들어감'
_CV = re.compile(r"([가-힣]*[어아])\s+(냄|남|줌|놓음|둠|봄|냈?음|감|옴|짐)")


def _fix_spacing(s: str) -> str:
    s = _NUM_KO.sub(r"\1\2", s)
    s = _COMPOUND.sub(r"\1\2", s)
    s = _CV.sub(r"\1\2", s)
    return s


def looks_like_keywords(text: str) -> bool:
    """변형 모드 안전장치 — 완성 문장이 아니라 '키워드 나열'로 보이면 True.
    슬래시(/) 나열이거나, 서술어(용언)·문장 어미가 전혀 없으면 키워드로 판단한다.
    (실수로 키워드를 넣고 '같은 의미 문장 만들기'를 눌러 엉뚱한 결과가 나오는 것을 막는다.)"""
    t = (text or "").strip()
    if not t:
        return False
    if "/" in t:                      # 앱의 키워드 나열 형식(A / B / C)
        return True
    from .spellcheck import _get_kiwi
    kiwi = _get_kiwi()
    if kiwi is None:
        return False
    try:
        tags = [tk.tag for tk in kiwi.tokenize(t)]
    except Exception:
        return False
    has_predicate = any(tg[:1] == "V" or tg in ("XSV", "XSA") for tg in tags)
    has_ending = any(tg in ("EF", "EC", "ETN", "ETM") for tg in tags)
    return not has_predicate and not has_ending


def _fix_josa_ro(text: str) -> str:
    """받침 있는 명사(ㄹ 제외) 뒤 조사 '로'→'으로'(예: '요약문로'→'요약문으로').
    kiwi가 조사 '로'(JKB)를 분리 인식하므로 '진로·회로' 같은 단어는 건드리지 않는다."""
    from .spellcheck import _get_kiwi
    kiwi = _get_kiwi()
    if kiwi is None or "로" not in text:
        return text
    try:
        toks = kiwi.tokenize(text)
    except Exception:
        return text
    edits = []
    for t in toks:
        # 단독 '로' 토큰 = 조사 자리. kiwi가 생소어 뒤 '로'를 NNG로 오분석하기도 하므로
        # (예: '판별식로'→[판별식/NNP, 로/NNG]) 태그와 무관하게 처리한다.
        # '진로·회로'는 form이 '진로'/'회로'인 단일 토큰이라 form=='로' 조건에서 제외됨.
        if t.form == "로" and t.start > 0:
            prev = text[t.start - 1]
            if "가" <= prev <= "힣":
                jong = (ord(prev) - 0xAC00) % 28
                if jong not in (0, 8):            # 받침 있고 ㄹ이 아니면 '으로'
                    edits.append(t.start)
    for pos in sorted(edits, reverse=True):
        text = text[:pos] + "으" + text[pos:]
    return text

# 서술성 명사(X + 하다) 동의어 묶음
_PRED_NOUN = [
    {"분석", "해석", "검토"}, {"발표", "공유"}, {"설명", "안내", "이야기"},
    {"탐구", "탐색", "연구"}, {"수행", "진행", "실시"}, {"관찰", "관측"},
    {"조사", "파악"}, {"이해", "파악"}, {"정리", "종합"}, {"측정", "계측"},
    {"참여", "참가", "동참"}, {"협력", "협동"}, {"제시", "제안"},
    {"비교", "대조"}, {"활용", "이용", "사용"}, {"향상", "증진"},
    {"표현", "표출"}, {"실천", "이행"}, {"점검", "확인"}, {"발휘", "구사"},
    {"구성", "구축"}, {"설계", "구상"}, {"개선", "보완"}, {"적용", "응용"},
    {"준비", "마련"}, {"완수", "수행"}, {"몰입", "집중"},
    {"질문", "문의"}, {"발견", "포착"}, {"도출", "유도"}, {"습득", "체득"},
    {"기록", "정리"}, {"제작", "제작"}, {"기획", "구상"}, {"검증", "확인"},
    {"보고", "정리"}, {"토의", "토론"}, {"논의", "토의"}, {"공유", "나눔"},
    {"구성", "구조화"}, {"작성", "기술"}, {"관리", "운영"}, {"제시", "제안"},
    # --- 확장(2026-07-02, 표현 다양성) ---
    {"해결", "해소", "극복"}, {"도전", "시도"}, {"발전", "성장"}, {"성취", "달성"},
    {"판단", "분별"}, {"추론", "추리"}, {"실현", "구현"}, {"완성", "완결"},
    {"대응", "대처"}, {"조율", "조정"}, {"소통", "교류"}, {"주도", "선도"},
    {"기여", "이바지"}, {"정립", "확립"}, {"감상", "음미"}, {"비판", "비평"},
    {"인식", "자각"}, {"창출", "창조"}, {"수집", "모음"}, {"발상", "착안"},
    # --- 능동 확장 2 (2026-07-02) ---
    {"예측", "전망"}, {"계산", "연산"}, {"증명", "입증"}, {"격려", "독려"},
    {"조언", "제언"}, {"성찰", "숙고"}, {"고민", "궁리"}, {"숙달", "숙련"},
    {"계발", "개발"}, {"함양", "육성"}, {"몰두", "전념"}, {"경험", "체험"},
    {"연습", "훈련"}, {"조립", "결합"}, {"분해", "해체"}, {"판별", "구분", "분류"},
    {"선정", "선택", "채택"}, {"배치", "배열"}, {"인지", "감지"}, {"보존", "보전"},
    # --- 능동 확장 3 (2026-07-02) ---
    {"요약", "축약"}, {"정독", "숙독"}, {"열거", "나열"}, {"서술", "진술"},
    {"표기", "기재"}, {"산정", "산출"}, {"유지", "지속"}, {"반복", "되풀이"},
    {"시연", "시범"}, {"부각", "조명"}, {"강조", "역설"}, {"언급", "거론"},
    {"반박", "반론"}, {"옹호", "지지"}, {"수긍", "납득"}, {"협의", "협상"},
    {"타협", "절충"}, {"화합", "단합"}, {"권장", "권유"}, {"배려", "포용"},
    {"공감", "교감"}, {"봉사", "헌신"}, {"기부", "기증"}, {"연마", "단련"},
    {"정진", "매진"}, {"도모", "지향", "추구"}, {"형성", "성립"}, {"유발", "야기"},
    {"촉진", "촉발"}, {"총괄", "관장"}, {"주관", "주최"}, {"답사", "탐방", "견학"},
    {"검사", "진단", "판정"}, {"배분", "분배"}, {"할당", "배정"}, {"집계", "합산"},
    # --- 과목 전용 서술어(해당 단어가 있을 때만 발동 → 전역 안전) ---
    {"검출", "검지"}, {"응결", "응축"}, {"증발", "기화"}, {"판독", "해독"},
    {"감별", "식별"}, {"낭독", "낭송"}, {"작문", "글쓰기"}, {"논증", "논변"},
    # --- 능동 확장 4 ---
    {"고려", "감안", "참작"}, {"착수", "돌입"}, {"완료", "마무리"}, {"재현", "재연"},
    {"복원", "복구"}, {"변형", "변환"}, {"확산", "전파"}, {"제거", "삭제"},
    {"보충", "보강"}, {"정비", "정돈"}, {"인용", "차용"}, {"참고", "참조"},
    {"주목", "주시"}, {"경청", "청취"}, {"응답", "답변"}, {"통합", "융합"},
    {"연계", "연결"}, {"예방", "방지"}, {"암기", "암송"},
]
# 동사·형용사 어간 동의어
_VERB = [
    {"보이", "드러내", "나타내"}, {"기르", "키우"}, {"갖추", "지니"},
    {"돕", "지원하"}, {"살피", "살펴보"}, {"이끌", "주도하"},
    {"넓히", "확장하"}, {"높이", "끌어올리"}, {"맡", "담당하"},
    {"익히", "습득하"}, {"느끼", "체감하"}, {"나누", "주고받"},
    # --- 확장 ---
    {"만들", "제작하"}, {"쌓", "축적하"}, {"세우", "수립하"},
    {"고치", "수정하"}, {"늘리", "확대하"}, {"돌보", "보살피"},
    {"모으", "수집하"}, {"넘", "극복하"},
    # --- 능동 확장 2 ---
    {"바꾸", "변경하"}, {"고르", "선택하"}, {"줄이", "축소하"}, {"견주", "비교하"},
    {"다듬", "정제하"}, {"북돋", "고취하"}, {"밝히", "규명하"}, {"엮", "구성하"},
    # --- 능동 확장 3 ---
    {"다루", "취급하"}, {"뽑", "선발하"}, {"이루", "달성하"}, {"맞추", "조정하"},
    {"아우르", "포괄하"}, {"견디", "인내하"},
    # --- 능동 확장 4 ---
    {"깨닫", "인식하"}, {"떠올리", "연상하"}, {"헤아리", "가늠하"},
    {"펼치", "전개하"}, {"메우", "보완하"},
]
# 연결어미(EC) 변경 — 의미 보존되는 쌍
_EC = {"고": ["며"], "며": ["고"]}
# 서술어 앞에 안전하게 덧붙일 생기부 부사(명사·사실 불변 → 안전한 다양성)
_ADVERBS = ["성실히", "꼼꼼히", "적극적으로", "스스로", "꾸준히", "차근차근",
            "진지하게", "열심히", "능숙하게", "침착하게",
            "세심히", "자발적으로", "논리적으로", "창의적으로", "주도적으로",
            "능동적으로", "끈기 있게", "깊이 있게",
            "신중히", "명료하게", "정확히", "정성껏", "빈틈없이", "일관되게",
            "활발히", "성심껏",
            "면밀히", "유연하게", "폭넓게", "자세히", "두루", "골고루",
            "진취적으로", "한결같이",
            "열정적으로", "침착히", "세밀하게", "정교하게", "착실히",
            "부지런히", "알차게", "야무지게"]
# 종결부에 붙일 생기부 관용 표현(틀 명사는 _GENERIC_NOUNS라 새 명사 아님 → 안전)
_IDIOMS = ["모습이 인상적임", "태도가 돋보임", "자세를 보임", "점이 인상적임",
           "모습이 돋보임", "면모가 엿보임", "모습을 보임", "모습이 보기 좋음",
           "자세가 인상적임", "면모를 보임", "점이 돋보임", "모습이 엿보임"]
# 평가 절 — 생기부는 [활동 관찰]+[평가]. 평가부는 긍정적이면 활동과 다소 동떨어져도 무방.
# 서술어를 연결형(~하며)으로 바꾸고 뒤에 붙인다: 'X하며 탐구력을 보임'
_EVAL_CLAUSES = [
    "탐구력을 보임", "사고력을 발휘함", "역량을 보임", "이해력을 보임",
    "집중력을 보임", "창의성이 돋보임", "적극성이 돋보임", "성실함이 돋보임",
    "노력이 엿보임", "잠재력을 보임", "열정이 돋보임", "논리력을 보임",
    "분석력이 돋보임", "표현력이 뛰어남", "응용력을 보임", "통찰력이 엿보임",
    "호기심이 돋보임", "끈기가 돋보임", "책임감이 돋보임", "자기주도성이 돋보임",
    "관찰력이 돋보임", "판단력이 우수함", "몰입도가 높음", "이해가 빠름",
    "학습 태도가 모범적임", "성실성이 돋보임",
    "논리적 사고가 뛰어남", "탐구 정신이 돋보임", "학업 역량이 우수함",
    "지적 호기심이 왕성함", "적극적인 참여가 인상적임", "성실한 자세가 돋보임",
    # --- 전 영역(행특·자율·동아리·봉사·진로) 인성·태도 평가 ---
    "리더십을 발휘함", "배려심이 돋보임", "협동심이 뛰어남", "봉사 정신이 돋보임",
    "책임감이 강함", "공동체 의식이 돋보임", "자기 관리가 철저함", "인내심이 돋보임",
    "소통 능력이 뛰어남", "성실한 태도가 몸에 뱀", "진취적인 자세가 돋보임",
    "긍정적인 태도가 인상적임",
]
# 평가를 '두 문장'으로 나눌 때 뒷문장 앞에 붙이는 연결어(없어도 됨)
_EVAL_CONNECTORS = ["", "이 과정에서 ", "이를 통해 ", "특히 "]
# 과목별 평가 표현 — 해당 과목일 때만 붙인다(수리적 사고력을 국어에 붙이지 않도록)
_SUBJECT_EVAL = {
    "과학": ["과학적 사고력이 뛰어남", "실험 설계 능력이 돋보임", "탐구 역량이 우수함",
             "관찰력이 뛰어남", "원리를 탐구하는 자세가 돋보임"],
    "물리": ["물리적 개념 이해가 뛰어남", "현상을 원리로 설명하는 능력이 돋보임"],
    "화학": ["화학 반응 이해가 뛰어남", "실험 분석 능력이 돋보임"],
    "생명": ["생명 현상 탐구력이 돋보임", "관찰·분석 능력이 우수함"],
    "생물": ["생명 현상 탐구력이 돋보임", "관찰·분석 능력이 우수함"],
    "지구": ["자연 현상 탐구력이 돋보임", "자료 해석 능력이 우수함"],
    "수학": ["수리적 사고력이 뛰어남", "논리적 추론 능력이 돋보임", "문제 해결력이 우수함",
             "풀이 과정이 논리적임"],
    "국어": ["문해력이 뛰어남", "비판적 사고가 돋보임", "어휘력이 풍부함", "표현력이 우수함"],
    "영어": ["어휘력이 풍부함", "의사소통 능력이 돋보임", "독해력이 우수함"],
    "사회": ["비판적 시각이 돋보임", "자료 해석 능력이 우수함", "탐구 자세가 돋보임"],
    "역사": ["역사적 사고력이 돋보임", "사료 분석 능력이 우수함"],
    "한국사": ["역사적 사고력이 돋보임", "사료 해석 능력이 우수함"],
    "지리": ["공간적 사고력이 돋보임", "자료 해석 능력이 우수함"],
    "경제": ["논리적 분석력이 돋보임", "자료 해석 능력이 우수함"],
    "윤리": ["성찰적 사고가 돋보임", "가치 판단 능력이 우수함"],
    "도덕": ["성찰적 태도가 돋보임", "공동체 의식이 뛰어남"],
    "정보": ["논리적 사고력이 뛰어남", "문제 해결 능력이 돋보임"],
    "기술": ["창의적 설계 능력이 돋보임", "문제 해결력이 우수함"],
    "가정": ["실생활 적용 능력이 돋보임", "실천적 태도가 인상적임"],
    "음악": ["표현력이 풍부함", "감수성이 돋보임"],
    "미술": ["표현력이 풍부함", "창의적 발상이 돋보임"],
    "체육": ["적극적인 태도가 돋보임", "협동심이 뛰어남"],
    "한문": ["어휘력이 풍부함", "문장 해석 능력이 우수함"],
    "제2외국어": ["어휘력이 풍부함", "의사소통 의지가 돋보임"],
    "일본어": ["어휘력이 풍부함", "의사소통 의지가 돋보임"],
    "중국어": ["어휘력이 풍부함", "의사소통 의지가 돋보임"],
}
# 과목 평가 표현에 쓰인 긍정 명사도 화이트리스트에 포함
_EVAL_NOUNS_SUBJECT = {
    "실험", "탐구", "수리", "추론", "문해력", "어휘력", "독해력", "시각",
    "과학", "사료", "풀이", "설계", "물리", "화학", "생명", "현상", "공간",
    "가치", "성찰", "표현력", "감수성", "발상", "적용", "의지", "해석",
}


def _subject_evals(subject: str) -> list:
    """과목명 유연 매칭(예: '물리학'→물리, '통합과학'→과학)."""
    subject = (subject or "").strip()
    if not subject:
        return []
    for key, evs in _SUBJECT_EVAL.items():
        if key in subject or subject in key:
            return evs
    return []
# 평가에 자유롭게 쓰이는 긍정 명사 — 활동과 무관해도 허용(검증에서 '새 명사'로 안 침)
_EVAL_NOUNS = {
    "탐구력", "탐구심", "사고력", "논리력", "논리", "창의력", "창의성", "역량",
    "자질", "잠재력", "성실성", "성실함", "책임감", "열정", "열의", "적극성",
    "리더십", "협동심", "배려심", "인내심", "끈기", "집중력", "이해력", "응용력",
    "분석력", "표현력", "의사소통", "자기주도성", "통찰력", "호기심", "몰입",
    "성장", "발전", "실력", "자신감", "성취", "완성도", "발상", "안목", "감각",
    "관찰력", "판단력", "몰입도", "학습", "협업", "발표력", "이해", "사고",
    "탐구", "정신", "학업", "참여", "의욕", "의지력", "집념", "감수성",
    "리더십", "배려심", "협동심", "봉사", "공동체", "소통", "관리",
    "융통성", "포용력", "실행력", "추진력",
    # 과목 무관 '역량·능력' 명사(내용어 아님) — 전역 허용 안전
    "문해력", "어휘력", "독해력", "추론", "수리", "성찰",
}
# 교체 가능한 '일반' 명사 동의어 — 도메인어·고유명사는 넣지 않음(의미 보존)
_NOUN_SYN = [
    {"방법", "방식"}, {"특징", "특성"}, {"능력", "역량"}, {"관점", "시각"},
    {"내용", "사항"}, {"의견", "견해"}, {"과정", "절차"}, {"목표", "목적"},
    {"자료", "정보"}, {"원리", "이치"}, {"방향", "방침"}, {"역할", "소임"},
    # --- 확장 ---
    {"결과", "성과"}, {"문제", "과제"}, {"계획", "방안"}, {"이유", "까닭"},
    {"부분", "측면"}, {"핵심", "요점"}, {"의미", "뜻"}, {"기준", "잣대"},
    {"차이", "차이점"}, {"수준", "정도"}, {"관계", "연관성"},
    # --- 능동 확장 2 ---
    {"바탕", "토대", "기반"}, {"경향", "성향"}, {"장점", "강점"}, {"상황", "여건"},
    {"효과", "효능"}, {"영향", "작용"}, {"성질", "속성"}, {"현상", "양상"},
    {"전반", "전체"},
    # --- 능동 확장 3 ---
    {"원인", "요인"}, {"요소", "성분"}, {"규모", "크기"}, {"범위", "영역"},
    {"순서", "차례"}, {"기회", "계기"}, {"근거", "논거"}, {"사례", "예시"},
    {"흐름", "추세"}, {"경계", "한계"},
    # --- 능동 확장 4 ---
    {"의도", "취지"}, {"입장", "처지"}, {"쟁점", "논점"}, {"책무", "임무"},
    {"의의", "가치"}, {"징후", "조짐"}, {"연관", "관련"},
]


def _syn_map(groups):
    m = {}
    for g in groups:
        for w in g:
            alt = sorted(g - {w})
            if alt:
                m[w] = alt
    return m


_PRED_MAP = _syn_map(_PRED_NOUN)
_VERB_MAP = _syn_map(_VERB)
_NOUN_MAP = _syn_map(_NOUN_SYN)


def _alternatives(morphs):
    """각 형태소 위치의 치환 후보(원형 포함). 명사·고유명사·조사는 원형만."""
    opts = []
    n = len(morphs)
    for i, (form, tag) in enumerate(morphs):
        alts = [form]
        # 복합동사(이끌+어+내다)의 앞 어간은 치환 금지 — 의미·형태가 깨짐
        # 뒷동사는 VX(보조용언)로도 태깅됨(이끌어'내'다)
        compound = (i + 2 < n and morphs[i + 1][1] == "EC"
                    and morphs[i + 1][0] in ("어", "아")
                    and morphs[i + 2][1] in ("VV", "VA", "VX"))
        if tag in ("VV", "VA") and form in _VERB_MAP and not compound:
            alts += _VERB_MAP[form]
        elif (tag == "NNG" and i + 1 < n and morphs[i + 1][0] == "하"
              and morphs[i + 1][1] in ("XSV", "XSA") and form in _PRED_MAP):
            alts += _PRED_MAP[form]
        elif tag == "NNG" and form in _NOUN_MAP:      # 교체 가능한 일반 명사 동의어
            alts += _NOUN_MAP[form]
        elif tag == "EC" and form in _EC:
            alts += _EC[form]
        opts.append(alts)
    return opts


# 절 경계로 쓸 안전한 연결어미만(복합동사의 '어/아'는 제외 — '이끌어내다' 분리 방지)
_SAFE_CONN = {"고", "며", "여", "하고", "하며", "하여"}


def _reorder(morphs, rng):
    """진짜 절 연결어미(고·며·여)에서만 절을 나눠 순서를 섞는다(종결 절은 끝에).
    '이끌어냄'의 내부 '어' 같은 복합동사 어미는 경계로 보지 않는다."""
    clauses, cur = [], []
    for m in morphs:
        cur.append(m)
        if m[1] == "EC" and m[0] in _SAFE_CONN:
            clauses.append((cur, True))
            cur = []
    if cur:
        clauses.append((cur, False))
    nonfinal = [c for c, nf in clauses if nf]
    final = [c for c, nf in clauses if not nf]
    if len(nonfinal) < 2:
        return morphs
    order = nonfinal[:]
    rng.shuffle(order)
    if order == nonfinal:                       # 그대로면 변화 없음
        return morphs
    flat = [m for c in order for m in c] + [m for c in final for m in c]
    return flat


def _mechanical(sentence: str, n: int = 10, *, seed: int = 42,
                subject: str = "", profile: dict | None = None) -> list[str]:
    """사전·어미·어순 기반 결정론적 변형(안전판). 모델 변형이 부족할 때 보충.
    profile: 교사 빈도 프로필(자주 쓰는 부사·평가표현) — 있으면 우선 사용."""
    sentence = (sentence or "").strip()
    if not sentence:
        return []
    from .spellcheck import _get_kiwi
    from .postprocess import to_nominal_endings
    kiwi = _get_kiwi()
    if kiwi is None:
        return [sentence]
    try:
        toks = kiwi.tokenize(sentence)
    except Exception:
        return [sentence]
    morphs = [(t.form, t.tag) for t in toks]
    opts = _alternatives(morphs)
    var_pos = [i for i, a in enumerate(opts) if len(a) > 1]
    can_reorder = sum(1 for f, tg in morphs if tg == "EC") >= 2

    rng = random.Random(seed)
    results, seen = [], set()

    def _add_surf(surf):
        surf = _fix_spacing(to_nominal_endings(surf))
        key = surf.replace(" ", "")
        if key and key not in seen:
            seen.add(key)
            results.append(surf)

    def _add(ms):
        try:
            _add_surf(kiwi.join(ms))
        except Exception:
            pass

    _add(morphs)                                 # 원문 우선

    # 교사 빈도 프로필(자주 쓰는 부사·평가표현)을 우선 소비 → '그 교사 말투'로 수렴
    prof = profile or {}
    t_advs = [a for a in prof.get("adverbs", []) if a not in _ADVERBS]
    t_evals = prof.get("evals", [])
    evals = _EVAL_CLAUSES + _subject_evals(subject)          # 과목별 평가 표현(유연 매칭)
    pool_pri: list = []                          # 교사 빈도(우선)
    pool: list = []                              # 범용
    # ① 부사 삽입 — 서술성 명사(NNG+하) 앞. 단, 이미 부사·부사어가 있으면 겹치지 않게 제외
    def _adverbial_before(i):
        if i == 0:
            return False
        f, tg = morphs[i - 1]
        return tg == "MAG" or f in ("으로", "로") or f[-1:] in ("히", "게", "이")
    adv_points = [i for i, (f, tg) in enumerate(morphs)
                  if tg == "NNG" and i + 1 < len(morphs)
                  and morphs[i + 1][0] == "하" and morphs[i + 1][1] in ("XSV", "XSA")
                  and not _adverbial_before(i)]
    for i in adv_points:
        for adv in t_advs:                       # 교사 자주 쓰는 부사(우선)
            pool_pri.append(("m", morphs[:i] + [(adv, "MAG")] + morphs[i:]))
        for adv in _ADVERBS:
            pool.append(("m", morphs[:i] + [(adv, "MAG")] + morphs[i:]))
    # ② 관용 표현 덧붙이기 — 종결 서술어를 관형형으로 바꿔 생기부 관용구 부착
    eidx = next((i for i in range(len(morphs) - 1, -1, -1)
                 if morphs[i][1] in ("ETN", "EF")), None)
    base_c = None
    if eidx and morphs[eidx - 1][1] in ("VV", "VA", "XSV", "VX"):
        et = ("ᆫ", "ETM") if morphs[eidx - 1][1] == "VA" else ("는", "ETM")
        try:
            base = _fix_spacing(kiwi.join(morphs[:eidx] + [et]))
        except Exception:
            base = None
        if base:
            for idm in _IDIOMS:
                pool.append(("s", base + " " + idm))
        try:                                     # 평가 절 부착(X하며 + 평가) — 한 문장
            base_c = _fix_spacing(kiwi.join(morphs[:eidx] + [("며", "EC")]))
            for ev in evals:
                pool.append(("s", base_c + " " + ev))
        except Exception:
            base_c = None
    # 문장 분할: 긴 연결문(A하고 B하며 C함)을 절 단위 단문 나열로(병합의 반대) — 구조 다양성
    if sum(1 for f, tg in morphs if tg == "EC" and f in _SAFE_CONN) >= 2:
        clauses = _split_clauses(sentence)
        if len(clauses) >= 2:
            pool_pri.append(("s", ". ".join(clauses)))   # 구조가 확 달라 우선 채택
    # 두 문장으로 나누기 — [활동 관찰함]. [평가 보임].
    obs = _fix_spacing(to_nominal_endings(kiwi.join(morphs))).rstrip(". ")
    for ev in evals:
        conn = _EVAL_CONNECTORS[rng.randrange(len(_EVAL_CONNECTORS))]
        pool.append(("two", f"{obs}. {conn}{ev}"))
    # 교사 자주 쓰는 평가 표현(우선) — 두 문장 + (가능하면) 한 문장 결합
    for ev in t_evals:
        pool_pri.append(("two", f"{obs}. {ev}"))
        if base_c:
            pool_pri.append(("s", base_c + " " + ev))
    # ③ 서술어·명사 동의어 치환 + 절 순서 바꾸기 조합
    # ③-a 각 치환 가능 위치를 '반드시 바꾼' 변형을 보장 → 본문(서술어)이 실제로 달라짐
    for i in var_pos:
        for alt in [a for a in opts[i] if a != morphs[i][0]][:2]:
            chosen = list(morphs)
            chosen[i] = (alt, morphs[i][1])
            pool_pri.append(("m", chosen))         # 우선 소비(부사 스왑보다 앞)
    # ③-b 여러 위치 동시 치환 + 절 순서 섞기(랜덤 조합)
    if var_pos or can_reorder:
        for _ in range(n * 4):
            chosen = list(morphs)
            for i in var_pos:
                chosen[i] = (rng.choice(opts[i]), morphs[i][1])
            if can_reorder and rng.random() < 0.55:
                chosen = _reorder(chosen, rng)
            pool.append(("m", chosen))

    rng.shuffle(pool_pri)                         # 교사 우선 풀 먼저 소비
    rng.shuffle(pool)
    # 후보를 넉넉히 모은 뒤(부사 스왑이 수적으로 많으므로) 구조 균형으로 선택한다.
    cap = max(n * 4, 24)
    for kind, payload in pool_pri + pool:
        if len(results) >= cap:
            break
        if kind == "m":
            _add(payload)
        else:
            _add_surf(payload)
    return _balance_by_structure(results, n, rng)


def _balance_by_structure(cands: list[str], n: int, rng) -> list[str]:
    """구조(구성·종결) 그룹을 라운드로빈으로 돌며 하나씩 뽑되, 각 그룹 안에서는 본문(첫
    문장, 부사 제외)이 새로운 것을 우선한다 → 구조·본문 모두 다양(부사만 바뀐 편중 방지)."""
    from . import patterns

    def _body(c: str) -> str:                     # 본문 지문(첫 문장) — 부사는 빼고 비교
        s = c.split(".")[0]
        for adv in _ADVERBS:
            s = s.replace(adv, "")
        return s.replace(" ", "")[:26]

    groups: dict = {}
    order: list = []
    for c in cands:
        lab = patterns.classify(c)
        st = (lab["comp"], lab["end"])
        if st not in groups:
            groups[st] = []
            order.append(st)
        groups[st].append((c, _body(c)))
    rng.shuffle(order)
    for st in order:
        rng.shuffle(groups[st])

    out: list = []
    seen_body: set = set()
    progressed = True
    while len(out) < n and progressed:
        progressed = False
        for st in order:                          # 각 구조에서 하나씩(라운드로빈)
            g = groups[st]
            if not g:
                continue
            pick = next((idx for idx, (_, b) in enumerate(g) if b not in seen_body), 0)
            c, body = g.pop(pick)                  # 새 본문 우선, 없으면 아무거나
            out.append(c)
            seen_body.add(body)
            progressed = True
            if len(out) >= n:
                break
    return out[:n]


def _split_sents(text: str) -> list[str]:
    return [s.strip() for s in re.split(r"(?<=[.])\s+", text.strip()) if s.strip()]


def _sentence_variants(sent: str, k: int, rng, *, adverbs: bool = True) -> list[str]:
    """한 문장의 '본문' 변형만(동의어 치환·연결어미·부사). 평가절·두문장 분리는 안 함.
    → 다문장 입력을 문장별로 바꿔 재조합할 때 각 문장 재료로 쓴다."""
    from .spellcheck import _get_kiwi
    from .postprocess import to_nominal_endings
    kiwi = _get_kiwi()
    if kiwi is None:
        return [sent]
    try:
        morphs = [(t.form, t.tag) for t in kiwi.tokenize(sent)]
    except Exception:
        return [sent]
    opts = _alternatives(morphs)
    var_pos = [i for i, a in enumerate(opts) if len(a) > 1]
    out: list = []
    seen: set = set()

    def add(ms):
        try:
            s = _fix_spacing(to_nominal_endings(kiwi.join(ms)))
        except Exception:
            return
        key = s.replace(" ", "")
        if key and key not in seen:
            seen.add(key)
            out.append(s)

    add(morphs)                                    # 원형 먼저(내용 보존)
    for i in var_pos:                              # 각 위치 단일 치환(동의어·연결어미)
        for alt in [a for a in opts[i] if a != morphs[i][0]][:2]:
            ch = list(morphs)
            ch[i] = (alt, morphs[i][1])
            add(ch)
    # 평가 문장이면 평가 서술어를 표층에서 치환(돋보임↔뛰어남 등) — 평가부는 더 자유롭게
    if _is_eval_sent(sent):
        base_list = list(out)
        for base in base_list:
            b = base.rstrip(". ")
            for pat, reps in _EVAL_PRED_SWAP.items():
                if b.endswith(pat):
                    for rep in reps:
                        s2 = b[:-len(pat)] + rep
                        key = s2.replace(" ", "")
                        if key not in seen:
                            seen.add(key)
                            out.append(s2)
                    break
    if adverbs and len(out) < k:                   # 부족하면 부사 삽입으로 보충
        adv_pts = [i for i, (f, tg) in enumerate(morphs)
                   if tg == "NNG" and i + 1 < len(morphs)
                   and morphs[i + 1][0] == "하" and morphs[i + 1][1] in ("XSV", "XSA")]
        advs = list(_ADVERBS)
        rng.shuffle(advs)
        for i in adv_pts:
            for adv in advs:
                if len(out) >= k + 2:
                    break
                add(morphs[:i] + [(adv, "MAG")] + morphs[i:])
    return out[:max(k, 1)]


_EVAL_SENT_RE = re.compile(
    r"(력|심|성|감|역량|자세|태도|정신|의식|자질|리더십)\S*\s*"
    r"(보임|돋보임|뛰어남|우수함|발휘|지님|엿보임|인상적|강함|높음|빠름)")

# 평가 서술어 표층 치환(의미 보존 범위) — 평가부만 더 자유롭게 변형
_EVAL_PRED_SWAP = {
    "돋보임": ["뛰어남", "두드러짐"], "뛰어남": ["돋보임", "우수함"],
    "우수함": ["뛰어남", "돋보임"], "보임": ["드러냄", "보여줌"],
    "드러냄": ["보임"], "엿보임": ["보임", "드러남"],
    "지님": ["갖춤"], "인상적임": ["돋보임", "인상 깊음"],
}


def _is_eval_sent(s: str) -> bool:
    """'평가' 문장인지(역량·태도 명사 + 평가 서술). 평가는 더 자유롭게 변형한다."""
    return bool(_EVAL_SENT_RE.search(s))


def _clause_form(morphs, rng):
    """한 문장의 명사형 종결(…함/…음)을 연결어미(…하며/…하고/…하여)로 바꾼 절로.
    '여'는 하-어간(XSV) 전용 — 아니면 '배우여' 같은 비문이 되므로 며/고만."""
    ms = list(morphs)
    while ms and ms[-1][1].split("-")[0] in ("SF", "SP", "SE", "SS", "ETN", "EF"):
        ms.pop()                                   # 종결·부호 제거(불규칙 태그 -I/-R 포함)
    if ms and ms[-1][1].split("-")[0] in ("VV", "VA", "VX", "XSV", "XSA"):
        stem, tag = ms[-1]
        ecs = ["며", "고"]
        if tag.split("-")[0] == "XSV" or stem.endswith("하"):         # 하-어간만 '여'
            ecs = ["며", "고", "여"]
        ms.append((rng.choice(ecs), "EC"))
    return ms


def _merge_forms(msents: list[str], rng, rounds: int = 8) -> list[str]:
    """쪼개진 문장들을 다양한 접속사로 '병합'해 한 문장으로. 자유도가 낮은 단문 나열을
    긴 연결문으로 바꿔 구조 다양성을 만든다(마지막 절만 명사형 종결 유지)."""
    from .spellcheck import _get_kiwi
    from .postprocess import to_nominal_endings
    kiwi = _get_kiwi()
    if kiwi is None or len(msents) < 2:
        return []
    try:
        morphset = [[(t.form, t.tag) for t in kiwi.tokenize(s)] for s in msents]
    except Exception:
        return []
    out: list = []
    seen: set = set()
    for _ in range(rounds):
        parts = []
        for i, ms in enumerate(morphset):
            if i < len(morphset) - 1:
                parts.append(_clause_form(ms, rng))
            else:
                parts.append(list(ms))            # 마지막 절 = 명사형 종결 유지
        flat = [m for p in parts for m in p]
        try:
            surf = _fix_spacing(to_nominal_endings(kiwi.join(flat)))
        except Exception:
            continue
        key = surf.replace(" ", "")
        if key and key not in seen:
            seen.add(key)
            out.append(surf)
    return out


def _rejected_texts(engine, area) -> list[str]:
    """교사가 '버린'(부정 피드백) 변형 목록 — 생성 시 유사 출력 회피용."""
    store = getattr(engine, "_store", None)
    if store is None or area is None:
        return []
    try:
        return store.rejected_texts(area.key)
    except Exception:
        return []


def _recombine_paraphrase(engine, sentence: str, n: int,
                          area=None, subject: str = "") -> list[str]:
    """다문장 입력 → 각 문장을 따로 변형해 재조합. 내용·길이는 보존하고 표현만 서로 다름.
    - 관찰(활동) 문장: 최소 변형(사실 보존)  - 평가 문장: 더 자유롭게 변형
    - 절반은 마침표로 나눠 잇고, 절반은 다양한 접속사로 '병합'해 구조 다양성 확보."""
    masked, mapping = _mask_terms(sentence)
    msents = _split_sents(masked)
    rng = random.Random(_seed_of(sentence))
    # 관찰=최소(부사 없이·후보 3), 평가=자유(부사 포함·후보 6)
    pools = []
    for ms in msents:
        ev = _is_eval_sent(ms)
        pools.append(_sentence_variants(ms, 6 if ev else 3, rng, adverbs=ev))
    rejected = _rejected_texts(engine, area)       # 교사가 '버린' 표현(회피)
    results: list = []
    seen = {sentence.replace(" ", "")}
    tries, limit = 0, n * 150
    sim = 0.965                                    # 변형끼리 이 이상 비슷하면 스킵
    while len(results) < n and tries < limit:
        tries += 1
        chosen = [rng.choice(p) for p in pools]
        if len(chosen) >= 2 and rng.random() < 0.55:   # 절반 이상은 접속사로 병합
            mv = _merge_forms(chosen, rng, rounds=1)
            combo = mv[0] if mv else " ".join(chosen)
        else:
            combo = " ".join(chosen)
        full = _unmask(combo, mapping)
        key = full.replace(" ", "")
        if key in seen:
            continue
        if _too_similar(full, sentence, 0.985):    # 원문과 거의 같으면 스킵
            continue
        if any(_too_similar(full, rj, 0.9) for rj in rejected):   # 교사가 버린 표현 회피
            continue
        if any(_too_similar(full, r, sim) for r in results):  # 변형끼리도 충분히 다르게
            continue
        seen.add(key)
        results.append(full)
        if tries > limit * 0.6 and len(results) < n:
            sim = 0.99                             # 후반엔 기준 완화(개수 보장)
    return results[:n]


# ---------------------------------------------------------------------------
# 언어모델 기반 제약 변형 — 고유명사·주요 명사는 유지, 서술어·조사만 모델이 바꿈
# ---------------------------------------------------------------------------
_LATIN = re.compile(r"[A-Za-z]")
_HANGUL = re.compile(r"[가-힣]")


def _nouns(sentence: str):
    """(전체 보존명사, 필수명사). 필수=고유명사(NNP)+등록용어, 전체=거기에 내용명사 추가."""
    from .spellcheck import _get_kiwi
    from . import glossary
    kiwi = _get_kiwi()
    allk: list[str] = []
    crit: list[str] = []
    if kiwi is not None:
        try:
            toks = kiwi.tokenize(sentence)
        except Exception:
            toks = []
        n = len(toks)
        for i, t in enumerate(toks):
            if t.tag == "NNP":
                allk.append(t.form)
                crit.append(t.form)
            elif t.tag == "NNG" and len(t.form) >= 2:
                nxt = toks[i + 1] if i + 1 < n else None
                if not (nxt and nxt.form == "하" and nxt.tag in ("XSV", "XSA")):
                    allk.append(t.form)            # 서술성 명사(분석/발표 등)는 제외
    flat = sentence.replace(" ", "")
    for t in glossary.all_terms():
        if t.replace(" ", "") in flat:
            allk.append(t)
            crit.append(t)
    return list(dict.fromkeys(allk)), list(dict.fromkeys(crit))


def preserve_terms(sentence: str) -> list[str]:
    """변형 시 그대로 유지할 단어 목록(프롬프트 표시용)."""
    return _nouns(sentence)[0]


def _clean_line(line: str) -> str:
    line = line.strip()
    line = re.sub(r"^\s*(?:\d+[.)]|[-•*·▪]|[(\[]?\d+[)\]])\s*", "", line)  # 번호·불릿
    line = re.sub(r"\s*/\s*", ". ", line)                    # '/' 구분자 → 문장 분리
    return line.strip().strip("\"'“”‘’").strip()


def _malformed_ending(v: str) -> bool:
    """모델이 자주 내는 비문 종결 탐지:
    ① XSN(력·심·감)+하 → '탐구력함/자신감함'  ② ㅁ받침 명사+하 → '도움함/이끔함'
    ③ '드럽' 오분석 → '드러움/드러임'. (정상 '배려함·노력함·분석함'은 걸리지 않음)"""
    from .spellcheck import _get_kiwi
    if re.search(r"드러[움임넴]", v):                      # 드러남/드러냄의 오형(드러움·드러임)
        return True
    kiwi = _get_kiwi()
    if kiwi is None:
        return False
    try:
        toks = kiwi.tokenize(v)
    except Exception:
        return False
    for i in range(len(toks) - 1):
        cur, nxt = toks[i], toks[i + 1]
        if nxt.form == "하" and nxt.tag in ("XSV", "VV"):
            if cur.tag == "XSN":                          # 력/심/감 + 하
                return True
            if cur.tag in ("NNG", "NNP") and cur.form:    # ㅁ받침 명사(도움/이끔) + 하
                c = cur.form[-1]
                if "가" <= c <= "힣" and (ord(c) - 0xAC00) % 28 == 16:
                    return True
        if cur.form == "드럽":                            # '드러움/드러임' 오분석
            return True
    return False


# 중간에 끊긴(연결어미로 끝나는) 문장 거부
_BAD_END = re.compile(r"(으로써|으로서|하며|으며|하고|하여|면서|지만|거나|든지|어서|아서|도록|는데|니까)\.?$")
# '틀 명사 + 함/음/임'은 비문(태도함·모습함 등) — 명사는 '~를 보임/이 돋보임'이라야 함
_BAD_NOUN_END = re.compile(
    r"(모습|태도|자세|면모|마음|능력|역량|자질|소임|역할|안목|감각|점|자신감|리더십|"
    # 능력 명사('실력하다' 등은 없는 말) — 'X력함'은 비문. 노력·협력(하다-동사)은 제외
    r"실력|사고력|논리력|창의력|통찰력|집중력|판단력|표현력|이해력|응용력|분석력|"
    r"관찰력|발표력|어휘력|독해력|문해력|상상력|기억력|추진력|실행력)[함음임]\.?$")
# 모델이 지시문을 따라 쓴 메타 문장 거부
_META = ("다음은", "다음과", "바꾼 ", "바꿔", "표현으로", "원문", "아래", "같이 변", "결과는:")
# 학생 기록에 부적절한 취업·직장 관련어(모델 표류의 신호) — 원문에 없이 들어오면 거부
_DRIFT_WORDS = ("근무", "출근", "퇴근", "직장", "재직", "채용", "취업", "직원", "회사원", "입사")


def _starts_connective(v: str) -> bool:
    """첫 어절이 연결어미(EC)로 끝나면 비문(예: '설명하고…', '펼치고…')."""
    from .spellcheck import _get_kiwi
    kiwi = _get_kiwi()
    parts = v.split()
    if kiwi is None or not parts:
        return False
    try:
        toks = kiwi.tokenize(parts[0])
    except Exception:
        return False
    return bool(toks) and toks[-1].tag == "EC"


def _bigrams(s: str) -> set:
    s = s.replace(" ", "")
    return {s[i:i + 2] for i in range(len(s) - 1)}


def _too_similar(a: str, b: str, thresh: float = 0.90) -> bool:
    """원문과 거의 같으면(부사만 덧붙인 얕은 변형) True."""
    A, B = _bigrams(a), _bigrams(b)
    if not A or not B:
        return a.replace(" ", "") == b.replace(" ", "")
    return len(A & B) / len(A | B) >= thresh


# 생기부 관용 표현의 '틀' 명사 — 사실이 아니라 서술 프레임이라 새로 들어와도 허용
_GENERIC_NOUNS = {
    "모습", "태도", "점", "자세", "마음", "능력", "역량", "열정", "흥미", "관심",
    "노력", "의지", "자신감", "책임감", "모범", "면모", "자질", "습관", "면",
    "과정", "결과", "내용", "활동", "수업", "시간", "부분", "편", "때", "중",
}


def _valid(v: str, allk: list[str], crit: list[str], original: str,
           sim_thresh: float = 0.95) -> bool:
    if not v or (_LATIN.search(v) and _HANGUL.search(v)):      # 한글+영문 깨짐
        return False
    if _BAD_END.search(v):                                     # 문장이 끊김
        return False
    if _BAD_NOUN_END.search(v.rstrip(". ")):                   # '태도함' 같은 비문 종결
        return False
    if _malformed_ending(v):                                   # '탐구력함·도움함·드러움' 비문
        return False
    if _starts_connective(v):                                  # 연결어미로 시작(비문)
        return False
    if any(w in v for w in _META):                             # 지시문 따라쓰기
        return False
    if any(w in v and w not in original for w in _DRIFT_WORDS):  # 취업·직장어 표류
        return False
    if _too_similar(v, original, sim_thresh):                  # 부사만 바뀐 얕은 변형
        return False
    base = len(original.replace(" ", ""))
    if not (base * 0.5 <= len(v.replace(" ", "")) <= base * 2.6):   # 관용구·평가문 덧붙임 여유
        return False
    norm = v.replace(" ", "")
    if any(t.replace(" ", "") not in norm for t in crit):      # 고유명사·용어 필수
        return False
    # 원문 명사(allk) + 서술 '틀' 명사 + 과목 무관 역량 명사만 허용.
    # _EVAL_NOUNS_SUBJECT(화학·물리·실험·풀이·설계 등 실제 '내용' 명사)는 전역 허용에서
    # 제외한다 — 넣으면 교사 코퍼스의 도메인 소재가 다른 맥락으로 유출된다(앵커 유출 방지).
    allow = set(allk) | _GENERIC_NOUNS | _EVAL_NOUNS
    for nn in allk:                                            # + 교체 가능한 명사의 동의어
        allow |= set(_NOUN_MAP.get(nn, []))
    if len([x for x in _nouns(v)[0] if x not in allow]) > 1:   # 새 '내용' 명사 2개↑ 유입 거부
        return False                                           # (부사·관용구·명사동의어는 통과)
    # 원문 명사가 빠졌는지(단, 동의어로 교체된 경우는 보존으로 인정)
    missing = sum(1 for t in allk if t.replace(" ", "") not in norm
                  and not any(s in norm for s in _NOUN_MAP.get(t, [])))
    return missing <= max(1, len(allk) // 5)                   # 내용명사 대부분 보존


def _build_system(preserve: list[str]) -> str:
    kept = ", ".join(preserve) if preserve else "(없음)"
    return (
        "너는 학교생활기록부 문장을 '같은 의미의 다른 표현'으로 바꿔 쓰는 도우미다. "
        "규칙을 반드시 지킨다:\n"
        "1. 문장의 사실을 절대 바꾸지 않는다. 원문에 없는 새로운 활동·소재를 지어내지 않는다.\n"
        f"2. 다음 명사는 철자 그대로 반드시 유지한다. 빼거나 다른 말로 바꾸지 않는다: {kept}\n"
        "3. 서술어·조사·어미·어순을 바꾸거나, 부사(꼼꼼히·적극적으로·스스로 등)·생기부 "
        "관용 표현(~하는 모습을 보임, ~태도가 돋보임 등)을 덧붙여 문장을 서로 다르게 만든다.\n"
        "4. 모든 문장은 명사형 종결(~함, ~임, ~보임, ~지님)로 완결한다. 문장을 중간에 끊지 않는다.\n"
        "5. 주어·학생 호칭('학생은' 등)을 쓰지 않는다.\n"
        "6. 한 줄에 한 문장씩, 요청한 개수만큼만 출력한다. 번호·설명·따옴표 없이 본문만.\n\n"
        "예) 원문: 산과 염기 단원에서 지시약 실험을 직접 설계하고 결과를 그래프로 정리함\n"
        "바꾼 문장:\n"
        "산과 염기 단원에서 지시약 실험을 스스로 계획하고 결과를 그래프로 나타냄\n"
        "산과 염기 단원의 지시약 실험을 직접 구성하여 그 결과를 그래프로 정리해 봄\n"
        "(유지된 명사: 산, 염기, 단원, 지시약, 실험, 결과, 그래프 — 서술어만 바뀜)"
    )


# 등록 용어 가림용 플레이스홀더(분절·변경 원천 차단) — 받침 있는 한글 명사
_MASKS = ["갑물질", "을물질", "병물질", "정물질", "무물질", "기물질", "경물질", "신물질",
          "해물질", "수물질", "차물질", "토물질"]
# 자동 복합어 감지에 묶을 명사류 태그(NR=수사: 이차·일차의 '이/일' 보존; 관형사 '이/그'는 MM이라 제외)
_NOUN_TAGS = {"NNG", "NNP", "NNB", "SN", "SL", "SH", "NR"}


def _auto_compounds(sentence: str) -> list[str]:
    """공백 없이 붙여 쓴 복합명사(kiwi가 2토큰 이상으로 쪼개는 것)를 원문 그대로 추출.
    예: '환경부장'(환경부+장), '1인1역'(1+인+1+역), '이차함수'(이차+함수).
    공백으로 떨어진 것(광합성 실험)은 묶지 않는다."""
    from .spellcheck import _get_kiwi
    kiwi = _get_kiwi()
    if kiwi is None:
        return []
    try:
        toks = kiwi.tokenize(sentence)
    except Exception:
        return []
    comps, i, n = [], 0, len(toks)
    while i < n:
        if toks[i].tag in _NOUN_TAGS:
            j = i
            while (j + 1 < n and toks[j + 1].tag in _NOUN_TAGS
                   and toks[j + 1].start == toks[j].start + len(toks[j].form)):
                j += 1
            if j > i:                                   # 2토큰 이상 = 붙여 쓴 복합어
                start = toks[i].start
                end = toks[j].start + len(toks[j].form)
                surf = sentence[start:end]
                if len(surf) >= 2:
                    comps.append(surf)
            i = j + 1
        else:
            i += 1
    return comps


def _mask_terms(sentence: str):
    """등록 용어 + 자동 감지 복합명사를 플레이스홀더로 치환(분절·변경 원천 차단).
    반환: (가린문장, {플레이스홀더:원어})."""
    from . import glossary
    targets: list[str] = []
    for t in sorted(glossary.all_terms(), key=len, reverse=True):   # 등록 용어 우선
        if t and t not in targets:
            targets.append(t)
    for c in sorted(set(_auto_compounds(sentence)), key=len, reverse=True):
        if c not in targets:
            targets.append(c)
    masked, mapping, i = sentence, {}, 0
    for t in targets:
        if t and t in masked and i < len(_MASKS):
            ph = _MASKS[i]
            masked = masked.replace(t, ph)
            mapping[ph] = t
            i += 1
    return masked, mapping


def _unmask(text: str, mapping: dict[str, str]) -> str:
    # 모델이 플레이스홀더를 띄어 쓸 수 있으므로 글자 사이 공백을 허용해 복원
    for ph, t in mapping.items():
        pat = re.compile(r"\s*".join(re.escape(c) for c in ph))
        text = pat.sub(t, text)
    return text


def _split_clauses(text: str) -> list[str]:
    """문장을 절 단위(고·며·여 연결어미)로 쪼개 각 절을 명사형으로. 서술 표현 팔레트용."""
    from .spellcheck import _get_kiwi
    from .postprocess import to_nominal_endings
    kiwi = _get_kiwi()
    if kiwi is None:
        return []
    try:
        toks = [(t.form, t.tag) for t in kiwi.tokenize(text)]
    except Exception:
        return []
    clauses, cur = [], []
    for m in toks:
        cur.append(m)
        if m[1] == "EC" and m[0] in _SAFE_CONN:
            clauses.append(cur)
            cur = []
    if cur:
        clauses.append(cur)
    out = []
    for c in clauses:
        cc = c[:-1] if c and c[-1][1] == "EC" else c        # 끝 연결어미 제거
        if cc and cc[-1][1] in ("VV", "VA", "VX", "XSV", "XSA"):
            cc = cc + [("ᆷ", "ETN")]                         # 명사형 어미 부착
        try:
            surf = _fix_spacing(to_nominal_endings(kiwi.join(cc))).strip()
        except Exception:
            continue
        if 4 <= len(surf) <= 40:
            out.append(surf)
    return out


def _style_anchors(engine, area, subject: str, query: str, k: int = 4) -> list[str]:
    """교사가 사전 학습한 비슷한 문장을 검색해 **절 단위 서술 표현**으로 분해.
    명사 밀집 문장의 변형 자유도를 교사 어휘로 보완한다."""
    store = getattr(engine, "_store", None)
    if area is None or store is None:
        return []
    try:
        rows = store.retrieve(area=area.key, query=query, k=k, subject=subject)
    except Exception:
        return []
    qn = query.replace(" ", "")
    palette, seen = [], set()
    for r in rows:
        t = (getattr(r, "output_text", "") or "").strip()
        if not t:
            continue
        for c in _split_clauses(t):                          # 절 단위로 쪼개 표현 다양화
            key = c.replace(" ", "")
            if key and key != qn and key not in seen:
                seen.add(key)
                palette.append(c)
    return palette[:8]


_PROFILE_CACHE: dict = {}


def _teacher_profile(engine, area_key: str, subject: str = "") -> dict:
    """교사 코퍼스 전체에서 '자주 쓰는' 부사·평가표현을 빈도순으로 집계(개인화).
    유사도(anchors)와 달리 전역 빈도라 '이 교사가 즐겨 쓰는 말투'를 잡는다. (건수로 캐시)"""
    store = getattr(engine, "_store", None)
    if store is None or not area_key:
        return {"adverbs": [], "evals": []}
    try:
        docs = store._rows_for_area(area_key)
    except Exception:
        return {"adverbs": [], "evals": []}
    if subject:
        sub = [d for d in docs if getattr(d, "subject", "")
               and (subject in d.subject or d.subject in subject)]
        docs = sub or docs
    key = (area_key, subject, len(docs))
    if key in _PROFILE_CACHE:
        return _PROFILE_CACHE[key]
    from collections import Counter
    from .spellcheck import _get_kiwi
    kiwi = _get_kiwi()
    allow_eval = _EVAL_NOUNS | _EVAL_NOUNS_SUBJECT | _GENERIC_NOUNS
    adv, evals = Counter(), Counter()
    for d in docs[:400]:
        txt = (getattr(d, "output_text", "") or "").strip()
        if kiwi:
            try:
                for t in kiwi.tokenize(txt):
                    if t.tag == "MAG" and 2 <= len(t.form) <= 6:
                        adv[t.form] += 1
            except Exception:
                pass
        for c in _split_clauses(txt):                # 평가 명사가 든 짧은 절 = 교사 평가표현
            if 4 <= len(c) <= 15 and any(ev in c for ev in allow_eval):
                evals[c] += 1                        # 짧게 = 다른 문장에도 전이 잘 됨
    prof = {"adverbs": [w for w, _ in adv.most_common(6)],
            "evals": [c for c, _ in evals.most_common(6)]}
    _PROFILE_CACHE[key] = prof
    return prof


_STRUCT_CACHE: dict = {}


def _structure_profile(engine, area_key: str, subject: str = "") -> dict:
    """교사 코퍼스의 '구조 패턴'(구성·종결·순서·연결) 빈도 프로파일. 부족하면 전역 기본값."""
    from . import patterns
    store = getattr(engine, "_store", None)
    if store is None or not area_key:
        return patterns.DEFAULT_PROFILE
    try:
        docs = store._rows_for_area(area_key)
    except Exception:
        return patterns.DEFAULT_PROFILE
    if subject:
        sub = [d for d in docs if getattr(d, "subject", "")
               and (subject in d.subject or d.subject in subject)]
        docs = sub or docs
    key = (area_key, subject, len(docs))
    if key in _STRUCT_CACHE:
        return _STRUCT_CACHE[key]
    prof = patterns.analyze([getattr(d, "output_text", "") for d in docs[:400]])
    _STRUCT_CACHE[key] = prof
    return prof


def _seed_of(text: str) -> int:
    """문장별 결정론적 시드(같은 입력 → 같은 구조 플랜)."""
    return sum(ord(c) * (i + 1) for i, c in enumerate(text[:64])) & 0xFFFFFFFF


def llm_paraphrase(engine, sentence: str, n: int = 10,
                   progress=None, area=None, subject: str = "",
                   should_cancel=None) -> list[str]:
    """언어모델로 의미 보존 변형 n개. 등록 용어·복합어는 가려서(mask) 분절·변경을 원천
    차단하고, **교사가 사전 학습한 비슷한 문장을 어투 앵커로 주입**해 표현 자유도를 보완.
    부족분은 사전·어순 변형으로 보충. should_cancel(): True면 중간에 멈추고 지금까지 반환."""
    from .postprocess import to_nominal_endings
    from . import patterns
    sentence = (sentence or "").strip()
    if not sentence:
        return []
    # 다문장 입력(문단)이면: 문장별로 변형해 재조합 → 내용 보존 + 표현 다양
    long_sents = [s for s in _split_sents(sentence) if len(s) >= 8]
    if len(long_sents) >= 2:
        recombined = _recombine_paraphrase(engine, sentence, n, area, subject)
        if len(recombined) >= max(2, min(n, 3)):
            return recombined
        # 재조합이 빈약하면(문장별 변형 여지 없음) 아래 단일 경로로 폴백
    masked, mapping = _mask_terms(sentence)         # 등록 용어·복합어 가림
    placeholders = list(mapping.keys())
    allk, crit = _nouns(masked)
    allk = list(dict.fromkeys(allk + placeholders))
    crit = list(dict.fromkeys(crit + placeholders))  # 플레이스홀더는 반드시 보존
    profile = _teacher_profile(engine, area.key if area else "", subject)  # 교사 빈도 개인화
    struct_prof = _structure_profile(engine, area.key if area else "", subject)  # 교사 구조 패턴
    struct_plan = patterns.plan(n, struct_prof, random.Random(_seed_of(sentence)))
    system = _build_system(allk)
    per = min(max(3, n), 6)
    # 온도는 중간(안전 우선) — 너무 높이면 작은 모델이 헛소리(엉뚱한 부사·비표준어)를 냄.
    # 자유도는 절 팔레트(_style_anchors)와 적응형 보충이 담당한다.
    temps = [0.8, 0.95, 1.05, 0.85, 1.0, 1.1]

    # 자유도 보완: 교사 과거 문장을 어투 앵커로(내용은 빌리지 않음). 용어는 가려서 표시.
    anchors = _style_anchors(engine, area, subject, sentence)
    anchor_block = ""
    if anchors:
        masked_anchors = [_mask_terms(a)[0] for a in anchors]
        anchor_block = (
            "참고 — 네가 전에 쓴 서술 표현들이다. 이 어투·표현을 적극 빌려 다양하게 바꾸되, "
            "거기 담긴 소재·내용은 가져오지 말고 원문의 명사는 그대로 둬라:\n"
            + "\n".join(f"- {a}" for a in masked_anchors) + "\n\n")
    # 빈도 개인화: 교사가 자주 쓰는 표현을 우선 살리도록 모델에 알림
    fav = list(profile.get("evals", []))[:4] + list(profile.get("adverbs", []))[:4]
    if fav:
        anchor_block += ("네가 특히 자주 쓰는 표현·어투다. 되도록 이 말투를 살려라: "
                         + ", ".join(fav) + "\n\n")

    from . import compliance
    orig_block = {t for lv, c, t in compliance.check(sentence) if lv == "block"}

    rejected = _rejected_texts(engine, area)       # 교사가 '버린' 표현(회피)
    results: list[str] = []
    seen = {sentence.replace(" ", "")}

    def _accept(v: str, mut_thresh: float = 0.90) -> None:
        """이미 채택된 변형들과도 충분히 다를 때만 추가(거의 같은 것만 제외)."""
        v = _fix_josa_ro(v)                        # 조사 '로'→'으로' 교정
        key = v.replace(" ", "")
        if key in seen:
            return
        # 원문에 없던 생기부 '기재 불가' 항목이 변형에서 새로 생기면 제외(앵커 유출 방지)
        if any(t not in orig_block for lv, c, t in compliance.check(v) if lv == "block"):
            return
        if any(_too_similar(v, rj, 0.9) for rj in rejected):   # 교사가 버린 표현 회피
            return
        for a in results:
            if _too_similar(v, a, mut_thresh):     # 다른 변형과 거의 같으면 제외
                return
        seen.add(key)
        results.append(v)

    # 부족하면 더 많이 샘플링해 '서로 다른' 변형을 모은다(명사 밀집 문장 대비)
    rounds = max(6, (n + per - 1) // per + 3)
    for r in range(rounds):
        if len(results) >= n:
            break
        if should_cancel and should_cancel():      # 교사가 '중지' → 지금까지로 마감
            break
        if progress:
            progress(f"문장 변형 중… ({len(results)}/{n})")
        # 이미 만든 변형을 보여주고 '그것들과 다르게' 만들라고 지시(다양성↑)
        avoid_block = ""
        if results:
            prev = [_mask_terms(x)[0] for x in results]
            avoid_block = (
                "아래는 이미 만든 표현이다. 이것들과 다르게 만들어라 — 다른 부사나 "
                "관용 표현을 쓰거나 서술어·어순을 바꿔라:\n"
                + "\n".join(f"- {p}" for p in prev) + "\n\n")
        # 구조 축(구성·종결·순서·연결)을 각 문장마다 서로 다르게 강제(얕은 변형 방지)
        base_idx = r * per
        targets = [struct_plan[(base_idx + k) % len(struct_plan)] for k in range(per)]
        struct_block = (
            "아래 지정한 '서로 다른 문장 구조'로 하나씩 만들어라. 부사만 바꾸지 말고 "
            "문장 구성·종결 방식·절 연결을 지시대로 바꿔라:\n"
            + "\n".join(f"{k + 1}) {patterns.instruction(t)}" for k, t in enumerate(targets))
            + "\n\n")
        user = (anchor_block + avoid_block + struct_block
                + f"원문: {masked}\n\n"
                f"원문의 명사(대상·소재)는 그대로 두고, 서로 다른 표현 {per}가지를 "
                f"한 줄에 하나씩 출력해라.\n"
                f"- 부사(꼼꼼히·적극적으로·스스로·꾸준히·차근차근·진지하게 등)나 생기부 "
                f"관용 표현(~하는 모습을 보임, ~태도가 돋보임, ~점이 인상적임 등)을 "
                f"덧붙이거나 서술어를 바꿔 문장을 서로 다르게 만든다.\n"
                f"- 일반 명사(결과·방법·내용·특징 등)는 비슷한 말로 바꿔도 좋다. "
                f"단, 고유명사·전문용어·숫자는 절대 바꾸지 않는다.\n"
                f"- 생기부는 '활동'과 '평가'로 이뤄진다. 활동(대상·소재)은 그대로 두되, "
                f"평가 부분에는 긍정적 표현(탐구력·성실함·역량·적극성·논리적 사고 등)을 "
                f"자유롭게 덧붙여도 좋다.\n"
                f"- 활동과 평가를 한 문장으로 이어도 되고, 두 문장으로 나눠도 된다"
                f"(예: '…실험을 수행함. 탐구력을 보임').\n"
                f"- 원문에 없는 '새로운 사실·소재(활동 내용)'는 지어내지 않는다.\n"
                f"- 설명·머리말 없이 문장만 쓴다.")
        try:
            out = engine.complete(system, user,
                                  max_tokens=64 + per * (len(masked) // 2 + 24),
                                  temperature=temps[r % len(temps)])
        except Exception:
            break
        for line in out.splitlines():
            v = _clean_line(line)
            if not v:
                continue
            v = to_nominal_endings(_fix_spacing(v))
            if not _valid(v, allk, crit, masked):    # 플레이스홀더 보존 여부 포함 검증
                continue
            v = _unmask(v, mapping)                   # 원용어 복원
            _accept(v)
            if len(results) >= n:
                break

    def _fill(sim_thresh: float):
        for v in _mechanical(masked, n * 2, subject=subject, profile=profile):
            if len(results) >= n:
                return
            if not _valid(v, allk, crit, masked, sim_thresh):
                continue
            v = _unmask(v, mapping)
            _accept(v)

    if len(results) < n:                            # 1차 보충: 엄격(얕음 제외)
        _fill(0.90)
    if len(results) < min(n, 3):                    # 너무 적으면 완화: 약한 변형도 허용
        _fill(0.97)                                  # (연결어미·종결 교체 등 — 개수 보장)
    if not results:                                 # 최후: 안전한(사전·어순) 변형 1개라도
        for v in _mechanical(masked, n, subject=subject, profile=profile):
            v = _unmask(v, mapping)
            if v.replace(" ", "") != sentence.replace(" ", ""):
                results.append(v)
                break
    return results[:n]
