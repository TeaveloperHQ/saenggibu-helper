"""교사별 학습 저장소.

교사가 '채택(저장)'한 결과를 SQLite 에 누적하고, 다음 생성 때 같은 영역의
유사한 과거 예시를 BS25 로 검색해 few-shot 으로 주입한다.
=> 쓸수록 그 교사의 문체·표현을 따라가는 '학습' 효과를 낸다.

외부 모델·인터넷 없이 동작하도록 BM25 를 순수 파이썬으로 구현한다.
한국어 형태소 분석기(konlpy 등)는 자바 의존성 때문에 exe 배포에 부적합하므로
'어절 + 글자 bigram' 토크나이저로 대체한다(충분히 실용적).
"""
from __future__ import annotations

import math
import re
import sqlite3
import time
from dataclasses import dataclass

from . import config

_WORD_RE = re.compile(r"[가-힣]+|[a-zA-Z]+|[0-9]+")


def tokenize(text: str) -> list[str]:
    """어절 토큰 + 한글 글자 bigram 을 함께 반환한다."""
    text = text.lower()
    tokens: list[str] = []
    for w in _WORD_RE.findall(text):
        tokens.append(w)
        if len(w) >= 2 and re.fullmatch(r"[가-힣]+", w):
            tokens.extend(w[i:i + 2] for i in range(len(w) - 1))
    return tokens


@dataclass
class Example:
    id: int
    area: str
    subject: str
    keywords: str
    output_text: str
    rating: int
    created_at: float


