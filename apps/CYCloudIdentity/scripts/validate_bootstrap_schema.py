from __future__ import annotations

import sqlite3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MIGRATIONS = ROOT / "migrations"


def main() -> int:
    conn = sqlite3.connect(":memory:")
    conn.execute("PRAGMA foreign_keys = ON")
    for migration in sorted(MIGRATIONS.glob("*.sql")):
        conn.executescript(migration.read_text(encoding="utf-8"))

    tables = {
        row[0]
        for row in conn.execute(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"
        )
    }
    for required in ("bootstrap_requests", "email_delivery_budget"):
        if required not in tables:
            raise AssertionError(f"missing table: {required}")

    verifier = "pbkdf2-sha256$100000$" + ("a" * 32) + "$" + ("b" * 64)
    conn.execute(
        """
        INSERT INTO bootstrap_requests(
            bootstrap_id, workspace_id, workspace_code, workspace_display_name,
            employee_id, employee_no, employee_name, email_normalized,
            credential_algorithm, credential_verifier, applications_json,
            expires_at
        ) VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            "bootstrap-test-001",
            "workspace-test-001",
            "TEST001",
            "Synthetic Workspace",
            "employee-test-001",
            "0001",
            "Synthetic Employee",
            "employee@example.test",
            "pbkdf2-sha256",
            verifier,
            '[{"applicationId":"APP_TEST_A","displayName":"Synthetic App"}]',
            "2099-01-01T00:10:00.000Z",
        ),
    )

    conn.execute(
        "INSERT INTO email_delivery_budget(usage_date_utc, reserved_count, sent_count) VALUES('2099-01-01', 1, 2)"
    )
    try:
        conn.execute(
            "UPDATE email_delivery_budget SET reserved_count = -1 WHERE usage_date_utc = '2099-01-01'"
        )
    except sqlite3.IntegrityError:
        pass
    else:
        raise AssertionError("negative email reservation count must be rejected")

    if conn.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
        raise AssertionError("database integrity check failed")
    foreign_keys = list(conn.execute("PRAGMA foreign_key_check"))
    if foreign_keys:
        raise AssertionError(f"foreign key check failed: {foreign_keys!r}")

    print("PASS CYCloud Identity bootstrap staging schema")
    print("PASS bounded email delivery budget schema")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
