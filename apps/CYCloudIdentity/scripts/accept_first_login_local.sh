#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CONFIG="wrangler.first-login-test.generated.jsonc"
STATE_DIR="$(mktemp -d)"
PORT="8792"
WORKER_PID=""
WORKER_LOG="$STATE_DIR/worker.log"

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
Path('wrangler.first-login-test.generated.jsonc').write_text(
    template.replace('__D1_DATABASE_ID__', db_id),
    encoding='utf-8',
)
PY

npx wrangler d1 migrations apply DB --local --config "$CONFIG" --persist-to "$STATE_DIR" >/dev/null

TEMP_VERIFIER="$(python3 - <<'PY'
import hashlib
password = b'TempP4ss'
salt = bytes.fromhex('102132435465768798a9bacbdcedfe0f')
iterations = 100_000
digest = hashlib.pbkdf2_hmac('sha256', password, salt, iterations, 32)
print('pbkdf2-sha256$%s$%s$%s' % (iterations, salt.hex(), digest.hex()))
PY
)"
SUPER_VERIFIER="$(python3 - <<'PY'
import hashlib
password = b'test-pass-123'
salt = bytes.fromhex('00112233445566778899aabbccddeeff')
iterations = 100_000
digest = hashlib.pbkdf2_hmac('sha256', password, salt, iterations, 32)
print('pbkdf2-sha256$%s$%s$%s' % (iterations, salt.hex(), digest.hex()))
PY
)"

SEED_SQL=$(cat <<SQL
INSERT INTO workspaces(workspace_id, workspace_code, display_name, status)
VALUES('workspace-first-login', 'FIRST01', 'First Login Acceptance', 'bootstrap');

INSERT INTO employees(
  employee_id, workspace_id, employee_no, name, email_normalized,
  email_verified_at, enabled, role_key, identity_admin, activated_at
)
VALUES
  ('employee-first-super', 'workspace-first-login', '9001', 'Synthetic Super', 'super@example.test', '2026-09-29T00:00:00.000Z', 1, 'ADMIN', 0, '2026-09-29T00:00:00.000Z'),
  ('employee-first-pending', 'workspace-first-login', '9002', 'Synthetic Pending', 'pending@example.test', NULL, 0, 'USER', 0, NULL),
  ('employee-first-expired', 'workspace-first-login', '9003', 'Synthetic Expired', 'expired@example.test', NULL, 0, 'USER', 0, NULL),
  ('employee-first-resend', 'workspace-first-login', '9004', 'Synthetic Resend', 'resend@example.test', NULL, 0, 'USER', 0, NULL),
  ('employee-first-edit', 'workspace-first-login', '9005', 'Synthetic Edit', 'edit@example.test', NULL, 0, 'USER', 0, NULL);

UPDATE workspaces
SET super_admin_employee_id = 'employee-first-super', status = 'active'
WHERE workspace_id = 'workspace-first-login';

INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version)
VALUES('employee-first-super', 'pbkdf2-sha256', '$SUPER_VERIFIER', 1);

INSERT INTO employee_initial_credentials(
  employee_id, algorithm, verifier, exchange_token_digest, exchange_expires_at,
  issued_at, expires_at, sent_at, verified_at, revision
)
VALUES
  ('employee-first-pending', 'pbkdf2-sha256', '$TEMP_VERIFIER', NULL, NULL,
   '2026-09-29T00:00:00.000Z', '2099-01-01T00:00:00.000Z', '2026-09-29T00:00:01.000Z', NULL, 1),
  ('employee-first-expired', 'pbkdf2-sha256', '$TEMP_VERIFIER', NULL, NULL,
   '2000-01-01T00:00:00.000Z', '2000-01-02T00:00:00.000Z', '2000-01-01T00:00:01.000Z', NULL, 1),
  ('employee-first-resend', 'pbkdf2-sha256', '$TEMP_VERIFIER', NULL, NULL,
   '2026-09-29T00:00:00.000Z', '2099-01-01T00:00:00.000Z', '2000-01-01T00:00:01.000Z', NULL, 1),
  ('employee-first-edit', 'pbkdf2-sha256', '$TEMP_VERIFIER', NULL, NULL,
   '2026-09-29T00:00:00.000Z', '2099-01-01T00:00:00.000Z', '2099-01-01T00:00:00.000Z', NULL, 1);

