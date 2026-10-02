import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const lifecycle = read('src/account-lifecycle.js');
const worker = read('src/app.js');
const core = read('src/index.js');
const tools = read('src/accounting-tools.js');
const migration = read('migrations/0005_account_archival.sql');
const openingMigration = read('migrations/0006_opening_balance_overrides.sql');
const openings = read('src/opening-balances.js');
const ui = read('public/app.js');

assert.match(worker, /handleAccountLifecycleApi\(request, env, resolved\.principal\)/);

assert.match(lifecycle, /workspaceRole \|\| ''\) !== 'SUPER_ADMIN'/);
assert.match(lifecycle, /ACCOUNT_MUST_BE_ARCHIVED/);
assert.match(lifecycle, /ACCOUNT_HAS_HISTORY/);
assert.match(lifecycle, /SELECT COUNT\(\*\) AS count FROM transactions WHERE account_name = \?/);
assert.match(lifecycle, /currentOpeningUsageForAccount\(db, name\)/);
assert.match(openings, /ORDER BY month DESC LIMIT 1/);
assert.match(openings, /latestOverrideAmount/);
assert.match(lifecycle, /DELETE FROM accounts WHERE id = \? AND archived_at IS NOT NULL/);
assert.doesNotMatch(lifecycle, /DELETE FROM transactions/);
assert.match(lifecycle, /latestOpeningAmount !== 0/);
assert.match(lifecycle, /DELETE FROM opening_balance_overrides WHERE account_name = \? AND amount = 0/);

assert.match(core, /archivedAccounts:/);
assert.match(core, /WHERE archived_at IS NULL/);
assert.match(core, /WHERE a\.archived_at IS NOT NULL/);
assert.match(core, /name = \? AND archived_at IS NULL/);
assert.doesNotMatch(core, /request\.method === 'DELETE'\) return handleDeleteAccount/);
assert.match(core, /SELECT 1 FROM opening_balance_audit WHERE account_name = \? LIMIT 1/);
assert.match(core, /latest_opening_amount/);

assert.match(tools, /SELECT id FROM accounts WHERE archived_at IS NULL ORDER BY sort_order, id/);
assert.match(tools, /moveWithin\(db, 'accounts', 'archived_at IS NULL'/);

assert.match(migration, /ALTER TABLE accounts ADD COLUMN archived_at TEXT/);
assert.match(migration, /INSERT OR IGNORE INTO accounts\(name, sort_order, is_default, created_at, archived_at\)/);
assert.match(migration, /SELECT DISTINCT account_name AS name[\s\S]*FROM transactions/);
assert.match(migration, /SELECT DISTINCT account_name AS name[\s\S]*FROM opening_balances/);
assert.match(openingMigration, /CREATE TABLE opening_balance_overrides/);
assert.match(openingMigration, /CREATE TABLE opening_balance_audit/);
assert.match(openingMigration, /DROP TABLE opening_balances/);

assert.match(ui, /async function archiveAccountOptimistically\(id\)/);
assert.match(ui, /async function restoreAccountOptimistically\(id\)/);
assert.match(ui, /async function permanentlyDeleteArchivedAccount\(id\)/);
assert.match(ui, /\/api\/accounts\/\$\{id\}\/archive/);
assert.match(ui, /\/api\/accounts\/\$\{id\}\/restore/);
assert.match(ui, /\/api\/accounts\/\$\{id\}\/permanent/);

console.log('Account lifecycle regression checks passed.');
