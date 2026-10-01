#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CONFIG="wrangler.test.generated.jsonc"
STATE_DIR="$(mktemp -d)"
PORT="8791"
WORKER_PID=""
WORKER_LOG="$STATE_DIR/worker.log"

print_worker_log() {
  if [[ -f "$WORKER_LOG" ]]; then
    echo '--- CYCloud Identity local Worker log ---' >&2
    cat "$WORKER_LOG" >&2
    echo '--- end local Worker log ---' >&2
  fi
}

cleanup() {
  if [[ -n "$WORKER_PID" ]]; then
    kill "$WORKER_PID" >/dev/null 2>&1 || true
    wait "$WORKER_PID" >/dev/null 2>&1 || true
  fi
  rm -f "$CONFIG"
  rm -rf "$STATE_DIR"
}
trap cleanup EXIT

DB_ID="$(python3 - <<'PY'
import uuid
print(uuid.uuid4())
PY
)"
python3 - "$DB_ID" <<'PY'
from pathlib import Path
import sys

db_id = sys.argv[1]
template = Path('wrangler.test.template.jsonc').read_text(encoding='utf-8')
Path('wrangler.test.generated.jsonc').write_text(
    template.replace('__D1_DATABASE_ID__', db_id),
    encoding='utf-8',
)
PY

npx wrangler d1 migrations apply DB \
  --local \
  --config "$CONFIG" \
  --persist-to "$STATE_DIR" >/dev/null

VERIFIER="$(python3 - <<'PY'
import hashlib
password = b'test-pass-123'
salt = bytes.fromhex('00112233445566778899aabbccddeeff')
iterations = 100_000
digest = hashlib.pbkdf2_hmac('sha256', password, salt, iterations, 32)
print(f'pbkdf2-sha256${iterations}${salt.hex()}${digest.hex()}')
PY
)"

SEED_SQL=$(cat <<SQL
INSERT INTO workspaces(workspace_id, workspace_code, display_name, status)
VALUES('workspace-test-001', 'TEST001', 'Synthetic Test Workspace', 'bootstrap');

INSERT INTO employees(
  employee_id, workspace_id, employee_no, name, email_normalized,
  email_verified_at, enabled, role_key, identity_admin, activated_at
)
VALUES
  ('employee-test-super', 'workspace-test-001', '0001', 'Synthetic Super', 'super@example.test', '2026-09-28T00:00:00.000Z', 1, 'ADMIN', 0, '2026-09-28T00:00:00.000Z'),
  ('employee-test-user', 'workspace-test-001', '0002', 'Synthetic User', 'user@example.test', '2026-09-28T00:00:00.000Z', 1, 'USER', 0, '2026-09-28T00:00:00.000Z'),
  ('employee-test-admin', 'workspace-test-001', '0003', 'Synthetic Admin', 'admin@example.test', '2026-09-28T00:00:00.000Z', 1, 'ADMIN', 0, '2026-09-28T00:00:00.000Z'),
  ('employee-test-identity-admin', 'workspace-test-001', '0004', 'Synthetic Identity Admin', 'identity-admin@example.test', '2026-09-28T00:00:00.000Z', 1, 'ADMIN', 1, '2026-09-28T00:00:00.000Z');

UPDATE workspaces
SET super_admin_employee_id = 'employee-test-super',
    recovery_email_normalized = 'super@example.test',
    recovery_email_verified_at = '2026-09-28T00:00:00.000Z',
    status = 'active'
WHERE workspace_id = 'workspace-test-001';

INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version)
VALUES
  ('employee-test-super', 'pbkdf2-sha256', '$VERIFIER', 1),
  ('employee-test-user', 'pbkdf2-sha256', '$VERIFIER', 1),
  ('employee-test-admin', 'pbkdf2-sha256', '$VERIFIER', 1),
  ('employee-test-identity-admin', 'pbkdf2-sha256', '$VERIFIER', 1);

INSERT INTO applications(application_id, display_name)
VALUES
  ('APP_TEST_LOGIN', 'Synthetic Core App'),
  ('APP_TEST_EXTRA', 'Synthetic Extra App');

