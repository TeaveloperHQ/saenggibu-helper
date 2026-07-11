"""맞춤법 검사.

기본: 네이버 맞춤법 검사(온라인) — 실제 교정안을 받는다. 표의 '내용'은 학생
이름·학번이 빠진 평가 문장이라 식별정보가 아니므로 검사에 보낸다.
폴백/생성필터: 오프라인 형태소 분석(kiwipiepy)로 깨진·미등록 단어를 잡는다.

- naver_spellcheck(text): (교정문, 오류수) 또는 None(온라인 실패)
- suspect_words(text) / looks_garbled(text): 오프라인 의심 단어
"""
from __future__ import annotations

import json
import re
import ssl
import urllib.parse
import urllib.request

# ----------------------------------------------------------------------------
# 네이버 맞춤법 검사(온라인)
# ----------------------------------------------------------------------------
_UA = {"User-Agent": "Mozilla/5.0", "Referer": "https://search.naver.com/"}
# 학생 문장을 전송하므로 SSL 인증서를 반드시 검증한다(MITM 방지).
# 검증 실패 시 요청이 예외를 던지고, 호출부에서 None→오프라인 검사로 폴백한다.
_SSL = ssl.create_default_context()
_passport: str | None = None


def _get(url: str, params: dict | None = None) -> str:
    if params:
        url = url + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers=_UA)
    with urllib.request.urlopen(req, timeout=8, context=_SSL) as r:
        return r.read().decode("utf-8", "ignore")


def _passport_key(refresh: bool = False) -> str | None:
    global _passport
    if _passport and not refresh:
        return _passport
    try:
        html = _get("https://search.naver.com/search.naver", {"query": "맞춤법검사기"})
    except Exception:
        return None
    m = (re.search(r"passportKey=([a-zA-Z0-9]+)", html)
         or re.search(r'"passportKey"\s*:\s*"([^"]+)"', html))
    _passport = m.group(1) if m else None
    return _passport


def naver_spellcheck(text: str) -> tuple[str, int, str] | None:
    """네이버 맞춤법 교정. (교정문, 오류수, 마크업html) 반환. 온라인 실패 시 None."""
    text = (text or "").strip()
    if not text:
        return ("", 0, "")
    for attempt in range(2):                       # 키 만료 시 1회 갱신 재시도
        key = _passport_key(refresh=(attempt == 1))
        if not key:
            return None
        try:
            raw = _get(
                "https://m.search.naver.com/p/csearch/ocontent/util/SpellerProxy",
                {"passportKey": key, "where": "nexearch",
                 "color_blindness": 0, "q": text})
            res = json.loads(raw)["message"]["result"]
            html = res["html"]
            corrected = re.sub("<[^>]+>", "", html)
            return (corrected, int(res.get("errata_count", 0)), html)
        except Exception:
            continue
    return None


def styled_html(naver_html: str) -> str:
    """네이버 교정 마크업 → 셀 렌더링용 색상 html(빨강=맞춤법, 파랑=띄어쓰기)."""
    h = naver_html
    h = h.replace("<em class='red_text'>", "<span style='color:#d32f2f;font-weight:bold'>")
    h = h.replace("<em class='green_text'>", "<span style='color:#1565c0;font-weight:bold'>")
    h = re.sub(r"<em class='[^']*'>", "<span style='color:#e65100;font-weight:bold'>", h)
    h = h.replace("</em>", "</span>")
    return h


# ----------------------------------------------------------------------------
# 오프라인 형태소 분석(kiwipiepy) — 폴백 및 생성 단계 필터
# ----------------------------------------------------------------------------
_kiwi = None
_kiwi_unavailable = False
_FOREIGN_TAGS = {"SL", "SH", "SW", "UNK"}
_SCORE_THRESHOLD = -22.0


def _get_kiwi():
    global _kiwi, _kiwi_unavailable
    if _kiwi is None and not _kiwi_unavailable:
        try:
            from kiwipiepy import Kiwi
            _kiwi = Kiwi()                       # 먼저 대입(재귀 _get_kiwi 안전)
            from . import glossary               # 등록 용어를 사전에 반영
            glossary.register_all_with_kiwi()
        except Exception:
            _kiwi_unavailable = True
    return _kiwi


def suspect_words(text: str) -> list[str]:
    """깨진·미등록·외국어 단어 후보(오프라인)."""
    text = (text or "").strip()
    if not text:
        return []
    kiwi = _get_kiwi()
    if kiwi is None:
        return []
    try:
        toks = kiwi.tokenize(text)
    except Exception:
        return []
    out = []
    for tk in toks:
        if len(tk.form) < 2:
            continue
        if tk.tag in _FOREIGN_TAGS:
            out.append(tk.form)
        elif tk.tag in ("NNG", "NNP") and tk.score < _SCORE_THRESHOLD:
            out.append(tk.form)
    return list(dict.fromkeys(out))


def looks_garbled(text: str) -> bool:
    """생성 단계 재생성 판단용(오프라인)."""
    return bool(suspect_words(text))


def analyze_tokens(text: str) -> list[tuple[str, str]]:
    """입력 문장을 형태소로 나누고 종류를 표시: (형태소, 종류).
    종류 = ok(정상) | susp(미등록·오타 의심) | foreign(영문/한자) | gram(조사·어미·기호)."""
    text = (text or "").strip()
    if not text:
        return []
    kiwi = _get_kiwi()
    if kiwi is None:
        return [(text, "ok")]
    try:
        toks = kiwi.tokenize(text)
    except Exception:
        return [(text, "ok")]
    from . import glossary
    gloss = glossary.words()                    # 등록 용어의 개별 단어
    out = []
    for tk in toks:
        tag, form = tk.tag, tk.form
        if form in gloss:                       # 교사가 등록한 용어는 정상
            kind = "ok"
        elif tag in ("SL", "SH", "SW"):
            kind = "foreign"
        elif tag == "UNK":
            kind = "susp"
        elif tag in ("NNG", "NNP") and len(form) >= 2 and tk.score < _SCORE_THRESHOLD:
            kind = "susp"
        elif tag[:1] in ("J", "E", "X", "S"):
            kind = "gram"
        else:
            kind = "ok"
        out.append((form, kind))
    return out
