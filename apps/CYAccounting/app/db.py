from __future__ import annotations

import sqlite3
from contextlib import contextmanager
from datetime import datetime
from pathlib import Path
from typing import Iterable, Optional

from util import (
    MAX_AMOUNT,
    DB_FILENAME,
    SaveResult,
    format_date,
    index_to_month,
    is_month_locked,
    month_bounds,
    month_key_from_date,
    month_to_index,
    normalize_name,
    parse_date,
    shift_month,
    text_sort_key,
)

SCHEMA_VERSION = 2


class DatabaseError(RuntimeError):
    pass


class Database:
    def __init__(self, path: Path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.conn: sqlite3.Connection | None = None
        self.open()

    def open(self) -> None:
        if self.conn is not None:
            self.conn.close()
        existed = self.path.exists() and self.path.stat().st_size > 0
        self.conn = sqlite3.connect(str(self.path), timeout=15)
        self.conn.row_factory = sqlite3.Row
        self.conn.execute("PRAGMA foreign_keys=ON")
        if existed:
            ok, message = self.integrity_check(require_application_schema=True)
            if not ok:
                self.conn.close(); self.conn = None
                raise DatabaseError(message)
        self.conn.execute("PRAGMA journal_mode=WAL")
        self.conn.execute("PRAGMA synchronous=FULL")
        self._init_schema()

    def close(self) -> None:
        if self.conn is not None:
            self.conn.close()
            self.conn = None

    @contextmanager
    def tx(self):
        assert self.conn is not None
        try:
            self.conn.execute("BEGIN IMMEDIATE")
            yield self.conn
            self.conn.commit()
        except Exception:
            self.conn.rollback()
            raise

    def _init_schema(self) -> None:
        assert self.conn is not None
        self.conn.executescript(
            """
            CREATE TABLE IF NOT EXISTS meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS accounts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                sort_order INTEGER NOT NULL,
                is_default INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS category_groups (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                kind TEXT NOT NULL CHECK(kind IN ('income','expense')),
                name TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(kind, name)
            );

            CREATE TABLE IF NOT EXISTS categories (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                kind TEXT NOT NULL CHECK(kind IN ('income','expense')),
                group_id INTEGER NOT NULL REFERENCES category_groups(id) ON DELETE RESTRICT,
                name TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                is_favorite INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                UNIQUE(kind, name)
            );

            CREATE TABLE IF NOT EXISTS transactions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tx_date TEXT NOT NULL,
                account_name TEXT NOT NULL,
                kind TEXT NOT NULL CHECK(kind IN ('income','expense')),
                category_name TEXT NOT NULL,
                summary TEXT NOT NULL DEFAULT '',
                amount INTEGER NOT NULL CHECK(amount >= 1),
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_transactions_date ON transactions(tx_date);
            CREATE INDEX IF NOT EXISTS idx_transactions_month ON transactions(substr(tx_date,1,7));

            CREATE TABLE IF NOT EXISTS opening_balances (
                month TEXT NOT NULL,
                account_name TEXT NOT NULL,
                amount INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY(month, account_name)
            );

            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """
        )
        # Schema migration: V2 adds a per-category quick-access flag.
        category_columns = {row[1] for row in self.conn.execute("PRAGMA table_info(categories)").fetchall()}
        if "is_favorite" not in category_columns:
            self.conn.execute("ALTER TABLE categories ADD COLUMN is_favorite INTEGER NOT NULL DEFAULT 0")

        self.conn.execute(
            "INSERT OR REPLACE INTO meta(key,value) VALUES('schema_version',?)",
            (str(SCHEMA_VERSION),),
        )
        self._seed_defaults()
        self.conn.commit()

    def _seed_defaults(self) -> None:
        assert self.conn is not None
        now = datetime.now().isoformat(timespec="seconds")
        count = self.conn.execute("SELECT COUNT(*) FROM accounts").fetchone()[0]
        if count == 0:
            self.conn.execute(
                "INSERT INTO accounts(name,sort_order,is_default,created_at) VALUES(?,?,1,?)",
                ("現金", 0, now),
            )
        for kind, gname, cname in (
            ("income", "收入分類", "一般收入"),
            ("expense", "支出分類", "一般支出"),
        ):
            # Seed a usable default only when that side has no actual category at all.
            # Existing user-defined categories must never cause "收入分類/一般收入" or
            # "支出分類/一般支出" to be injected on a later startup.
            category_count = self.conn.execute(
                "SELECT COUNT(*) FROM categories WHERE kind=?", (kind,)
            ).fetchone()[0]
            if int(category_count) > 0:
                continue
            g = self.conn.execute(
                "SELECT id FROM category_groups WHERE kind=? AND name=?",
                (kind, gname),
            ).fetchone()
            if not g:
                cur = self.conn.execute(
                    "INSERT INTO category_groups(kind,name,sort_order,created_at) VALUES(?,?,0,?)",
                    (kind, gname, now),
                )
                gid = cur.lastrowid
            else:
                gid = g[0]
            self.conn.execute(
                "INSERT INTO categories(kind,group_id,name,sort_order,created_at) VALUES(?,?,?,?,?)",
                (kind, gid, cname, 0, now),
            )

    @staticmethod
    def _validate_connection(con: sqlite3.Connection, require_application_schema: bool = True) -> tuple[bool, str]:
        try:
            check = con.execute("PRAGMA integrity_check").fetchone()[0]
            if check != "ok":
                return False, f"資料庫完整性檢查失敗：{check}"
            if require_application_schema:
                tables = {r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'").fetchall()}
                required = {"meta", "accounts", "category_groups", "categories", "transactions", "opening_balances", "app_settings"}
                missing = sorted(required - tables)
                if missing:
                    return False, "不是志遠記帳系統資料庫（缺少必要資料表）"
                row = con.execute("SELECT value FROM meta WHERE key='schema_version'").fetchone()
                if not row:
                    return False, "不是志遠記帳系統資料庫"
                try:
                    version = int(row[0])
                except Exception:
                    return False, "志遠記帳系統資料庫版本紀錄無效"
                if version < 1 or version > SCHEMA_VERSION:
                    return False, f"不支援的志遠記帳系統資料庫版本：{version}"
                foreign_key_errors = con.execute("PRAGMA foreign_key_check").fetchall()
                if foreign_key_errors:
                    return False, "資料庫關聯完整性檢查失敗"
            return True, ""
        except Exception as exc:
            return False, str(exc)

    def integrity_check(self, require_application_schema: bool = True) -> tuple[bool, str]:
        assert self.conn is not None
        return self._validate_connection(self.conn, require_application_schema)

    def checkpoint(self) -> None:
        assert self.conn is not None
        self.conn.execute("PRAGMA wal_checkpoint(FULL)")

    def validate_database_file(self, path: Path) -> tuple[bool, str]:
        path = Path(path)
        if not path.exists() or path.stat().st_size <= 0:
            return False, "資料庫檔案不存在或為空白"
        con = None
        try:
            con = sqlite3.connect(str(path), timeout=10)
            return self._validate_connection(con, True)
        except Exception as e:
            return False, str(e)
        finally:
            if con is not None:
                con.close()

    def backup_to(self, dst: Path) -> None:
        assert self.conn is not None
        dst.parent.mkdir(parents=True, exist_ok=True)
        tmp = dst.with_suffix(dst.suffix + ".tmp")
        if tmp.exists():
            tmp.unlink()
        out = sqlite3.connect(str(tmp))
        try:
            self.conn.backup(out)
            out.commit()
            ok, message = self._validate_connection(out, True)
            if not ok:
                raise DatabaseError(f"備份驗證失敗：{message}")
        except Exception:
            out.close()
            tmp.unlink(missing_ok=True)
            raise
        finally:
            try:
                out.close()
            except Exception:
                pass
        tmp.replace(dst)

    # ---------- settings ----------
    def get_setting(self, key: str, default: str = "") -> str:
        assert self.conn is not None
        row = self.conn.execute("SELECT value FROM app_settings WHERE key=?", (key,)).fetchone()
        return row[0] if row else default

    def set_setting(self, key: str, value: str) -> None:
        assert self.conn is not None
        self.conn.execute(
            "INSERT INTO app_settings(key,value) VALUES(?,?) "
            "ON CONFLICT(key) DO UPDATE SET value=excluded.value",
            (key, value),
        )
        self.conn.commit()

    def locked_through(self) -> str | None:
        v = self.get_setting("locked_through", "").strip()
        return v or None

    def set_locked_through(self, month: str | None) -> None:
        self.set_setting("locked_through", month or "")

    # ---------- accounts ----------
    def accounts(self) -> list[dict]:
        assert self.conn is not None
        rows = self.conn.execute(
            "SELECT id,name,sort_order,is_default FROM accounts ORDER BY sort_order,id"
        ).fetchall()
        return [dict(r) for r in rows]

    def default_account(self) -> str:
        assert self.conn is not None
        row = self.conn.execute(
            "SELECT name FROM accounts ORDER BY is_default DESC,sort_order,id LIMIT 1"
        ).fetchone()
        return row[0]

    def add_account(self, name: str) -> None:
        name = normalize_name(name)
        assert self.conn is not None
        now = datetime.now().isoformat(timespec="seconds")
        order = self.conn.execute("SELECT COALESCE(MAX(sort_order),-1)+1 FROM accounts").fetchone()[0]
        self.conn.execute(
            "INSERT INTO accounts(name,sort_order,is_default,created_at) VALUES(?,?,0,?)",
            (name, order, now),
        )
        self.conn.commit()

    def rename_account(self, account_id: int, name: str) -> None:
        assert self.conn is not None
        self.conn.execute("UPDATE accounts SET name=? WHERE id=?", (normalize_name(name), account_id))
        self.conn.commit()

    def delete_account(self, account_id: int) -> None:
        assert self.conn is not None
        with self.tx() as con:
            count = con.execute("SELECT COUNT(*) FROM accounts").fetchone()[0]
            if count <= 1:
                raise DatabaseError("帳戶至少需要保留一個")
            row = con.execute("SELECT is_default FROM accounts WHERE id=?", (account_id,)).fetchone()
            if not row:
                return
            was_default = bool(row[0])
            con.execute("DELETE FROM accounts WHERE id=?", (account_id,))
            self._normalize_account_order(con)
            if was_default:
                first = con.execute("SELECT id FROM accounts ORDER BY sort_order,id LIMIT 1").fetchone()[0]
                con.execute("UPDATE accounts SET is_default=CASE WHEN id=? THEN 1 ELSE 0 END", (first,))

    def set_default_account(self, account_id: int) -> None:
        assert self.conn is not None
        self.conn.execute("UPDATE accounts SET is_default=CASE WHEN id=? THEN 1 ELSE 0 END", (account_id,))
        self.conn.commit()

    def move_account(self, account_id: int, delta: int) -> None:
        assert self.conn is not None
        rows = self.conn.execute("SELECT id FROM accounts ORDER BY sort_order,id").fetchall()
        ids = [r[0] for r in rows]
        if account_id not in ids:
            return
        i = ids.index(account_id)
        j = i + delta
        if not 0 <= j < len(ids):
            return
        ids[i], ids[j] = ids[j], ids[i]
        with self.tx() as con:
            for order, aid in enumerate(ids):
                con.execute("UPDATE accounts SET sort_order=? WHERE id=?", (order, aid))

    @staticmethod
    def _normalize_account_order(con: sqlite3.Connection) -> None:
        ids = [r[0] for r in con.execute("SELECT id FROM accounts ORDER BY sort_order,id")]
        for order, aid in enumerate(ids):
            con.execute("UPDATE accounts SET sort_order=? WHERE id=?", (order, aid))

    # ---------- categories ----------
    def category_tree(self, kind: str) -> list[dict]:
        assert self.conn is not None
        groups = self.conn.execute(
            "SELECT id,name,sort_order FROM category_groups WHERE kind=? ORDER BY sort_order,id",
            (kind,),
        ).fetchall()
        out = []
        for g in groups:
            cats = self.conn.execute(
                "SELECT id,name,sort_order,is_favorite FROM categories WHERE kind=? AND group_id=? ORDER BY sort_order,id",
                (kind, g["id"]),
            ).fetchall()
            out.append({"id": g["id"], "name": g["name"], "sort_order": g["sort_order"], "categories": [dict(c) for c in cats]})
        return out

    def category_names(self, kind: str) -> list[str]:
        return [c["name"] for g in self.category_tree(kind) for c in g["categories"]]

    def favorite_category_names(self, kind: str) -> list[str]:
        """Return favorite categories in the same custom order used by category management."""
        return [
            c["name"]
            for g in self.category_tree(kind)
            for c in g["categories"]
            if int(c.get("is_favorite", 0))
        ][:10]

    def frequent_summaries(
        self,
        kind: str,
        account: str,
        category: str,
        recent_n: int = 100,
        min_count: int = 3,
        limit: int = 10,
        basis: str = "tx_date",
    ) -> list[str]:
        """Return frequent summaries for one account + kind + category.

        ``basis='tx_date'`` samples by accounting date (newest date first, then
        entry time/id); ``basis='created_at'`` samples by recent entry time.
        Blank summaries are ignored after the N-row sampling window is selected.
        Ranking is frequency first, then the most recent appearance *within the
        selected basis*.
        """
        assert self.conn is not None
        recent_n = max(1, int(recent_n))
        min_count = max(1, int(min_count))
        limit = max(1, int(limit))
        order_sql = (
            "tx_date DESC,created_at DESC,id DESC"
            if basis == "tx_date"
            else "created_at DESC,id DESC"
        )
        rows = self.conn.execute(
            "SELECT summary,tx_date,created_at,id FROM transactions "
            "WHERE kind=? AND account_name=? AND category_name=? "
            f"ORDER BY {order_sql} LIMIT ?",
            (kind, account, category, recent_n),
        ).fetchall()
        stats: dict[str, dict[str, object]] = {}
        for rank, row in enumerate(rows):
            summary = (row["summary"] or "").strip()
            if not summary:
                continue
            item = stats.setdefault(summary, {"count": 0, "latest_rank": rank})
            item["count"] = int(item["count"]) + 1
        ranked = [
            (summary, int(meta["count"]), int(meta["latest_rank"]))
            for summary, meta in stats.items()
            if int(meta["count"]) >= min_count
        ]
        ranked.sort(key=lambda item: (-item[1], item[2]))
        return [item[0] for item in ranked[:limit]]

    def set_category_favorite(self, category_id: int, favorite: bool) -> None:
        assert self.conn is not None
        row = self.conn.execute("SELECT kind,is_favorite FROM categories WHERE id=?", (category_id,)).fetchone()
        if not row:
            return
        if favorite and not int(row["is_favorite"]):
            count = self.conn.execute(
                "SELECT COUNT(*) FROM categories WHERE kind=? AND is_favorite=1", (row["kind"],)
            ).fetchone()[0]
            if int(count) >= 10:
                raise DatabaseError("常用科目最多設定10個。")
        self.conn.execute("UPDATE categories SET is_favorite=? WHERE id=?", (1 if favorite else 0, category_id))
        self.conn.commit()

    def add_group(self, kind: str, name: str) -> None:
        assert self.conn is not None
        now = datetime.now().isoformat(timespec="seconds")
        order = self.conn.execute(
            "SELECT COALESCE(MAX(sort_order),-1)+1 FROM category_groups WHERE kind=?",
            (kind,),
        ).fetchone()[0]
        self.conn.execute(
            "INSERT INTO category_groups(kind,name,sort_order,created_at) VALUES(?,?,?,?)",
            (kind, normalize_name(name), order, now),
        )
        self.conn.commit()

    def rename_group(self, group_id: int, name: str) -> None:
        assert self.conn is not None
        self.conn.execute("UPDATE category_groups SET name=? WHERE id=?", (normalize_name(name), group_id))
        self.conn.commit()

    def delete_group(self, group_id: int) -> None:
        assert self.conn is not None
        count = self.conn.execute("SELECT COUNT(*) FROM categories WHERE group_id=?", (group_id,)).fetchone()[0]
        if count:
            raise DatabaseError("此大分類仍包含科目，請先移動或刪除其科目")
        self.conn.execute("DELETE FROM category_groups WHERE id=?", (group_id,))
        self.conn.commit()

    def move_group(self, group_id: int, delta: int) -> None:
        assert self.conn is not None
        row = self.conn.execute("SELECT kind FROM category_groups WHERE id=?", (group_id,)).fetchone()
        if not row:
            return
        kind = row[0]
        ids = [r[0] for r in self.conn.execute("SELECT id FROM category_groups WHERE kind=? ORDER BY sort_order,id", (kind,))]
        if group_id not in ids:
            return
        i = ids.index(group_id); j = i + delta
        if not 0 <= j < len(ids):
            return
        ids[i], ids[j] = ids[j], ids[i]
        with self.tx() as con:
            for order, gid in enumerate(ids):
                con.execute("UPDATE category_groups SET sort_order=? WHERE id=?", (order, gid))

    def add_category(self, kind: str, group_id: int, name: str) -> None:
        assert self.conn is not None
        now = datetime.now().isoformat(timespec="seconds")
        order = self.conn.execute(
            "SELECT COALESCE(MAX(sort_order),-1)+1 FROM categories WHERE kind=? AND group_id=?",
            (kind, group_id),
        ).fetchone()[0]
        self.conn.execute(
            "INSERT INTO categories(kind,group_id,name,sort_order,created_at) VALUES(?,?,?,?,?)",
            (kind, group_id, normalize_name(name), order, now),
        )
        self.conn.commit()

    def rename_category(self, category_id: int, name: str) -> None:
        assert self.conn is not None
        self.conn.execute("UPDATE categories SET name=? WHERE id=?", (normalize_name(name), category_id))
        self.conn.commit()

    def move_category_to_group(self, category_id: int, group_id: int) -> None:
        assert self.conn is not None
        row = self.conn.execute("SELECT kind FROM categories WHERE id=?", (category_id,)).fetchone()
        if not row:
            return
        kind = row[0]
        order = self.conn.execute(
            "SELECT COALESCE(MAX(sort_order),-1)+1 FROM categories WHERE kind=? AND group_id=?",
            (kind, group_id),
        ).fetchone()[0]
        self.conn.execute("UPDATE categories SET group_id=?,sort_order=? WHERE id=?", (group_id, order, category_id))
        self.conn.commit()

    def delete_category(self, category_id: int) -> None:
        assert self.conn is not None
        self.conn.execute("DELETE FROM categories WHERE id=?", (category_id,))
        self.conn.commit()

    def move_category(self, category_id: int, delta: int) -> None:
        assert self.conn is not None
        row = self.conn.execute("SELECT kind,group_id FROM categories WHERE id=?", (category_id,)).fetchone()
        if not row:
            return
        ids = [r[0] for r in self.conn.execute(
            "SELECT id FROM categories WHERE kind=? AND group_id=? ORDER BY sort_order,id", (row[0], row[1])
        )]
        if category_id not in ids:
            return
        i = ids.index(category_id); j = i + delta
        if not 0 <= j < len(ids):
            return
        ids[i], ids[j] = ids[j], ids[i]
        with self.tx() as con:
            for order, cid in enumerate(ids):
                con.execute("UPDATE categories SET sort_order=? WHERE id=?", (order, cid))

    # ---------- opening balances ----------
    def opening_balances(self, month: str) -> dict[str, int]:
        assert self.conn is not None
        rows = self.conn.execute(
            "SELECT account_name,amount FROM opening_balances WHERE month=?",
            (month,),
        ).fetchall()
        return {r[0]: int(r[1]) for r in rows}

    def set_opening_balances(self, month: str, values: dict[str, int | None]) -> None:
        assert self.conn is not None
        if is_month_locked(month, self.locked_through()):
            raise DatabaseError(f"{month} 已鎖定，無法修改期初餘額")
        now = datetime.now().isoformat(timespec="seconds")
        with self.tx() as con:
            for account, amount in values.items():
                if amount is None:
                    con.execute(
                        "DELETE FROM opening_balances WHERE month=? AND account_name=?",
                        (month, account),
                    )
                else:
                    con.execute(
                        "INSERT INTO opening_balances(month,account_name,amount,created_at,updated_at) VALUES(?,?,?,?,?) "
                        "ON CONFLICT(month,account_name) DO UPDATE SET amount=excluded.amount,updated_at=excluded.updated_at",
                        (month, account, int(amount), now, now),
                    )

    def relevant_accounts_for_month(self, month: str) -> list[tuple[str, bool]]:
        """Return (name, is_current) ordered current first, then historical."""
        assert self.conn is not None
        current = [a["name"] for a in self.accounts()]
        start, end = month_bounds(month)
        tx_names = [r[0] for r in self.conn.execute(
            "SELECT DISTINCT account_name FROM transactions WHERE tx_date BETWEEN ? AND ?", (start, end)
        )]
        ob_names = [r[0] for r in self.conn.execute(
            "SELECT account_name FROM opening_balances WHERE month=?", (month,)
        )]
        seen = set()
        out: list[tuple[str, bool]] = []
        for n in current:
            if n not in seen:
                out.append((n, True)); seen.add(n)
        historical = sorted({*tx_names, *ob_names} - seen, key=text_sort_key)
        out.extend((n, False) for n in historical)
        return out

    def month_has_any_opening(self, month: str) -> bool:
        assert self.conn is not None
        return bool(self.conn.execute("SELECT 1 FROM opening_balances WHERE month=? LIMIT 1", (month,)).fetchone())

    # ---------- transactions ----------
    def month_has_transactions(self, month: str) -> bool:
        assert self.conn is not None
        start, end = month_bounds(month)
        return bool(self.conn.execute(
            "SELECT 1 FROM transactions WHERE tx_date BETWEEN ? AND ? LIMIT 1", (start, end)
        ).fetchone())

    def latest_transaction_month(self) -> str | None:
        assert self.conn is not None
        row = self.conn.execute("SELECT MAX(tx_date) FROM transactions").fetchone()
        if not row or not row[0]:
            return None
        return row[0][:7]

    def earliest_data_month(self) -> str | None:
        assert self.conn is not None
        row1 = self.conn.execute("SELECT MIN(substr(tx_date,1,7)) FROM transactions").fetchone()[0]
        row2 = self.conn.execute("SELECT MIN(month) FROM opening_balances").fetchone()[0]
        values = [v for v in (row1, row2) if v]
        return min(values, key=month_to_index) if values else None

    def save_transaction(
        self, tx_date: str, account: str, kind: str, category: str, summary: str,
        amount: int, opening_values: dict[str, int] | None = None,
    ) -> SaveResult:
        d = parse_date(tx_date)
        if d is None:
            return SaveResult(False, "日期格式或日期內容不正確")
        month = month_key_from_date(d)
        if is_month_locked(month, self.locked_through()):
            return SaveResult(False, f"{month} 已鎖定，無法新增資料")
        if not account:
            return SaveResult(False, "尚未選擇帳戶")
        if not category:
            return SaveResult(False, f"尚未選擇{'收入' if kind == 'income' else '支出'}科目")
        if int(amount) < 1:
            return SaveResult(False, "金額必須大於0")
        if int(amount) > MAX_AMOUNT:
            return SaveResult(False, "金額最多7位數")
        assert self.conn is not None
        now = datetime.now().isoformat(timespec="microseconds")
        try:
            with self.tx() as con:
                if opening_values is not None:
                    for opening_account, opening_amount in opening_values.items():
                        con.execute(
                            "INSERT INTO opening_balances(month,account_name,amount,created_at,updated_at) VALUES(?,?,?,?,?) "
                            "ON CONFLICT(month,account_name) DO UPDATE SET amount=excluded.amount,updated_at=excluded.updated_at",
                            (month, opening_account, int(opening_amount), now, now),
                        )
                cur = con.execute(
                    "INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at) "
                    "VALUES(?,?,?,?,?,?,?,?)",
                    (tx_date, account, kind, category, summary.strip(), int(amount), now, now),
                )
                tx_id = int(cur.lastrowid)
            return SaveResult(True, "", tx_id)
        except Exception as e:
            return SaveResult(False, f"資料庫寫入失敗：{e}")


    def import_transactions(self, records: list[dict], skip_duplicates: bool = True) -> dict:
        """Bulk-import normalized transactions without changing master data.

        Each record must provide tx_date, account_name, kind, category_name,
        summary and amount.  Locked-month rows are rejected.  Exact duplicates
        compare the six accounting fields and can be skipped.
        """
        assert self.conn is not None
        normalized: list[dict] = []
        errors: list[str] = []
        for item in records:
            row_no = item.get("row_no", "?")
            tx_date = str(item.get("tx_date") or "").strip()
            d = parse_date(tx_date)
            if d is None:
                errors.append(f"第 {row_no} 列：日期錯誤")
                continue
            month = month_key_from_date(d)
            if is_month_locked(month, self.locked_through()):
                errors.append(f"第 {row_no} 列：{month} 已鎖定")
                continue
            account = normalize_name(str(item.get("account_name") or ""))
            kind = str(item.get("kind") or "").strip()
            category = normalize_name(str(item.get("category_name") or ""))
            summary = str(item.get("summary") or "").strip()
            try:
                amount = int(item.get("amount"))
            except Exception:
                amount = 0
            if not account:
                errors.append(f"第 {row_no} 列：帳戶空白")
                continue
            if kind not in ("income", "expense"):
                errors.append(f"第 {row_no} 列：收支類型錯誤")
                continue
            if not category:
                errors.append(f"第 {row_no} 列：科目空白")
                continue
            if amount < 1:
                errors.append(f"第 {row_no} 列：金額必須大於 0")
                continue
            if amount > MAX_AMOUNT:
                errors.append(f"第 {row_no} 列：金額最多7位數")
                continue
            normalized.append({
                "row_no": row_no,
                "tx_date": format_date(d),
                "account_name": account,
                "kind": kind,
                "category_name": category,
                "summary": summary,
                "amount": amount,
            })

        imported = 0
        duplicates = 0
        now_base = datetime.now()
        try:
            with self.tx() as con:
                seen_this_import: set[tuple] = set()
                for idx, item in enumerate(normalized):
                    key = (
                        item["tx_date"], item["account_name"], item["kind"],
                        item["category_name"], item["summary"], int(item["amount"]),
                    )
                    if skip_duplicates:
                        if key in seen_this_import:
                            duplicates += 1
                            continue
                        exists = con.execute(
                            "SELECT 1 FROM transactions WHERE tx_date=? AND account_name=? AND kind=? "
                            "AND category_name=? AND summary=? AND amount=? LIMIT 1",
                            key,
                        ).fetchone()
                        if exists:
                            duplicates += 1
                            seen_this_import.add(key)
                            continue
                    # Preserve worksheet order with deterministic microsecond offsets.
                    stamp = now_base.isoformat(timespec="microseconds")
                    if idx:
                        from datetime import timedelta as _timedelta
                        stamp = (now_base + _timedelta(microseconds=idx)).isoformat(timespec="microseconds")
                    con.execute(
                        "INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at) "
                        "VALUES(?,?,?,?,?,?,?,?)",
                        (
                            item["tx_date"], item["account_name"], item["kind"],
                            item["category_name"], item["summary"], int(item["amount"]),
                            stamp, stamp,
                        ),
                    )
                    seen_this_import.add(key)
                    imported += 1
            return {"imported": imported, "duplicates": duplicates, "errors": errors}
        except Exception as exc:
            raise DatabaseError(f"匯入資料庫失敗：{exc}") from exc

    def delete_transactions(self, ids: Iterable[int]) -> None:
        ids = list({int(x) for x in ids})
        if not ids:
            return
        assert self.conn is not None
        rows = self.conn.execute(
            f"SELECT DISTINCT substr(tx_date,1,7) FROM transactions WHERE id IN ({','.join('?'*len(ids))})",
            ids,
        ).fetchall()
        for r in rows:
            if is_month_locked(r[0], self.locked_through()):
                raise DatabaseError(f"{r[0]} 已鎖定，無法刪除資料")
        self.conn.execute(
            f"DELETE FROM transactions WHERE id IN ({','.join('?'*len(ids))})", ids
        )
        self.conn.commit()

    def get_transaction(self, tx_id: int) -> dict | None:
        assert self.conn is not None
        row = self.conn.execute("SELECT * FROM transactions WHERE id=?", (tx_id,)).fetchone()
        return dict(row) if row else None

    def update_transaction(self, tx_id: int, field: str, value) -> tuple[bool, str]:
        allowed = {"tx_date", "account_name", "category_name", "summary", "amount"}
        if field not in allowed:
            return False, "此欄位不可編輯"
        tx = self.get_transaction(tx_id)
        if not tx:
            return False, "找不到該筆資料"
        old_month = tx["tx_date"][:7]
        if is_month_locked(old_month, self.locked_through()):
            return False, f"{old_month} 已鎖定，無法編輯"
        if field == "tx_date":
            d = parse_date(str(value))
            if not d:
                return False, "日期錯誤"
            new_month = month_key_from_date(d)
            if is_month_locked(new_month, self.locked_through()):
                return False, f"{new_month} 已鎖定，無法移入"
            value = format_date(d)
        elif field == "amount":
            try:
                value = int(value)
            except Exception:
                return False, "金額格式錯誤"
            if value < 1:
                return False, "金額必須大於0"
            if value > MAX_AMOUNT:
                return False, "金額最多7位數"
        elif field in ("account_name", "category_name"):
            value = normalize_name(str(value))
            if not value:
                return False, "欄位不可空白"
        else:
            value = str(value).strip()
        assert self.conn is not None
        try:
            self.conn.execute(
                f"UPDATE transactions SET {field}=?,updated_at=? WHERE id=?",
                (value, datetime.now().isoformat(timespec="microseconds"), tx_id),
            )
            self.conn.commit()
            return True, ""
        except Exception as e:
            self.conn.rollback()
            return False, str(e)

    def update_transaction_full(self, tx_id: int, tx_date: str, account: str, category: str, summary: str, amount: int) -> tuple[bool, str]:
        tx = self.get_transaction(tx_id)
        if not tx:
            return False, "找不到該筆資料"
        old_month = tx["tx_date"][:7]
        if is_month_locked(old_month, self.locked_through()):
            return False, f"{old_month} 已鎖定，無法編輯"
        d = parse_date(tx_date)
        if not d:
            return False, "日期錯誤"
        new_month = month_key_from_date(d)
        if is_month_locked(new_month, self.locked_through()):
            return False, f"{new_month} 已鎖定，無法移入"
        if not account or not category:
            return False, "帳戶與科目不可空白"
        if int(amount) < 1:
            return False, "金額必須大於0"
        # Preserve compatibility with a legacy over-limit record when only its
        # other fields are edited, while preventing every new over-limit value.
        if int(amount) > MAX_AMOUNT and int(amount) != int(tx["amount"]):
            return False, "金額最多7位數"
        assert self.conn is not None
        self.conn.execute(
            "UPDATE transactions SET tx_date=?,account_name=?,category_name=?,summary=?,amount=?,updated_at=? WHERE id=?",
            (format_date(d), account, category, summary.strip(), int(amount), datetime.now().isoformat(timespec="microseconds"), tx_id),
        )
        self.conn.commit()
        return True, ""

    def month_data(self, month: str) -> dict:
        assert self.conn is not None
        start, end = month_bounds(month)
        rows = [dict(r) for r in self.conn.execute(
            "SELECT * FROM transactions WHERE tx_date BETWEEN ? AND ?", (start, end)
        ).fetchall()]
        # Normal ledger order: date -> income before expense -> entry order.
        # Category/summary/amount no longer influence the normal view.
        rows.sort(key=lambda r: (
            r["tx_date"],
            0 if r["kind"] == "income" else 1,
            r["created_at"],
            int(r["id"]),
        ))
        openings = self.opening_balances(month)
        relevant = self.relevant_accounts_for_month(month)
        balances = {name: int(openings.get(name, 0)) for name, _ in relevant}
        missing = [name for name, _ in relevant if name not in openings]
        total = sum(balances.values())
        income_total = 0
        expense_total = 0
        for r in rows:
            if r["account_name"] not in balances:
                balances[r["account_name"]] = 0
                missing.append(r["account_name"])
            amt = int(r["amount"])
            if r["kind"] == "income":
                income_total += amt
                balances[r["account_name"]] += amt
                total += amt
            else:
                expense_total += amt
                balances[r["account_name"]] -= amt
                total -= amt
            r["balance"] = total
            r["account_balance"] = balances[r["account_name"]]
            r["account_balances"] = dict(balances)
        opening_total = sum(int(v) for v in openings.values())
        ending_total = opening_total + income_total - expense_total
        # Tooltip should include current accounts even if not otherwise present.
        current_names = [a["name"] for a in self.accounts()]
        ending_by_account = {name: int(openings.get(name, 0)) for name in set(current_names) | set(openings)}
        for r in rows:
            ending_by_account.setdefault(r["account_name"], 0)
            if r["kind"] == "income":
                ending_by_account[r["account_name"]] += int(r["amount"])
            else:
                ending_by_account[r["account_name"]] -= int(r["amount"])
        return {
            "month": month,
            "rows": rows,
            "openings": openings,
            "relevant_accounts": relevant,
            "missing_openings": sorted(set(missing), key=text_sort_key),
            "opening_total": opening_total,
            "income_total": income_total,
            "expense_total": expense_total,
            "net": income_total - expense_total,
            "ending_total": ending_total,
            "ending_by_account": ending_by_account,
        }

    def previous_month_carry_values(self, target_month: str) -> dict[str, int] | None:
        prev = shift_month(target_month, -1)
        if not (self.month_has_transactions(prev) or self.month_has_any_opening(prev)):
            return None
        data = self.month_data(prev)
        current_names = [a["name"] for a in self.accounts()]
        # Deleted historical accounts are intentionally excluded.
        return {name: int(data["ending_by_account"].get(name, 0)) for name in current_names}

    def counts(self) -> dict[str, int]:
        assert self.conn is not None
        return {
            "transactions": self.conn.execute("SELECT COUNT(*) FROM transactions").fetchone()[0],
            "openings": self.conn.execute("SELECT COUNT(*) FROM opening_balances").fetchone()[0],
        }