class MemoryStore:
    def __init__(self, db_path=None):
        self.db_path = str(db_path or config.DB_PATH)
        self._conn = sqlite3.connect(self.db_path, check_same_thread=False)
        self._conn.row_factory = sqlite3.Row
        # BM25용 토큰화 캐시(매 생성마다 전체 코퍼스 재토큰화 방지) — 쓰기 시 무효화
        self._corpus_cache: dict = {}     # area -> 토큰화된 교사 코퍼스
        self._seed_cache: dict = {}       # area -> 토큰화된 씨드 코퍼스(로드 시에만 변함)
        try:                              # 메인 앱·메모 도구가 동시에 써도 덜 막히게(WAL)
            self._conn.execute("PRAGMA journal_mode=WAL")
        except sqlite3.Error:
            pass
        self._init_schema()

    def _init_schema(self) -> None:
        self._conn.executescript(
            """
            CREATE TABLE IF NOT EXISTS examples (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                area        TEXT NOT NULL,
                subject     TEXT DEFAULT '',
                keywords    TEXT NOT NULL,
                output_text TEXT NOT NULL,
                rating      INTEGER DEFAULT 1,   -- 1=채택, 0=보류
                created_at  REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_examples_area ON examples(area);

            -- 내장 씨드 코퍼스(문장 '형식' 학습용). 교사 데이터와 분리한다.
            CREATE TABLE IF NOT EXISTS seed_examples (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                area        TEXT NOT NULL,
                subject     TEXT DEFAULT '',
                keywords    TEXT NOT NULL,
                output_text TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_seed_area ON seed_examples(area);

            -- 수업 중 빠른 메모(트레이 도구가 기록, 본 앱이 소비) — 학생·학급별 관찰
            CREATE TABLE IF NOT EXISTS memos (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at  REAL NOT NULL,
                klass       TEXT DEFAULT '',   -- 학급(예: 1반)
                student     TEXT DEFAULT '',   -- 학생(학번/이름)
                area        TEXT DEFAULT '',   -- 영역(선택; 나중에 배정 가능)
                subject     TEXT DEFAULT '',   -- 과목(세특)
                text        TEXT NOT NULL,
                used        INTEGER DEFAULT 0  -- 생성에 반영했는지
            );
            CREATE INDEX IF NOT EXISTS idx_memos_used ON memos(used);

            CREATE TABLE IF NOT EXISTS meta (k TEXT PRIMARY KEY, v TEXT);
            """
        )
        self._conn.commit()

    # ---- 수업 메모 -------------------------------------------------------
    def add_memo(self, *, text: str, klass: str = "", student: str = "",
                 area: str = "", subject: str = "") -> int:
        t = (text or "").strip()
        if not t:
            return 0
        cur = self._conn.execute(
            "INSERT INTO memos(created_at, klass, student, area, subject, text, used)"
            " VALUES(?,?,?,?,?,?,0)",
            (time.time(), klass or "", student or "", area or "", subject or "", t))
        self._conn.commit()
        return int(cur.lastrowid)

    def list_memos(self, *, include_used: bool = False, limit: int = 1000) -> list[dict]:
        q = "SELECT * FROM memos"
        if not include_used:
            q += " WHERE used=0"
        q += " ORDER BY created_at DESC LIMIT ?"
        return [dict(r) for r in self._conn.execute(q, (limit,)).fetchall()]

    def memo_count(self, *, include_used: bool = False) -> int:
        q = "SELECT COUNT(*) FROM memos"
        if not include_used:
            q += " WHERE used=0"
        return self._conn.execute(q).fetchone()[0]

    def set_memo_used(self, memo_id: int, used: int = 1) -> None:
        self._conn.execute("UPDATE memos SET used=? WHERE id=?", (used, memo_id))
        self._conn.commit()

    def delete_memo(self, memo_id: int) -> None:
        self._conn.execute("DELETE FROM memos WHERE id=?", (memo_id,))
        self._conn.commit()

    # ---- 쓰기 -------------------------------------------------------------
    def add_example(self, *, area: str, subject: str, keywords: str,
                    output_text: str, rating: int = 1) -> int:
        cur = self._conn.execute(
            "INSERT INTO examples(area, subject, keywords, output_text, rating, created_at)"
            " VALUES(?,?,?,?,?,?)",
            (area, subject or "", keywords, output_text, rating, time.time()),
        )
        self._conn.commit()
        self._corpus_cache.pop(area, None)         # 이 영역 캐시 무효화
        return int(cur.lastrowid)

    def delete_example(self, example_id: int) -> None:
        self._conn.execute("DELETE FROM examples WHERE id=?", (example_id,))
        self._conn.commit()
        self._corpus_cache.clear()                 # 어느 영역인지 모르니 전체 무효화

    def add_rejection(self, *, area: str, subject: str = "", output_text: str) -> int:
        """교사가 '버린' 표현을 부정 예시(rating=-1)로 저장. few-shot 검색에는 안 뽑히고
        (rating>=1만 검색), 생성 시 회피용으로만 쓰인다. 중복이면 저장 안 함."""
        t = (output_text or "").strip()
        if not t:
            return 0
        dup = self._conn.execute(
            "SELECT 1 FROM examples WHERE area=? AND output_text=? AND rating<0 LIMIT 1",
            (area, t)).fetchone()
        if dup:
            return 0
        cur = self._conn.execute(
            "INSERT INTO examples(area, subject, keywords, output_text, rating, created_at)"
            " VALUES(?,?,?,?,?,?)", (area, subject or "", "", t, -1, time.time()))
        self._conn.commit()
        return int(cur.lastrowid)

    def rejected_texts(self, area: str, limit: int = 300) -> list[str]:
        """교사가 버린 변형(rating<0)들 — 생성 시 유사 출력 회피용."""
        rows = self._conn.execute(
            "SELECT output_text FROM examples WHERE area=? AND rating<0"
            " ORDER BY created_at DESC LIMIT ?", (area, limit)).fetchall()
        return [r["output_text"] for r in rows]

    # ---- 씨드 코퍼스 -------------------------------------------------------
    def load_seed_corpus(self, path) -> int:
        """JSONL 씨드 코퍼스를 적재한다. 파일이 바뀌었을 때만 갱신(idempotent).

        각 줄: {"area","subject","keywords","output"}
        반환: 적재된 총 건수.
        """
        import hashlib
        import json
        from pathlib import Path

        p = Path(path)
        if not p.exists():
            return self.seed_count()
        digest = hashlib.sha256(p.read_bytes()).hexdigest()
        cur = self._conn.execute("SELECT v FROM meta WHERE k='seed_hash'").fetchone()
        if cur and cur[0] == digest and self.seed_count() > 0:
            return self.seed_count()  # 이미 최신

        self._seed_cache.clear()                   # 재적재 시 캐시 무효화
        self._conn.execute("DELETE FROM seed_examples")
        rows = []
        for line in p.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            try:
                o = json.loads(line)
            except json.JSONDecodeError:
                continue
            if not o.get("area") or not o.get("output"):
                continue
            rows.append((o["area"], o.get("subject", ""),
                         o.get("keywords", ""), o["output"]))
        self._conn.executemany(
            "INSERT INTO seed_examples(area, subject, keywords, output_text)"
            " VALUES(?,?,?,?)", rows,
        )
        self._conn.execute(
            "INSERT INTO meta(k, v) VALUES('seed_hash', ?)"
            " ON CONFLICT(k) DO UPDATE SET v=excluded.v", (digest,)
        )
        self._conn.commit()
        return len(rows)

    def seed_count(self, area: str | None = None) -> int:
        if area:
            return self._conn.execute(
                "SELECT COUNT(*) FROM seed_examples WHERE area=?", (area,)
            ).fetchone()[0]
        return self._conn.execute("SELECT COUNT(*) FROM seed_examples").fetchone()[0]

    def _seed_rows_for_area(self, area: str) -> list[Example]:
        rows = self._conn.execute(
            "SELECT * FROM seed_examples WHERE area=?", (area,)
        ).fetchall()
        return [
            Example(-1, area, r["subject"], r["keywords"], r["output_text"], 1, 0.0)
            for r in rows
        ]

    def retrieve_seed(self, *, area: str, query: str, k: int,
                      subject: str = "") -> list[Example]:
        """씨드 코퍼스에서 유사한 '형식' 예시 k 개. subject 가 있으면 같은 과목 우선."""
        docs = self._seed_rows_for_area(area)
        if not docs:
            return []
        cached = self._seed_cache.get(area)
        if cached and cached[0] == len(docs):
            corpus = cached[1]
        else:
            corpus = [tokenize(f"{d.keywords} {d.subject}") for d in docs]
            self._seed_cache[area] = (len(docs), corpus)
        scores = _bm25_scores(tokenize(query), corpus)
        scores = _boost_subject(scores, docs, subject)
        ranked = sorted(zip(scores, docs), key=lambda x: x[0], reverse=True)
        return [d for _, d in ranked[:k]]

    # ---- 읽기 -------------------------------------------------------------
    def _rows_for_area(self, area: str) -> list[Example]:
        rows = self._conn.execute(
            "SELECT * FROM examples WHERE area=? AND rating>=1", (area,)
        ).fetchall()
        return [
            Example(r["id"], r["area"], r["subject"], r["keywords"],
                    r["output_text"], r["rating"], r["created_at"])
            for r in rows
        ]

    def list_examples(self, area: str | None = None, limit: int = 200) -> list[Example]:
        if area:
            rows = self._conn.execute(
                "SELECT * FROM examples WHERE area=? ORDER BY created_at DESC LIMIT ?",
                (area, limit),
            ).fetchall()
        else:
            rows = self._conn.execute(
                "SELECT * FROM examples ORDER BY created_at DESC LIMIT ?", (limit,)
            ).fetchall()
        return [
            Example(r["id"], r["area"], r["subject"], r["keywords"],
                    r["output_text"], r["rating"], r["created_at"])
            for r in rows
        ]

    def count(self, area: str | None = None) -> int:
        if area:
            return self._conn.execute(
                "SELECT COUNT(*) FROM examples WHERE area=?", (area,)
            ).fetchone()[0]
        return self._conn.execute("SELECT COUNT(*) FROM examples").fetchone()[0]

    # ---- 검색(BM25) -------------------------------------------------------
    def retrieve(self, *, area: str, query: str, k: int = config.FEWSHOT_K,
                 subject: str = "") -> list[Example]:
        """같은 영역에서 query 와 가장 유사한 과거 예시 k 개를 반환.

        subject 가 주어지면 같은 과목 예시를 우선한다(과목별 세분화 효과).
        """
        docs = self._rows_for_area(area)
        if not docs:
            return []
        cached = self._corpus_cache.get(area)
        if cached and cached[0] == len(docs):
            corpus = cached[1]
        else:
            corpus = [tokenize(f"{d.keywords} {d.subject}") for d in docs]
            self._corpus_cache[area] = (len(docs), corpus)
        scores = _bm25_scores(tokenize(query), corpus)
        scores = _boost_subject(scores, docs, subject)
        ranked = sorted(zip(scores, docs), key=lambda x: x[0], reverse=True)
        return [d for s, d in ranked[:k] if s > 0] or [d for _, d in ranked[:k]]

    # ---- 백업 / 복원(교사 고유 학습 데이터 이전) -------------------------
    def export_to(self, path) -> int:
        """학습 데이터(examples)를 별도 파일로 백업한다. 반환: 백업된 예시 수."""
        dest = sqlite3.connect(str(path))
        try:
            self._conn.backup(dest)
        finally:
            dest.close()
        return self.count()

    def import_merge(self, path) -> int:
        """다른 백업 파일의 examples 를 현재 DB에 병합한다(중복 제외). 반환: 추가된 수."""
        src = sqlite3.connect(str(path))
        try:
            rows = src.execute(
                "SELECT area, subject, keywords, output_text, rating, created_at FROM examples"
            ).fetchall()
        except sqlite3.Error:
            src.close()
            raise ValueError("올바른 학습 백업 파일이 아닙니다.")
        src.close()

        existing = {
            (r["area"], r["output_text"])
            for r in self._conn.execute("SELECT area, output_text FROM examples")
        }
        added = 0
        for area, subject, keywords, output_text, rating, created_at in rows:
            if (area, output_text) in existing:
                continue
            self._conn.execute(
                "INSERT INTO examples(area, subject, keywords, output_text, rating, created_at)"
                " VALUES(?,?,?,?,?,?)",
                (area, subject, keywords, output_text, rating, created_at),
            )
            existing.add((area, output_text))
            added += 1
        self._conn.commit()
        return added

    def close(self) -> None:
        self._conn.close()


