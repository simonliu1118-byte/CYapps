from __future__ import annotations

import sqlite3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MIGRATION = ROOT / "migrations" / "0001_identity_foundation.sql"


def expect_integrity_error(conn: sqlite3.Connection, sql: str, params: tuple = ()) -> None:
    try:
        conn.execute(sql, params)
    except sqlite3.IntegrityError:
        return
    raise AssertionError(f"expected IntegrityError: {sql}")


def add_workspace(conn: sqlite3.Connection, workspace_id: str, code: str) -> None:
    conn.execute(
        "INSERT INTO workspaces(workspace_id, workspace_code, display_name) VALUES(?, ?, ?)",
        (workspace_id, code, f"Workspace {code}"),
    )


def add_employee(
    conn: sqlite3.Connection,
    employee_id: str,
    workspace_id: str,
    employee_no: str,
    email: str,
    role: str,
    enabled: int = 1,
) -> None:
    conn.execute(
        """
        INSERT INTO employees(
            employee_id, workspace_id, employee_no, name, email_normalized,
            email_verified_at, role, enabled
        ) VALUES(?, ?, ?, ?, ?, '2026-09-28T00:00:00.000Z', ?, ?)
        """,
        (employee_id, workspace_id, employee_no, f"Employee {employee_no}", email, role, enabled),
    )