INSERT INTO workspace_applications(workspace_id, application_id, enabled)
VALUES
  ('workspace-test-001', 'APP_TEST_LOGIN', 1),
  ('workspace-test-001', 'APP_TEST_EXTRA', 1);

-- Legacy Group data deliberately remains. It must no longer authorize App entry.
INSERT INTO identity_groups(group_id, workspace_id, group_key, display_name)
VALUES('group-test-legacy', 'workspace-test-001', 'LEGACY_ACCESS', 'Legacy Access');
INSERT INTO employee_identity_groups(workspace_id, employee_id, group_id)
VALUES('workspace-test-001', 'employee-test-user', 'group-test-legacy');
INSERT INTO identity_group_application_access(
  workspace_id, group_id, application_id, enabled, application_role_key
)
VALUES('workspace-test-001', 'group-test-legacy', 'APP_TEST_EXTRA', 1, 'ADMIN');
SQL
)

npx wrangler d1 execute DB \
  --local \
  --config "$CONFIG" \
  --persist-to "$STATE_DIR" \
  --command "$SEED_SQL" >/dev/null

npx wrangler dev \
  --local \
  --config "$CONFIG" \
  --persist-to "$STATE_DIR" \
  --port "$PORT" >"$WORKER_LOG" 2>&1 &
WORKER_PID="$!"

READY=0
for _ in $(seq 1 30); do
  if ! kill -0 "$WORKER_PID" >/dev/null 2>&1; then
    echo 'CYCloud Identity local Worker exited before health check became ready.' >&2
    print_worker_log
    exit 1
  fi
  if curl --silent --fail "http://127.0.0.1:${PORT}/v1/health" >/dev/null; then
    READY=1
    break
  fi
  sleep 1
done
if [[ "$READY" != "1" ]]; then
  echo 'CYCloud Identity local Worker did not become ready before timeout.' >&2
  print_worker_log
  exit 1
fi

login() {
  local employee_no="$1"
  local app_id="$2"
  curl --silent --show-error --fail-with-body \
    -X POST "http://127.0.0.1:${PORT}/v1/identity/login" \
    -H 'content-type: application/json' \
    --data "{\"workspaceId\":\"workspace-test-001\",\"applicationId\":\"${app_id}\",\"employeeNo\":\"${employee_no}\",\"password\":\"test-pass-123\"}"
}

token_from() {
  RESPONSE="$1" python3 - <<'PY'
import json, os
payload = json.loads(os.environ['RESPONSE'])
assert payload['ok'] is True
token = payload['session']['token']
assert token.startswith('cyid_') and len(token) == 69
print(token)
PY
}

USER_RESPONSE="$(login 0002 APP_TEST_LOGIN)"
USER_TOKEN="$(token_from "$USER_RESPONSE")"
USER_RESPONSE="$USER_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['USER_RESPONSE'])['principal']
assert p['workspaceRole'] == 'USER'
assert p['isWorkspaceSuperAdmin'] is False
assert p['isIdentityAdmin'] is False
assert p['emailVerified'] is True
PY

ADMIN_RESPONSE="$(login 0003 APP_TEST_LOGIN)"
ADMIN_TOKEN="$(token_from "$ADMIN_RESPONSE")"
ADMIN_RESPONSE="$ADMIN_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['ADMIN_RESPONSE'])['principal']
assert p['workspaceRole'] == 'ADMIN'
assert p['isIdentityAdmin'] is False
PY

IDENTITY_ADMIN_RESPONSE="$(login 0004 APP_TEST_LOGIN)"
IDENTITY_ADMIN_TOKEN="$(token_from "$IDENTITY_ADMIN_RESPONSE")"
IDENTITY_ADMIN_RESPONSE="$IDENTITY_ADMIN_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['IDENTITY_ADMIN_RESPONSE'])['principal']
assert p['workspaceRole'] == 'ADMIN'
assert p['isIdentityAdmin'] is True
PY