def _norm_subject(s: str) -> str:
    return (s or "").strip().lower()


def _boost_subject(scores: list[float], docs: list[Example], subject: str,
                   bonus: float = 1000.0) -> list[float]:
    """같은 과목 예시를 확실히 우선시킨다(같은 과목 안에서는 키워드 점수로 순위).

    같은 과목이면 사용하는 서술어가 비슷하므로, 과목 일치를 키워드 매칭보다 앞세운다.
    """
    subj = _norm_subject(subject)
    if not subj:
        return scores
    return [
        s + bonus if _norm_subject(d.subject) == subj else s
        for s, d in zip(scores, docs)
    ]


def _bm25_scores(query_tokens: list[str], corpus: list[list[str]],
                 k1: float = 1.5, b: float = 0.75) -> list[float]:
    """표준 BM25. corpus 각 문서에 대한 query 점수 리스트."""
    n = len(corpus)
    if n == 0:
        return []
    doc_len = [len(d) for d in corpus]
    avgdl = sum(doc_len) / n if n else 0.0

    # document frequency
    df: dict[str, int] = {}
    for doc in corpus:
        for term in set(doc):
            df[term] = df.get(term, 0) + 1

    # term frequency per doc
    tf = [{} for _ in corpus]
    for i, doc in enumerate(corpus):
        for term in doc:
            tf[i][term] = tf[i].get(term, 0) + 1

    scores = [0.0] * n
    q_terms = set(query_tokens)
    for term in q_terms:
        if term not in df:
            continue
        idf = math.log(1 + (n - df[term] + 0.5) / (df[term] + 0.5))
        for i in range(n):
            f = tf[i].get(term, 0)
            if f == 0:
                continue
            denom = f + k1 * (1 - b + b * (doc_len[i] / avgdl if avgdl else 0))
            scores[i] += idf * (f * (k1 + 1)) / denom
    return scores
