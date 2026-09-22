PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS accounts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    sort_order INTEGER NOT NULL DEFAULT 0,
    is_default INTEGER NOT NULL DEFAULT 0 CHECK(is_default IN (0, 1)),
    created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS category_groups (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    kind TEXT NOT NULL CHECK(kind IN ('income', 'expense')),
    name TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    UNIQUE(kind, name)
);

CREATE TABLE IF NOT EXISTS categories (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    kind TEXT NOT NULL CHECK(kind IN ('income', 'expense')),
    group_id INTEGER NOT NULL REFERENCES category_groups(id) ON DELETE RESTRICT,
    name TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    is_favorite INTEGER NOT NULL DEFAULT 0 CHECK(is_favorite IN (0, 1)),
    created_at TEXT NOT NULL,
    UNIQUE(kind, name)
);

CREATE TABLE IF NOT EXISTS transactions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    tx_date TEXT NOT NULL,
    account_name TEXT NOT NULL,
    kind TEXT NOT NULL CHECK(kind IN ('income', 'expense')),
    category_name TEXT NOT NULL,
    summary TEXT NOT NULL DEFAULT '',
    amount INTEGER NOT NULL CHECK(amount BETWEEN 1 AND 9999999),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_transactions_date ON transactions(tx_date);
CREATE INDEX IF NOT EXISTS idx_transactions_month ON transactions(substr(tx_date, 1, 7));
CREATE INDEX IF NOT EXISTS idx_transactions_account ON transactions(account_name);

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

INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '1');
INSERT OR IGNORE INTO accounts(name, sort_order, is_default, created_at)
VALUES ('現金', 0, 1, datetime('now'));

INSERT OR IGNORE INTO category_groups(kind, name, sort_order, created_at)
VALUES ('income', '收入分類', 0, datetime('now'));
INSERT OR IGNORE INTO category_groups(kind, name, sort_order, created_at)
VALUES ('expense', '支出分類', 0, datetime('now'));

INSERT OR IGNORE INTO categories(kind, group_id, name, sort_order, is_favorite, created_at)
SELECT 'income', id, '一般收入', 0, 0, datetime('now')
FROM category_groups WHERE kind = 'income' AND name = '收入分類';
INSERT OR IGNORE INTO categories(kind, group_id, name, sort_order, is_favorite, created_at)
SELECT 'expense', id, '一般支出', 0, 0, datetime('now')
FROM category_groups WHERE kind = 'expense' AND name = '支出分類';
