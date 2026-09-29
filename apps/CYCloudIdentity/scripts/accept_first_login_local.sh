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
print(f'pbkdf2-sha256${iterations}${salt.hex()}${digest.hex()}')
PY
)"
SUPER_VERIFIER="$(python3 - <<'PY'
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
VALUES('workspace-first-login', 'FIRST01', 'First Login Acceptance', 'bootstrap');

INSERT INTO employees(
  employee_id, workspace_id, employee_no, name, email_normalized,
  email_verified_at, enabled, role_key, identity_admin, activated_at
)
VALUES
  ('employee-first-super', 'workspace-first-login', '9001', 'Synthetic Super', 'super@example.test', '2026-09-29T00:00:00.000Z', 1, 'ADMIN', 0, '2026-09-29T00:00:00.000Z'),
  ('employee-first-pending', 'workspace-first-login', '9002', 'Synthetic Pending', 'pending@example.test', NULL, 0, 'USER', 0, NULL);

UPDATE workspaces
SET super_admin_employee_id = 'employee-first-super', status = 'active'
WHERE workspace_id = 'workspace-first-login';

INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version)
VALUES('employee-first-super', 'pbkdf2-sha256', '$SUPER_VERIFIER', 1);

INSERT INTO employee_initial_credentials(
  employee_id, algorithm, verifier, exchange_token_digest, exchange_expires_at, issued_at, sent_at, revision
)
VALUES(
  'employee-first-pending', 'pbkdf2-sha256', '$TEMP_VERIFIER', NULL, NULL,
  '2026-09-29T00:00:00.000Z', '2026-09-29T00:00:01.000Z', 1
);

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

FIRST_RESPONSE="$(curl --silent --show-error --fail-with-body \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/login" \
  -H 'content-type: application/json' \
  --data '{"workspaceId":"workspace-first-login","applicationId":"APP_TEST_LOGIN","employeeNo":"9002","password":"TempP4ss"}')"

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

# The temporary credential is accepted only by the core account application.
OTHER_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/login" \
  -H 'content-type: application/json' \
  --data '{"workspaceId":"workspace-first-login","applicationId":"APP_TEST_OTHER","employeeNo":"9002","password":"TempP4ss"}')"
[[ "$OTHER_STATUS" == "401" ]]

# A first-login ticket is not an Identity session token.
TICKET_SESSION_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/session/resolve" \
  -H "authorization: Bearer ${FIRST_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN')"
[[ "$TICKET_SESSION_STATUS" == "401" ]]

COMPLETE_RESPONSE="$(curl --silent --show-error --fail-with-body \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/first-login/complete" \
  -H 'content-type: application/json' \
  --data "{\"workspaceId\":\"workspace-first-login\",\"applicationId\":\"APP_TEST_LOGIN\",\"token\":\"${FIRST_TOKEN}\",\"password\":\"new-pass-123\"}")"

COMPLETE_RESPONSE="$COMPLETE_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['COMPLETE_RESPONSE'])
assert p['ok'] is True
assert p['principal']['employeeNo'] == '9002'
assert p['principal']['emailVerified'] is True
assert p['principal']['workspaceRole'] == 'USER'
assert p['session']['token'].startswith('cyid_')
PY

# The one-time password and first-login ticket are both unusable after completion.
OLD_PASSWORD_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/login" \
  -H 'content-type: application/json' \
  --data '{"workspaceId":"workspace-first-login","applicationId":"APP_TEST_LOGIN","employeeNo":"9002","password":"TempP4ss"}')"
[[ "$OLD_PASSWORD_STATUS" == "401" ]]

REPLAY_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/first-login/complete" \
  -H 'content-type: application/json' \
  --data "{\"workspaceId\":\"workspace-first-login\",\"applicationId\":\"APP_TEST_LOGIN\",\"token\":\"${FIRST_TOKEN}\",\"password\":\"another-pass-1\"}")"
[[ "$REPLAY_STATUS" == "400" ]]

NEW_LOGIN_RESPONSE="$(curl --silent --show-error --fail-with-body \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/login" \
  -H 'content-type: application/json' \
  --data '{"workspaceId":"workspace-first-login","applicationId":"APP_TEST_LOGIN","employeeNo":"9002","password":"new-pass-123"}')"
NEW_LOGIN_RESPONSE="$NEW_LOGIN_RESPONSE" python3 - <<'PY'
import json, os
p = json.loads(os.environ['NEW_LOGIN_RESPONSE'])
assert p['ok'] is True
assert p['principal']['emailVerified'] is True
assert p['session']['token'].startswith('cyid_')
PY

echo 'PASS one-time Email password enters forced first-login flow without session'
echo 'PASS first-login ticket is core-app-only, short-lived authority and not a session'
echo 'PASS permanent password completion verifies Email, activates account and creates normal session'
echo 'PASS one-time password and first-login ticket cannot be reused after completion'
