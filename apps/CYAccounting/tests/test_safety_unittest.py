from __future__ import annotations

import json
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "app"))

import util
from db import Database


class VersionTests(unittest.TestCase):
    def test_runtime_version_matches_version_and_build_files(self):
        project = Path(__file__).resolve().parents[1]
        version = (project / "VERSION").read_text(encoding="utf-8").strip()
        build = int((project / "BUILD").read_text(encoding="utf-8").strip())
        expected = f"V{version}" + (f" Build {build}" if build else "")
        self.assertEqual(util.APP_VERSION, expected)


class ConfigSafetyTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.original_config_path = util.config_path
        util.config_path = lambda: self.root / "config.json"

    def tearDown(self):
        util.config_path = self.original_config_path

    def test_corrupt_current_config_recovers_previous_valid_copy(self):
        util.save_config({"database_path": "D:/CY/first.db", "value": 1})
        util.save_config({"database_path": "D:/CY/second.db", "value": 2})
        util.config_path().write_text("{broken", encoding="utf-8")

        recovered, warning = util.load_config_with_status()

        self.assertEqual(recovered["database_path"], "D:/CY/first.db")
        self.assertIn("安全設定復原", warning)
        self.assertTrue(list(self.root.glob("config.corrupt_*.json")))
        self.assertEqual(json.loads(util.config_path().read_text(encoding="utf-8")), recovered)

    def test_unrecoverable_config_stops_instead_of_returning_empty_settings(self):
        util.config_path().write_text("{broken", encoding="utf-8")
        util.config_path().with_suffix(".json.bak").write_text("[]", encoding="utf-8")
        with self.assertRaises(util.ConfigError):
            util.load_config_with_status()


class DatabaseSafetyTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.db = Database(self.root / "accounting.db")

    def tearDown(self):
        self.db.close()

    def test_all_transaction_writes_enforce_seven_digits(self):
        self.assertTrue(self.db.save_transaction(
            "2026/09/01", "現金", "income", "一般收入", "上限", 9_999_999
        ).ok)
        rejected = self.db.save_transaction(
            "2026/09/02", "現金", "income", "一般收入", "超額", 10_000_000
        )
        self.assertFalse(rejected.ok)
        self.assertIn("7位數", rejected.message)

        result = self.db.import_transactions([{
            "row_no": 2, "tx_date": "2026/09/03", "account_name": "現金",
            "kind": "expense", "category_name": "一般支出", "summary": "超額匯入",
            "amount": 10_000_000,
        }])
        self.assertEqual(result["imported"], 0)
        self.assertIn("7位數", result["errors"][0])

        tx_id = self.db.month_data("2026/09")["rows"][0]["id"]
        ok, message = self.db.update_transaction(tx_id, "amount", 10_000_000)
        self.assertFalse(ok)
        self.assertIn("7位數", message)

    def test_legacy_over_limit_value_is_preserved_when_other_fields_change(self):
        now = "2026-09-11T12:00:00"
        cur = self.db.conn.execute(
            "INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at) "
            "VALUES(?,?,?,?,?,?,?,?)",
            ("2026/09/01", "現金", "income", "一般收入", "舊資料", 10_000_000, now, now),
        )
        self.db.conn.commit()
        ok, message = self.db.update_transaction_full(
            int(cur.lastrowid), "2026/09/01", "現金", "一般收入", "只改摘要", 10_000_000
        )
        self.assertTrue(ok, message)
        self.assertEqual(self.db.get_transaction(int(cur.lastrowid))["amount"], 10_000_000)

    def test_backup_and_database_version_are_application_validated(self):
        backup = self.root / "backup.db"
        self.db.backup_to(backup)
        self.assertEqual(self.db.validate_database_file(backup), (True, ""))
        con = sqlite3.connect(backup)
        con.execute("UPDATE meta SET value='999' WHERE key='schema_version'")
        con.commit()
        con.close()
        ok, message = self.db.validate_database_file(backup)
        self.assertFalse(ok)
        self.assertIn("不支援", message)


if __name__ == "__main__":
    unittest.main()
