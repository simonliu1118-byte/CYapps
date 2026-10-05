from __future__ import annotations

import sqlite3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MIGRATIONS = [
    ROOT / "migrations" / "0001_identity_foundation.sql",
    ROOT / "migrations" / "0002_bootstrap_email_budget.sql",
    ROOT / "migrations" / "0003_identity_security_policy.sql",
]


def expect_integrity_error(conn: sqlite3.Connection, sql: str, params: tuple = ()) -> None:
    try:
        conn.execute(sql, params)
    except sqlite3.IntegrityError:
        return
    raise AssertionError(f"expected IntegrityError: {sql}")


def main() -> int:
    conn = sqlite3.connect(":memory:")
    conn.execute("PRAGMA foreign_keys = ON")
    for migration in MIGRATIONS:
        conn.executescript(migration.read_text(encoding="utf-8"))

    conn.execute(
        "INSERT INTO workspaces(workspace_id, workspace_code, display_name) VALUES('workspace-policy', 'POLICY', 'Policy Test')"
    )
    conn.execute(
        """
        INSERT INTO employees(
          employee_id, workspace_id, employee_no, name, email_normalized, email_verified_at, enabled
        ) VALUES('employee-policy-super', 'workspace-policy', '0001', 'Policy Super', 'super@example.test', '2026-09-28T00:00:00.000Z', 1)
        """
    )
    conn.execute(
        """
        UPDATE workspaces
           SET super_admin_employee_id = 'employee-policy-super',
               recovery_email_normalized = 'super@example.test',
               recovery_email_verified_at = '2026-09-28T00:00:00.000Z',
               status = 'active'
         WHERE workspace_id = 'workspace-policy'
        """
    )

    conn.execute(
        """
        INSERT INTO identity_security_policies(
          workspace_id, updated_by_employee_id
        ) VALUES('workspace-policy', 'employee-policy-super')
        """
    )
    row = conn.execute(
        """
        SELECT otp_resend_cooldown_seconds,
               otp_max_attempts,
               otp_max_sent_per_email_purpose_hour,
               email_daily_limit,
               revision
          FROM identity_security_policies
         WHERE workspace_id = 'workspace-policy'
        """
    ).fetchone()
    if row != (60, 5, 5, 100, 1):
        raise AssertionError(f"unexpected policy defaults: {row!r}")

    conn.execute(
        """
        UPDATE identity_security_policies
           SET otp_resend_cooldown_seconds = 90,
               otp_max_attempts = 6,
               otp_max_sent_per_email_purpose_hour = 4,
               email_daily_limit = 40,
               revision = revision + 1
         WHERE workspace_id = 'workspace-policy'
        """
    )
    updated = conn.execute(
        """
        SELECT otp_resend_cooldown_seconds,
               otp_max_attempts,
               otp_max_sent_per_email_purpose_hour,
               email_daily_limit,
               revision
          FROM identity_security_policies
         WHERE workspace_id = 'workspace-policy'
        """
    ).fetchone()
    if updated != (90, 6, 4, 40, 2):
        raise AssertionError(f"policy update failed: {updated!r}")

    expect_integrity_error(
        conn,
        "UPDATE identity_security_policies SET otp_resend_cooldown_seconds = 10 WHERE workspace_id = 'workspace-policy'",
    )
    expect_integrity_error(
        conn,
        "UPDATE identity_security_policies SET otp_max_attempts = 20 WHERE workspace_id = 'workspace-policy'",
    )
    expect_integrity_error(
        conn,
        "UPDATE identity_security_policies SET otp_max_sent_per_email_purpose_hour = 0 WHERE workspace_id = 'workspace-policy'",
    )
    expect_integrity_error(
        conn,
        "UPDATE identity_security_policies SET email_daily_limit = 0 WHERE workspace_id = 'workspace-policy'",
    )

    conn.execute(
        """
        INSERT INTO workspace_email_delivery_budget(
          workspace_id, usage_date_utc, reserved_count, sent_count
        ) VALUES('workspace-policy', '2026-09-28', 1, 3)
        """
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO workspace_email_delivery_budget(
          workspace_id, usage_date_utc, reserved_count, sent_count
        ) VALUES('workspace-policy', 'bad-date', 0, 0)
        """,
    )

    conn.execute("DELETE FROM identity_security_policies WHERE workspace_id = 'workspace-policy'")
    conn.execute("DELETE FROM workspace_email_delivery_budget WHERE workspace_id = 'workspace-policy'")

    integrity = conn.execute("PRAGMA integrity_check").fetchone()[0]
    foreign_keys = list(conn.execute("PRAGMA foreign_key_check"))
    if integrity != "ok" or foreign_keys:
        raise AssertionError(f"database integrity failed: {integrity!r}, fk={foreign_keys!r}")

    print("PASS configurable Workspace OTP security policy schema")
    print("PASS bounded policy values")
    print("PASS per-Workspace daily email budget schema")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
