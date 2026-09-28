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

INSERT INTO employees(employee_id, workspace_id, employee_no, name, email_normalized, email_verified_at, enabled)
VALUES
  ('employee-test-super', 'workspace-test-001', '0001', 'Synthetic Super', 'super@example.test', '2026-09-28T00:00:00.000Z', 1),
  ('employee-test-user', 'workspace-test-001', '0002', 'Synthetic User', 'user@example.test', '2026-09-28T00:00:00.000Z', 1);

UPDATE workspaces
SET super_admin_employee_id = 'employee-test-super',
    recovery_email_normalized = 'super@example.test',
    recovery_email_verified_at = '2026-09-28T00:00:00.000Z',
    status = 'active'
WHERE workspace_id = 'workspace-test-001';

INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version)
VALUES
  ('employee-test-super', 'pbkdf2-sha256', '$VERIFIER', 1),
  ('employee-test-user', 'pbkdf2-sha256', '$VERIFIER', 1);

INSERT INTO applications(application_id, display_name)
VALUES('APP_TEST_LOGIN', 'Synthetic Login App');

INSERT INTO workspace_applications(workspace_id, application_id, enabled)
VALUES('workspace-test-001', 'APP_TEST_LOGIN', 1);

INSERT INTO identity_groups(group_id, workspace_id, group_key, display_name)
VALUES('group-test-users', 'workspace-test-001', 'TEST_USERS', 'Synthetic Test Users');

INSERT INTO employee_identity_groups(workspace_id, employee_id, group_id)
VALUES('workspace-test-001', 'employee-test-user', 'group-test-users');

INSERT INTO identity_group_application_access(workspace_id, group_id, application_id, enabled)
VALUES('workspace-test-001', 'group-test-users', 'APP_TEST_LOGIN', 1);
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

LOGIN_RESPONSE="$(curl --silent --show-error --fail-with-body \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/login" \
  -H 'content-type: application/json' \
  --data '{"workspaceId":"workspace-test-001","applicationId":"APP_TEST_LOGIN","employeeNo":"0002","password":"test-pass-123"}')"

TOKEN="$(LOGIN_RESPONSE="$LOGIN_RESPONSE" python3 - <<'PY'
import json
import os
payload = json.loads(os.environ['LOGIN_RESPONSE'])
assert payload['ok'] is True
principal = payload['principal']
assert principal['employeeNo'] == '0002'
assert principal['isWorkspaceSuperAdmin'] is False
assert principal['groupKeys'] == ['TEST_USERS']
token = payload['session']['token']
assert token.startswith('cyid_') and len(token) == 69
print(token)
PY
)"

curl --silent --show-error --fail-with-body \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/session/resolve" \
  -H "authorization: Bearer ${TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' >/dev/null

SUPER_RESPONSE="$(curl --silent --show-error --fail-with-body \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/login" \
  -H 'content-type: application/json' \
  --data '{"workspaceId":"workspace-test-001","applicationId":"APP_TEST_LOGIN","employeeNo":"0001","password":"test-pass-123"}')"

SUPER_TOKEN="$(SUPER_RESPONSE="$SUPER_RESPONSE" python3 - <<'PY'
import json
import os
payload = json.loads(os.environ['SUPER_RESPONSE'])
assert payload['ok'] is True
assert payload['principal']['isWorkspaceSuperAdmin'] is True
token = payload['session']['token']
assert token.startswith('cyid_') and len(token) == 69
print(token)
PY
)"

POLICY_RESPONSE="$(curl --silent --show-error --fail-with-body \
  "http://127.0.0.1:${PORT}/v1/admin/security-policy" \
  -H "authorization: Bearer ${SUPER_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN')"
POLICY_RESPONSE="$POLICY_RESPONSE" python3 - <<'PY'
import json
import os
payload = json.loads(os.environ['POLICY_RESPONSE'])
assert payload['ok'] is True
assert payload['systemEmailDailyCeiling'] == 50
assert payload['policy']['otpResendCooldownSeconds'] == 60
assert payload['policy']['otpMaxAttempts'] == 5
assert payload['policy']['otpMaxSentPerEmailPurposeHour'] == 5
assert payload['policy']['emailDailyLimit'] == 50
assert payload['policy']['revision'] == 0
PY

UPDATED_POLICY_RESPONSE="$(curl --silent --show-error --fail-with-body \
  -X PUT "http://127.0.0.1:${PORT}/v1/admin/security-policy" \
  -H "authorization: Bearer ${SUPER_TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' \
  -H 'content-type: application/json' \
  --data '{"otpResendCooldownSeconds":90,"otpMaxAttempts":6,"otpMaxSentPerEmailPurposeHour":4,"emailDailyLimit":40}')"
UPDATED_POLICY_RESPONSE="$UPDATED_POLICY_RESPONSE" python3 - <<'PY'
import json
import os
payload = json.loads(os.environ['UPDATED_POLICY_RESPONSE'])
assert payload['ok'] is True
assert payload['policy'] == {
    'otpResendCooldownSeconds': 90,
    'otpMaxAttempts': 6,
    'otpMaxSentPerEmailPurposeHour': 4,
    'emailDailyLimit': 40,
    'revision': 1,
}
PY

NON_SUPER_STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  -X PUT "http://127.0.0.1:${PORT}/v1/admin/security-policy" \
  -H "authorization: Bearer ${TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' \
  -H 'content-type: application/json' \
  --data '{"otpResendCooldownSeconds":90,"otpMaxAttempts":6,"otpMaxSentPerEmailPurposeHour":4,"emailDailyLimit":40}')"
if [[ "$NON_SUPER_STATUS" != "403" ]]; then
  echo "Expected non-super security policy update to return 403, got $NON_SUPER_STATUS" >&2
  print_worker_log
  exit 1
fi

curl --silent --show-error --fail-with-body \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/logout" \
  -H "authorization: Bearer ${TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN' >/dev/null

STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  -X POST "http://127.0.0.1:${PORT}/v1/identity/session/resolve" \
  -H "authorization: Bearer ${TOKEN}" \
  -H 'x-identity-application: APP_TEST_LOGIN')"
if [[ "$STATUS" != "401" ]]; then
  echo "Expected revoked session to return 401, got $STATUS" >&2
  print_worker_log
  exit 1
fi

echo 'PASS CYCloud Identity local login/session/logout roundtrip'
echo 'PASS group-based application access'
echo 'PASS Workspace highest-authority application access'
echo 'PASS highest-authority OTP security policy management'
echo 'PASS non-highest-authority policy update rejection'
echo 'PASS revoked session rejection'