def main() -> int:
    schema = MIGRATION.read_text(encoding="utf-8")
    conn = sqlite3.connect(":memory:")
    conn.execute("PRAGMA foreign_keys = ON")
    conn.executescript(schema)

    required_tables = {
        "workspaces",
        "employees",
        "employee_credentials",
        "applications",
        "workspace_applications",
        "employee_application_access",
        "identity_sessions",
        "email_otp_challenges",
        "identity_audit_events",
    }
    actual_tables = {
        row[0]
        for row in conn.execute(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'"
        )
    }
    missing = required_tables - actual_tables
    if missing:
        raise AssertionError(f"missing tables: {sorted(missing)}")

    applications = {
        row[0]
        for row in conn.execute("SELECT application_id FROM applications")
    }
    if applications != {"CYWEB", "CYACCOUNTINGWEB", "CYINVOICE"}:
        raise AssertionError(f"unexpected application seed: {sorted(applications)}")

    add_workspace(conn, "workspace-alpha", "ALPHA")
    add_workspace(conn, "workspace-beta", "BETA")

    conn.execute(
        "INSERT INTO workspace_applications(workspace_id, application_id) VALUES('workspace-alpha', 'CYWEB')"
    )
    conn.execute(
        "INSERT INTO workspace_applications(workspace_id, application_id) VALUES('workspace-beta', 'CYWEB')"
    )

    add_employee(
        conn,
        "employee-alpha-super",
        "workspace-alpha",
        "0001",
        "owner@example.test",
        "SUPER_ADMIN",
    )

    expect_integrity_error(
        conn,
        """
        INSERT INTO employees(
            employee_id, workspace_id, employee_no, name, email_normalized,
            email_verified_at, role, enabled
        ) VALUES(
            'employee-alpha-super-2', 'workspace-alpha', '0002', 'Second Owner',
            'owner2@example.test', '2026-09-28T00:00:00.000Z', 'SUPER_ADMIN', 1
        )
        """,
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO employees(
            employee_id, workspace_id, employee_no, name, email_normalized,
            email_verified_at, role, enabled
        ) VALUES(
            'employee-disabled-super', 'workspace-beta', '0009', 'Disabled Owner',
            'disabled@example.test', '2026-09-28T00:00:00.000Z', 'SUPER_ADMIN', 0
        )
        """,
    )

    # Employee No and Email are unique inside one Workspace, not globally.
    add_employee(
        conn,
        "employee-beta-super",
        "workspace-beta",
        "0001",
        "owner@example.test",
        "SUPER_ADMIN",
    )
    add_employee(
        conn,
        "employee-alpha-user",
        "workspace-alpha",
        "0002",
        "user@example.test",
        "EMPLOYEE",
    )

    expect_integrity_error(
        conn,
        """
        INSERT INTO employees(
            employee_id, workspace_id, employee_no, name, email_normalized, role
        ) VALUES('duplicate-no', 'workspace-alpha', '0002', 'Duplicate No', 'other@example.test', 'EMPLOYEE')
        """,
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO employees(
            employee_id, workspace_id, employee_no, name, email_normalized, role
        ) VALUES('duplicate-email', 'workspace-alpha', '0003', 'Duplicate Email', 'user@example.test', 'EMPLOYEE')
        """,
    )

    conn.execute(
        """
        INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version)
        VALUES('employee-alpha-user', 'pbkdf2-sha256', ?, 1)
        """,
        ("v" * 100,),
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version)
        VALUES('employee-alpha-super', 'plaintext', ?, 1)
        """,
        ("v" * 100,),
    )

    conn.execute(
        """
        INSERT INTO employee_application_access(workspace_id, employee_id, application_id)
        VALUES('workspace-alpha', 'employee-alpha-user', 'CYWEB')
        """
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO employee_application_access(workspace_id, employee_id, application_id)
        VALUES('workspace-beta', 'employee-alpha-user', 'CYWEB')
        """,
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO employee_application_access(workspace_id, employee_id, application_id)
        VALUES('workspace-alpha', 'employee-alpha-user', 'CYACCOUNTINGWEB')
        """,
    )

    conn.execute(
        """
        INSERT INTO identity_sessions(
            session_hash, workspace_id, employee_id, application_id,
            credential_version, employee_revision, created_at, expires_at
        ) VALUES(?, 'workspace-alpha', 'employee-alpha-user', 'CYWEB', 1, 1, ?, ?)
        """,
        (
            "a" * 64,
            "2026-09-28T00:00:00.000Z",
            "2026-09-28T08:00:00.000Z",
        ),
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO identity_sessions(
            session_hash, workspace_id, employee_id, application_id,
            credential_version, employee_revision, created_at, expires_at
        ) VALUES(?, 'workspace-beta', 'employee-alpha-user', 'CYWEB', 1, 1, ?, ?)
        """,
        (
            "b" * 64,
            "2026-09-28T00:00:00.000Z",
            "2026-09-28T08:00:00.000Z",
        ),
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO identity_sessions(
            session_hash, workspace_id, employee_id, application_id,
            credential_version, employee_revision, created_at, expires_at
        ) VALUES(?, 'workspace-alpha', 'employee-alpha-user', 'CYACCOUNTINGWEB', 1, 1, ?, ?)
        """,
        (
            "c" * 64,
            "2026-09-28T00:00:00.000Z",
            "2026-09-28T08:00:00.000Z",
        ),
    )

    conn.execute(
        """
        INSERT INTO email_otp_challenges(
            challenge_id, purpose, scope_key, email_normalized, otp_digest,
            expires_at, resend_after
        ) VALUES(
            'otp-valid', 'employee_password_reset', 'CYWEB:workspace-alpha:employee-alpha-user',
            'user@example.test', ?, '2026-09-28T00:10:00.000Z', '2026-09-28T00:01:00.000Z'
        )
        """,
        ("d" * 64,),
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO email_otp_challenges(
            challenge_id, purpose, email_normalized, otp_digest, expires_at, resend_after
        ) VALUES('otp-bad-purpose', 'login', 'user@example.test', ?, ?, ?)
        """,
        ("e" * 64, "2026-09-28T00:10:00.000Z", "2026-09-28T00:01:00.000Z"),
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO email_otp_challenges(
            challenge_id, purpose, email_normalized, otp_digest, expires_at, resend_after
        ) VALUES('otp-bad-digest', 'employee_password_reset', 'user@example.test', 'plain-code', ?, ?)
        """,
        ("2026-09-28T00:10:00.000Z", "2026-09-28T00:01:00.000Z"),
    )

    integrity = conn.execute("PRAGMA integrity_check").fetchone()[0]
    foreign_keys = list(conn.execute("PRAGMA foreign_key_check"))
    if integrity != "ok" or foreign_keys:
        raise AssertionError(f"database integrity failed: {integrity!r}, fk={foreign_keys!r}")

    print("PASS CYCloud Identity schema foundation")
    print("PASS Workspace isolation + Employee uniqueness")
    print("PASS single-SUPER_ADMIN upper bound + enabled invariant")
    print("PASS Application Access/session cross-Workspace isolation")
    print("PASS credential/OTP structural safety")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