SUPER_RESPONSE="$(login 0001 APP_TEST_LOGIN)"
SUPER_TOKEN="$(token_from "$SUPER_RESPONSE")"
SUPER_RESPONSE="$SUPER_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['SUPER_RESPONSE'])['principal']
assert p['workspaceRole'] == 'SUPER_ADMIN'
assert p['isWorkspaceSuperAdmin'] is True
PY

# Group access must no longer authorize a non-core App.
LEGACY_STATUS="$(curl --silent --output "$STATE_DIR/legacy.json" --write-out '%{http_code}' \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/login" \
  -H 'content-type: application/json' \
  --data '{"workspaceId":"workspace-test-001","applicationId":"APP_TEST_EXTRA","employeeNo":"0002","password":"test-pass-123"}')"
[[ "$LEGACY_STATUS" == "403" ]]

# Normal ADMIN cannot configure Access.
NORMAL_ADMIN_ACCESS_STATUS="$(curl --silent --output "$STATE_DIR/admin-access.json" --write-out '%{http_code}' \
  -X PUT "http://127.0.0.1:${PORT}/v1/admin/identity/employees/employee-test-user/applications/APP_TEST_EXTRA" \
  -H "authorization: Bearer ${ADMIN_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' \
  -H 'content-type: application/json' \
  --data '{"enabled":true}')"
[[ "$NORMAL_ADMIN_ACCESS_STATUS" == "403" ]]

# Identity Admin grants direct access to another Employee.
curl --silent --show-error --fail-with-body \
  -X PUT "http://127.0.0.1:${PORT}/v1/admin/identity/employees/employee-test-user/applications/APP_TEST_EXTRA" \
  -H "authorization: Bearer ${IDENTITY_ADMIN_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' \
  -H 'content-type: application/json' \
  --data '{"enabled":true}' >/dev/null

EXTRA_RESPONSE="$(login 0002 APP_TEST_EXTRA)"
EXTRA_RESPONSE="$EXTRA_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['EXTRA_RESPONSE'])['principal']
assert p['workspaceRole'] == 'USER'
PY

SELF_ACCESS_STATUS="$(curl --silent --output "$STATE_DIR/self-access.json" --write-out '%{http_code}' \
  -X PUT "http://127.0.0.1:${PORT}/v1/admin/identity/employees/employee-test-identity-admin/applications/APP_TEST_EXTRA" \
  -H "authorization: Bearer ${IDENTITY_ADMIN_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' \
  -H 'content-type: application/json' \
  --data '{"enabled":true}')"
[[ "$SELF_ACCESS_STATUS" == "409" ]]

CORE_ACCESS_STATUS="$(curl --silent --output "$STATE_DIR/core-access.json" --write-out '%{http_code}' \
  -X PUT "http://127.0.0.1:${PORT}/v1/admin/identity/employees/employee-test-user/applications/APP_TEST_LOGIN" \
  -H "authorization: Bearer ${IDENTITY_ADMIN_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' \
  -H 'content-type: application/json' \
  --data '{"enabled":false}')"
[[ "$CORE_ACCESS_STATUS" == "409" ]]

# Normal ADMIN can create USER, but not ADMIN. Email provider is intentionally
# absent in local acceptance; account creation must still succeed and report send failure.
CREATE_USER_RESPONSE="$(curl --silent --show-error --fail-with-body \
  -X POST "http://127.0.0.1:${PORT}/v1/admin/identity/employees" \
  -H "authorization: Bearer ${ADMIN_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' \
  -H 'content-type: application/json' \
  --data '{"employeeNo":"0100","displayName":"Pending User","email":"pending-user@example.test","roleKey":"USER"}')"
CREATE_USER_RESPONSE="$CREATE_USER_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['CREATE_USER_RESPONSE'])
assert p['ok'] is True
assert p['employee']['roleKey'] == 'USER'
assert p['employee']['pendingActivation'] is True
assert p['emailVerificationDelivery']['sent'] is False
PY

CREATE_ADMIN_STATUS="$(curl --silent --output "$STATE_DIR/create-admin.json" --write-out '%{http_code}' \
  -X POST "http://127.0.0.1:${PORT}/v1/admin/identity/employees" \
  -H "authorization: Bearer ${ADMIN_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' \
  -H 'content-type: application/json' \
  --data '{"employeeNo":"0101","displayName":"Forbidden Admin","email":"forbidden-admin@example.test","roleKey":"ADMIN"}')"
