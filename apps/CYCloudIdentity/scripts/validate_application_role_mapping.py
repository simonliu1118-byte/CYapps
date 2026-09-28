from __future__ import annotations

import sqlite3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FOUNDATION = ROOT / "migrations" / "0001_identity_foundation.sql"
MAPPING = ROOT / "migrations" / "0004_application_role_mapping.sql"


def expect_integrity_error(conn: sqlite3.Connection, sql: str, params: tuple = ()) -> None:
    try:
        conn.execute(sql, params)
    except sqlite3.IntegrityError:
        return
    raise AssertionError(f"expected IntegrityError: {sql}")


def main() -> int:
    conn = sqlite3.connect(":memory:")
    conn.execute("PRAGMA foreign_keys = ON")
    conn.executescript(FOUNDATION.read_text(encoding="utf-8"))

    conn.execute(
        "INSERT INTO workspaces(workspace_id, workspace_code, display_name) VALUES('workspace-role', 'ROLE', 'Role Test')"
    )
    conn.execute(
        "INSERT INTO employees(employee_id, workspace_id, employee_no, name, email_normalized) "
        "VALUES('employee-role-owner', 'workspace-role', '0001', 'Owner', 'owner@example.test')"
    )
    conn.execute(
        "UPDATE workspaces SET super_admin_employee_id='employee-role-owner', status='active' "
        "WHERE workspace_id='workspace-role'"
    )
    conn.execute(
        "INSERT INTO applications(application_id, display_name) VALUES('APP_ROLE_TEST', 'Role Test App')"
    )
    conn.execute(
        "INSERT INTO workspace_applications(workspace_id, application_id, enabled) "
        "VALUES('workspace-role', 'APP_ROLE_TEST', 1)"
    )
    conn.execute(
        "INSERT INTO identity_groups(group_id, workspace_id, group_key, display_name) "
        "VALUES('group-role-user', 'workspace-role', 'ROLE_USER', 'Role User')"
    )
    conn.execute(
        "INSERT INTO identity_group_application_access(workspace_id, group_id, application_id, enabled) "
        "VALUES('workspace-role', 'group-role-user', 'APP_ROLE_TEST', 1)"
    )

    conn.executescript(MAPPING.read_text(encoding="utf-8"))

    role_mode = conn.execute(
        "SELECT compatibility_role_mode FROM workspace_applications "
        "WHERE workspace_id='workspace-role' AND application_id='APP_ROLE_TEST'"
    ).fetchone()
    if role_mode != (None,):
        raise AssertionError(f"existing workspace application mapping changed unexpectedly: {role_mode!r}")

    role_key = conn.execute(
        "SELECT application_role_key FROM identity_group_application_access "
        "WHERE workspace_id='workspace-role' AND group_id='group-role-user' AND application_id='APP_ROLE_TEST'"
    ).fetchone()
    if role_key != (None,):
        raise AssertionError(f"existing group application grant changed unexpectedly: {role_key!r}")

    conn.execute(
        "UPDATE workspace_applications SET compatibility_role_mode='USER_ADMIN' "
        "WHERE workspace_id='workspace-role' AND application_id='APP_ROLE_TEST'"
    )
    conn.execute(
        "UPDATE identity_group_application_access SET application_role_key='USER' "
        "WHERE workspace_id='workspace-role' AND group_id='group-role-user' AND application_id='APP_ROLE_TEST'"
    )
    conn.execute(
        "UPDATE identity_group_application_access SET application_role_key='ADMIN' "
        "WHERE workspace_id='workspace-role' AND group_id='group-role-user' AND application_id='APP_ROLE_TEST'"
    )

    expect_integrity_error(
        conn,
        "UPDATE workspace_applications SET compatibility_role_mode='FIXED_GLOBAL_ROLE' "
        "WHERE workspace_id='workspace-role' AND application_id='APP_ROLE_TEST'",
    )
    expect_integrity_error(
        conn,
        "UPDATE identity_group_application_access SET application_role_key='SUPER_ADMIN' "
        "WHERE workspace_id='workspace-role' AND group_id='group-role-user' AND application_id='APP_ROLE_TEST'",
    )
    expect_integrity_error(
        conn,
        "UPDATE identity_group_application_access SET application_role_key='OWNER' "
        "WHERE workspace_id='workspace-role' AND group_id='group-role-user' AND application_id='APP_ROLE_TEST'",
    )

    index_names = {
        row[1]
        for row in conn.execute("PRAGMA index_list('identity_group_application_access')")
    }
    if "idx_group_app_access_role" not in index_names:
        raise AssertionError("missing idx_group_app_access_role")

    integrity = conn.execute("PRAGMA integrity_check").fetchone()[0]
    foreign_keys = list(conn.execute("PRAGMA foreign_key_check"))
    if integrity != "ok" or foreign_keys:
        raise AssertionError(f"database integrity failed: {integrity!r}, fk={foreign_keys!r}")

    print("PASS application role mapping forward migration")
    print("PASS existing grants remain compatible with NULL role metadata")
    print("PASS USER/ADMIN mapping only; ordinary groups cannot assign SUPER_ADMIN")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