INSERT INTO applications(application_id, display_name)
VALUES('APP_TEST_LOGIN', 'Synthetic Core App'), ('APP_TEST_OTHER', 'Synthetic Other App');
INSERT INTO workspace_applications(workspace_id, application_id, enabled)
VALUES('workspace-first-login', 'APP_TEST_LOGIN', 1), ('workspace-first-login', 'APP_TEST_OTHER', 1);
SQL
)

npx wrangler d1 execute DB --local --config "$CONFIG" --persist-to "$STATE_DIR" --command "$SEED_SQL" >/dev/null

npx wrangler dev --local --config "$CONFIG" --persist-to "$STATE_DIR" --port "$PORT" >"$WORKER_LOG" 2>&1 &
WORKER_PID="$!"

READY=0
for _ in $(seq 1 30); do
  if ! kill -0 "$WORKER_PID" >/dev/null 2>&1; then
    cat "$WORKER_LOG" >&2 || true
    exit 1
  fi
  if curl --silent --fail "http://127.0.0.1:${PORT}/v1/health" >/dev/null; then READY=1; break; fi
  sleep 1
done
[[ "$READY" == "1" ]]

FIRST_RESPONSE="$(curl --silent --show-error --fail-with-body   -X POST "http://127.0.0.1:${PORT}/v1/identity/login"   -H 'content-type: application/json'   --data '{"workspaceId":"workspace-first-login","applicationId":"APP_TEST_LOGIN","employeeNo":"9002","password":"TempP4ss"}')"

FIRST_TOKEN="$(FIRST_RESPONSE="$FIRST_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['FIRST_RESPONSE'])
assert p['ok'] is True
assert p['passwordChangeRequired'] is True
assert 'session' not in p
f = p['firstLogin']
assert f['employeeNo'] == '9002'
assert f['token'].startswith('cyif_') and len(f['token']) == 69
print(f['token'])
PY
)"

REUSE_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}'   -X POST "http://127.0.0.1:${PORT}/v1/identity/login"   -H 'content-type: application/json'   --data '{"workspaceId":"workspace-first-login","applicationId":"APP_TEST_LOGIN","employeeNo":"9002","password":"TempP4ss"}')"
[[ "$REUSE_STATUS" == "401" ]]

OTHER_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}'   -X POST "http://127.0.0.1:${PORT}/v1/identity/login"   -H 'content-type: application/json'   --data '{"workspaceId":"workspace-first-login","applicationId":"APP_TEST_OTHER","employeeNo":"9002","password":"TempP4ss"}')"
[[ "$OTHER_STATUS" == "401" ]]

TICKET_SESSION_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}'   -X POST "http://127.0.0.1:${PORT}/v1/identity/session/resolve"   -H "authorization: Bearer ${FIRST_TOKEN}"   -H 'x-identity-application: APP_TEST_LOGIN')"
[[ "$TICKET_SESSION_STATUS" == "401" ]]

EXPIRED_STATUS="$(curl --silent --output "$STATE_DIR/expired.json" --write-out '%{http_code}'   -X POST "http://127.0.0.1:${PORT}/v1/identity/login"   -H 'content-type: application/json'   --data '{"workspaceId":"workspace-first-login","applicationId":"APP_TEST_LOGIN","employeeNo":"9003","password":"TempP4ss"}')"
[[ "$EXPIRED_STATUS" == "401" ]]
python3 - "$STATE_DIR/expired.json" <<'PY'
import json, pathlib, sys
p = json.loads(pathlib.Path(sys.argv[1]).read_text())
assert p['error']['code'] == 'FIRST_LOGIN_PASSWORD_EXPIRED'
PY

COMPLETE_RESPONSE="$(curl --silent --show-error --fail-with-body   -X POST "http://127.0.0.1:${PORT}/v1/identity/first-login/complete"   -H 'content-type: application/json'   --data "{\"workspaceId\":\"workspace-first-login\",\"applicationId\":\"APP_TEST_LOGIN\",\"token\":\"${FIRST_TOKEN}\",\"password\":\"new-pass-123\"}")"

