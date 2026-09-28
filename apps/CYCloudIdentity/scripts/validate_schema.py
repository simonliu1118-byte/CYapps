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
    enabled: int = 1,
) -> None:
    conn.execute(
        """
        INSERT INTO employees(
            employee_id, workspace_id, employee_no, name, email_normalized,
            email_verified_at, enabled
        ) VALUES(?, ?, ?, ?, ?, '2026-09-28T00:00:00.000Z', ?)
        """,
        (employee_id, workspace_id, employee_no, f"Employee {employee_no}", email, enabled),
    )


def activate_workspace(
    conn: sqlite3.Connection,
    workspace_id: str,
    employee_id: str,
    recovery_email: str,
) -> None:
    conn.execute(
        """
        UPDATE workspaces
           SET super_admin_employee_id = ?,
               recovery_email_normalized = ?,
               recovery_email_verified_at = '2026-09-28T00:00:00.000Z',
               status = 'active'
         WHERE workspace_id = ?
        """,
        (employee_id, recovery_email, workspace_id),
    )


def main() -> int:
    schema = MIGRATION.read_text(encoding="utf-8")
    conn = sqlite3.connect(":memory:")
    conn.execute("PRAGMA foreign_keys = ON")
    conn.executescript(schema)

    required_tables = {
        "workspaces",
        "employees",
        "identity_groups",
        "employee_identity_groups",
        "employee_credentials",
        "applications",
        "workspace_applications",
        "employee_application_access",
        "identity_group_application_access",
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

    # Public foundation migration must not publish the company's real application catalog.
    applications = list(conn.execute("SELECT application_id FROM applications"))
    if applications:
        raise AssertionError(f"application registry must start empty, found: {applications!r}")

    add_workspace(conn, "workspace-alpha", "ALPHA")
    add_workspace(conn, "workspace-beta", "BETA")
    add_workspace(conn, "workspace-gamma", "GAMMA")

    # An active Workspace must have one explicit highest-authority employee.
    expect_integrity_error(
        conn,
        "UPDATE workspaces SET status = 'active' WHERE workspace_id = 'workspace-gamma'",
    )

    add_employee(
        conn,
        "employee-alpha-owner",
        "workspace-alpha",
        "0001",
        "owner-alpha@example.test",
    )
    add_employee(
        conn,
        "employee-alpha-user",
        "workspace-alpha",
        "0002",
        "user-alpha@example.test",
    )
    add_employee(
        conn,
        "employee-alpha-next-owner",
        "workspace-alpha",
        "0003",
        "next-alpha@example.test",
    )
    add_employee(
        conn,
        "employee-beta-owner",
        "workspace-beta",
        "0001",
        "owner-beta@example.test",
    )

    activate_workspace(
        conn,
        "workspace-alpha",
        "employee-alpha-owner",
        "owner-alpha@example.test",
    )
    activate_workspace(
        conn,
        "workspace-beta",
        "employee-beta-owner",
        "owner-beta@example.test",
    )

    expect_integrity_error(
        conn,
        """
        UPDATE workspaces
           SET super_admin_employee_id = 'employee-alpha-owner'
         WHERE workspace_id = 'workspace-beta'
        """,
    )
    expect_integrity_error(
        conn,
        "UPDATE employees SET enabled = 0 WHERE employee_id = 'employee-alpha-owner'",
    )
    expect_integrity_error(
        conn,
        "DELETE FROM employees WHERE employee_id = 'employee-alpha-owner'",
    )

    # Transfer is a single authority pointer, separate from editable groups.
    conn.execute(
        """
        UPDATE workspaces
           SET super_admin_employee_id = 'employee-alpha-next-owner',
               recovery_email_normalized = 'next-alpha@example.test',
               recovery_email_verified_at = '2026-09-28T00:05:00.000Z'
         WHERE workspace_id = 'workspace-alpha'
        """
    )
    conn.execute(
        "UPDATE employees SET enabled = 0 WHERE employee_id = 'employee-alpha-owner'"
    )

    # Employee No and Email are unique inside one Workspace, not globally.
    expect_integrity_error(
        conn,
        """
        INSERT INTO employees(
            employee_id, workspace_id, employee_no, name, email_normalized
        ) VALUES('duplicate-no', 'workspace-alpha', '0002', 'Duplicate No', 'other@example.test')
        """,
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO employees(
            employee_id, workspace_id, employee_no, name, email_normalized
        ) VALUES('duplicate-email', 'workspace-alpha', '0004', 'Duplicate Email', 'user-alpha@example.test')
        """,
    )
    add_employee(
        conn,
        "employee-beta-user",
        "workspace-beta",
        "0002",
        "user-alpha@example.test",
    )

    # Ordinary Identity Groups are runtime data, not a fixed enum.
    conn.execute(
        """
        INSERT INTO identity_groups(group_id, workspace_id, group_key, display_name)
        VALUES('group-alpha-ops', 'workspace-alpha', 'OPS', 'Operations')
        """
    )
    conn.execute(
        """
        INSERT INTO identity_groups(group_id, workspace_id, group_key, display_name)
        VALUES('group-alpha-custom', 'workspace-alpha', 'CUSTOM_TEAM', 'Custom Team')
        """
    )
    conn.execute(
        """
        INSERT INTO identity_groups(group_id, workspace_id, group_key, display_name)
        VALUES('group-beta-ops', 'workspace-beta', 'OPS', 'Operations')
        """
    )
    conn.execute(
        """
        UPDATE identity_groups
           SET group_key = 'WAREHOUSE', display_name = 'Warehouse Team', revision = revision + 1
         WHERE group_id = 'group-alpha-custom'
        """
    )
    renamed = conn.execute(
        "SELECT group_key, display_name FROM identity_groups WHERE group_id = 'group-alpha-custom'"
    ).fetchone()
    if renamed != ("WAREHOUSE", "Warehouse Team"):
        raise AssertionError(f"identity group rename failed: {renamed!r}")

    conn.execute(
        """
        INSERT INTO employee_identity_groups(workspace_id, employee_id, group_id)
        VALUES('workspace-alpha', 'employee-alpha-user', 'group-alpha-ops')
        """
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO employee_identity_groups(workspace_id, employee_id, group_id)
        VALUES('workspace-beta', 'employee-alpha-user', 'group-beta-ops')
        """,
    )

    conn.execute(
        """
        INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version)
        VALUES('employee-alpha-user', 'pbkdf2-sha256', ?, 1)
        """,
        ("v" * 100,),
    )
    # Schema leaves algorithm identifiers extensible; runtime decides which are actually supported.
    conn.execute(
        """
        INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version)
        VALUES('employee-beta-user', 'future-kdf-v2', ?, 1)
        """,
        ("w" * 100,),
    )

    # Tests register synthetic applications at runtime; the migration itself contains no real catalog.
    conn.execute(
        "INSERT INTO applications(application_id, display_name) VALUES('APP_TEST_A', 'Test App A')"
    )
    conn.execute(
        "INSERT INTO applications(application_id, display_name) VALUES('APP_TEST_B', 'Test App B')"
    )
    conn.execute(
        "INSERT INTO workspace_applications(workspace_id, application_id) VALUES('workspace-alpha', 'APP_TEST_A')"
    )
    conn.execute(
        "INSERT INTO workspace_applications(workspace_id, application_id) VALUES('workspace-beta', 'APP_TEST_A')"
    )

    conn.execute(
        """
        INSERT INTO employee_application_access(workspace_id, employee_id, application_id)
        VALUES('workspace-alpha', 'employee-alpha-user', 'APP_TEST_A')
        """
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO employee_application_access(workspace_id, employee_id, application_id)
        VALUES('workspace-beta', 'employee-alpha-user', 'APP_TEST_A')
        """,
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO employee_application_access(workspace_id, employee_id, application_id)
        VALUES('workspace-alpha', 'employee-alpha-user', 'APP_TEST_B')
        """,
    )

    conn.execute(
        """
        INSERT INTO identity_group_application_access(workspace_id, group_id, application_id)
        VALUES('workspace-alpha', 'group-alpha-ops', 'APP_TEST_A')
        """
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO identity_group_application_access(workspace_id, group_id, application_id)
        VALUES('workspace-beta', 'group-alpha-ops', 'APP_TEST_A')
        """,
    )

    conn.execute(
        """
        INSERT INTO identity_sessions(
            session_hash, workspace_id, employee_id, application_id,
            credential_version, employee_revision, created_at, expires_at
        ) VALUES(?, 'workspace-alpha', 'employee-alpha-user', 'APP_TEST_A', 1, 1, ?, ?)
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
        ) VALUES(?, 'workspace-beta', 'employee-alpha-user', 'APP_TEST_A', 1, 1, ?, ?)
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
        ) VALUES(?, 'workspace-alpha', 'employee-alpha-user', 'APP_TEST_B', 1, 1, ?, ?)
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
            'otp-valid', 'employee_password_reset', 'APP_TEST_A:workspace-alpha:employee-alpha-user',
            'user-alpha@example.test', ?, '2026-09-28T00:10:00.000Z', '2026-09-28T00:01:00.000Z'
        )
        """,
        ("d" * 64,),
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO email_otp_challenges(
            challenge_id, purpose, email_normalized, otp_digest, expires_at, resend_after
        ) VALUES('otp-bad-purpose', 'login', 'user-alpha@example.test', ?, ?, ?)
        """,
        ("e" * 64, "2026-09-28T00:10:00.000Z", "2026-09-28T00:01:00.000Z"),
    )
    expect_integrity_error(
        conn,
        """
        INSERT INTO email_otp_challenges(
            challenge_id, purpose, email_normalized, otp_digest, expires_at, resend_after
        ) VALUES('otp-bad-digest', 'employee_password_reset', 'user-alpha@example.test', 'plain-code', ?, ?)
        """,
        ("2026-09-28T00:10:00.000Z", "2026-09-28T00:01:00.000Z"),
    )

    integrity = conn.execute("PRAGMA integrity_check").fetchone()[0]
    foreign_keys = list(conn.execute("PRAGMA foreign_key_check"))
    if integrity != "ok" or foreign_keys:
        raise AssertionError(f"database integrity failed: {integrity!r}, fk={foreign_keys!r}")

    print("PASS CYCloud Identity schema foundation")
    print("PASS empty Public application registry + synthetic runtime registration")
    print("PASS Workspace isolation + Employee uniqueness")
    print("PASS protected Workspace highest-authority pointer + transfer")
    print("PASS extensible Identity Groups + cross-Workspace isolation")
    print("PASS direct/group Application Access + session isolation")
    print("PASS credential/OTP structural safety")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