[[ "$CREATE_ADMIN_STATUS" == "403" ]]

CREATE_IA_ADMIN_RESPONSE="$(curl --silent --show-error --fail-with-body \
  -X POST "http://127.0.0.1:${PORT}/v1/admin/identity/employees" \
  -H "authorization: Bearer ${IDENTITY_ADMIN_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' \
  -H 'content-type: application/json' \
  --data '{"employeeNo":"0102","displayName":"Pending Admin","email":"pending-admin@example.test","roleKey":"ADMIN"}')"
CREATE_IA_ADMIN_RESPONSE="$CREATE_IA_ADMIN_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['CREATE_IA_ADMIN_RESPONSE'])
assert p['ok'] is True
assert p['employee']['roleKey'] == 'ADMIN'
PY

# Normal ADMIN can read the role/access management snapshot but cannot mutate Access.
SNAPSHOT_RESPONSE="$(curl --silent --show-error --fail-with-body \
  "http://127.0.0.1:${PORT}/v1/admin/identity/snapshot" \
  -H "authorization: Bearer ${ADMIN_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN')"
SNAPSHOT_RESPONSE="$SNAPSHOT_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['SNAPSHOT_RESPONSE'])
assert p['ok'] is True
assert p['actor']['workspaceRole'] == 'ADMIN'
assert p['actor']['isIdentityAdmin'] is False
apps = {a['application_id']: a for a in p['applications']}
assert apps['APP_TEST_LOGIN']['core_access_locked'] == 1
assert apps['APP_TEST_EXTRA']['core_access_locked'] == 0
PY

# Current Super Admin still cannot disable itself through normal Employee update.
DISABLE_SUPER_STATUS="$(curl --silent --output "$STATE_DIR/disable-super.json" --write-out '%{http_code}' \
  -X PATCH "http://127.0.0.1:${PORT}/v1/admin/identity/employees/employee-test-super" \
  -H "authorization: Bearer ${SUPER_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' \
  -H 'content-type: application/json' \
  --data '{"enabled":false,"revision":1}')"
[[ "$DISABLE_SUPER_STATUS" == "409" ]]

POLICY_RESPONSE="$(curl --silent --show-error --fail-with-body \
  "http://127.0.0.1:${PORT}/v1/admin/security-policy" \
  -H "authorization: Bearer ${SUPER_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN')"
POLICY_RESPONSE="$POLICY_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['POLICY_RESPONSE'])
assert p['ok'] is True
assert p['policy']['otpResendCooldownSeconds'] == 60
PY

NON_SUPER_POLICY_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  -X PUT "http://127.0.0.1:${PORT}/v1/admin/security-policy" \
  -H "authorization: Bearer ${IDENTITY_ADMIN_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' \
  -H 'content-type: application/json' \
  --data '{"otpResendCooldownSeconds":90,"otpMaxAttempts":6,"otpMaxSentPerEmailPurposeHour":4,"emailDailyLimit":40}')"
[[ "$NON_SUPER_POLICY_STATUS" == "403" ]]

curl --silent --show-error --fail-with-body \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/logout" \
  -H "authorization: Bearer ${USER_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' >/dev/null

REVOKED_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/session/resolve" \
  -H "authorization: Bearer ${USER_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN')"
[[ "$REVOKED_STATUS" == "401" ]]

echo 'PASS Workspace USER/ADMIN/SUPER_ADMIN principal projection'
echo 'PASS Identity Admin capability projection'
echo 'PASS locked core CY Web entry access'
echo 'PASS legacy Group grant no longer authorizes App entry'
echo 'PASS Identity Admin direct App Access management and self-escalation protection'
echo 'PASS normal ADMIN has no Access mutation authority'
echo 'PASS role-aware Employee creation boundary'
echo 'PASS create-time activation email failure preserves pending Employee'
echo 'PASS admin snapshot exposes role/access model'
echo 'PASS Super Admin protected lifecycle and security policy boundary'
echo 'PASS revoked session rejection'