COMPLETE_RESPONSE="$COMPLETE_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['COMPLETE_RESPONSE'])
assert p['ok'] is True
assert p['emailVerified'] is True
assert p['passwordChanged'] is True
assert p['reloginRequired'] is True
assert 'session' not in p
assert 'principal' not in p
PY

REPLAY_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}'   -X POST "http://127.0.0.1:${PORT}/v1/identity/first-login/complete"   -H 'content-type: application/json'   --data "{\"workspaceId\":\"workspace-first-login\",\"applicationId\":\"APP_TEST_LOGIN\",\"token\":\"${FIRST_TOKEN}\",\"password\":\"another-pass-1\"}")"
[[ "$REPLAY_STATUS" == "400" ]]

NEW_LOGIN_RESPONSE="$(curl --silent --show-error --fail-with-body   -X POST "http://127.0.0.1:${PORT}/v1/identity/login"   -H 'content-type: application/json'   --data '{"workspaceId":"workspace-first-login","applicationId":"APP_TEST_LOGIN","employeeNo":"9002","password":"new-pass-123"}')"
NEW_LOGIN_RESPONSE="$NEW_LOGIN_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['NEW_LOGIN_RESPONSE'])
assert p['ok'] is True
assert p['principal']['emailVerified'] is True
assert p['session']['token'].startswith('cyid_')
PY

SUPER_LOGIN_RESPONSE="$(curl --silent --show-error --fail-with-body   -X POST "http://127.0.0.1:${PORT}/v1/identity/login"   -H 'content-type: application/json'   --data '{"workspaceId":"workspace-first-login","applicationId":"APP_TEST_LOGIN","employeeNo":"9001","password":"test-pass-123"}')"
SUPER_TOKEN="$(SUPER_LOGIN_RESPONSE="$SUPER_LOGIN_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['SUPER_LOGIN_RESPONSE'])
print(p['session']['token'])
PY
)"

RESEND_STATUS="$(curl --silent --output "$STATE_DIR/resend.json" --write-out '%{http_code}'   -X POST "http://127.0.0.1:${PORT}/v1/admin/identity/employees/employee-first-resend/activation/resend"   -H "authorization: Bearer ${SUPER_TOKEN}"   -H 'x-identity-application: APP_TEST_LOGIN')"
[[ "$RESEND_STATUS" == "503" ]]
RESEND_OLD_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}'   -X POST "http://127.0.0.1:${PORT}/v1/identity/login"   -H 'content-type: application/json'   --data '{"workspaceId":"workspace-first-login","applicationId":"APP_TEST_LOGIN","employeeNo":"9004","password":"TempP4ss"}')"
[[ "$RESEND_OLD_STATUS" == "401" ]]

EDIT_STATUS="$(curl --silent --output "$STATE_DIR/edit.json" --write-out '%{http_code}'   -X PATCH "http://127.0.0.1:${PORT}/v1/admin/identity/employees/employee-first-edit"   -H "authorization: Bearer ${SUPER_TOKEN}"   -H 'x-identity-application: APP_TEST_LOGIN'   -H 'content-type: application/json'   --data '{"email":"edit-new@example.test","revision":1}')"
[[ "$EDIT_STATUS" == "200" ]]
python3 - "$STATE_DIR/edit.json" <<'PY'
import json, pathlib, sys
p = json.loads(pathlib.Path(sys.argv[1]).read_text())
assert p['employee']['email'] == 'edit-new@example.test'
assert p['emailVerificationDelivery']['sent'] is False
assert p['activationDelivery']['sent'] is False
PY
EDIT_OLD_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}'   -X POST "http://127.0.0.1:${PORT}/v1/identity/login"   -H 'content-type: application/json'   --data '{"workspaceId":"workspace-first-login","applicationId":"APP_TEST_LOGIN","employeeNo":"9005","password":"TempP4ss"}')"
[[ "$EDIT_OLD_STATUS" == "401" ]]

echo 'PASS first-login password is expiring, core-app-only and truly single-use'
echo 'PASS first-login ticket is not a session and cannot be replayed'
echo 'PASS first-login completion verifies Email but requires explicit permanent-password re-login'
echo 'PASS resend and pending Email edit invalidate the previous initial credential even when delivery fails'
