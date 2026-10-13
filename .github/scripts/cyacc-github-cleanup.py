"""One-time, reviewed GitHub metadata cleanup; no Cloudflare or accounting access."""
import json
import os
from pathlib import Path
import re
import sys
import time
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen

REPO = 'simonliu1118-byte/CYapps'
MANIFEST = Path('.github/cyacc-cleanup-manifest.json')
OWN_PATH = re.compile(r'^\.github/workflows/(?:cyaccountingweb-|cyacc-|cyaccounting-web-)[^/]+\.ya?ml$')
SELF_PATH = '.github/workflows/cyaccountingweb-maintenance-cleanup.yml'


def owned(path):
    return bool(OWN_PATH.fullmatch(path or '')) and path != SELF_PATH


class API:
    def __init__(self, apply):
        self.apply = apply
        self.changed = 0

    def call(self, path, method='GET'):
        if method != 'GET':
            assert self.apply, 'read-only inventory cannot mutate'
            time.sleep(1)
        request = Request('https://api.github.com/repos/' + REPO + '/' + path,
                          method=method, headers={
                              'Authorization': 'Bearer ' + os.environ['GH_TOKEN'],
                              'Accept': 'application/vnd.github+json',
                              'X-GitHub-Api-Version': '2022-11-28',
                          })
        try:
            with urlopen(request, timeout=45) as response:
                raw = response.read()
                if method != 'GET':
                    self.changed += 1
                return json.loads(raw) if raw else None
        except HTTPError as error:
            if error.code == 404:
                return None
            # Never print API bodies, headers or credentials.
            raise RuntimeError(f'GitHub {method} failed with HTTP {error.code}') from None

    def pages(self, path, key=None):
        separator = '&' if '?' in path else '?'
        rows = []
        for page in range(1, 101):
            data = self.call(f'{path}{separator}per_page=100&page={page}')
            assert data is not None, 'inventory endpoint must exist'
            batch = data[key] if key else data
            rows.extend(batch)
            if len(batch) < 100:
                return rows
        raise RuntimeError('inventory exceeds pagination limit; no partial inventory accepted')


def inventory(api, manifest):
    workflows = api.pages('actions/workflows', 'workflows')
    ours = [w for w in workflows if owned(w['path'])]
    retired = [w for w in ours if not Path(w['path']).is_file()]
    current = [w for w in ours if Path(w['path']).is_file()]
    opened = api.pages('pulls?state=open')
    active_branches = {p['head']['ref'] for p in opened}
    branches = []
    skipped = []
    for item in manifest['branches']:
        name = item['name']
        if name in manifest['preserveBranches'] or name in active_branches:
            skipped.append(name)
            continue
        ref = api.call('git/ref/heads/' + quote(name, safe=''))
        if ref is None:
            continue
        if ref['object']['sha'] != item['sha']:
            skipped.append(name)
            continue
        branches.append(item)
    runs = []
    unfinished = []
    for workflow in retired:
        for run in api.pages(f"actions/workflows/{workflow['id']}/runs", 'workflow_runs'):
            assert owned(run['path']) and run['workflow_id'] == workflow['id']
            if run['status'] == 'completed' and str(run['id']) != os.environ.get('GITHUB_RUN_ID'):
                runs.append(run)
            else:
                unfinished.append(run['id'])
    artifacts = []
    for artifact in api.pages('actions/artifacts', 'artifacts'):
        if not artifact['expired']:
            continue
        run = api.call(f"actions/runs/{artifact['workflow_run']['id']}")
        if run and owned(run['path']) and run['status'] == 'completed':
            artifacts.append(artifact)
    return {'current': current, 'retired': retired, 'branches': branches,
            'skipped': skipped, 'runs': runs, 'unfinished': unfinished, 'artifacts': artifacts}


def main():
    mode = sys.argv[1]
    assert mode in ('plan', 'apply')
    manifest = json.loads(MANIFEST.read_text())
    assert manifest['repository'] == REPO == os.environ['GITHUB_REPOSITORY']
    if mode == 'apply':
        assert os.environ['GITHUB_EVENT_NAME'] == 'push' and os.environ['GITHUB_REF'] == 'refs/heads/main'
    api = API(mode == 'apply')
    plan = inventory(api, manifest)
    summary = {'mode': mode, 'preserved_workflows': [w['path'] for w in plan['current']],
               'retired_workflows': [w['path'] for w in plan['retired']],
               'branch_candidates': [b['name'] for b in plan['branches']],
               'preserved_changed_or_open_branches': plan['skipped'],
               'completed_retired_runs': len(plan['runs']),
               'preserved_unfinished_runs': plan['unfinished'],
               'expired_artifacts': len(plan['artifacts'])}
    print('CLEANUP_INVENTORY=' + json.dumps(summary, ensure_ascii=False), flush=True)
    if mode == 'apply':
        for item in plan['branches']:
            # Re-read both head and open PRs immediately before removing a reviewed ref.
            name = item['name']
            ref = api.call('git/ref/heads/' + quote(name, safe=''))
            opened = api.call('pulls?state=open&head=' + quote('simonliu1118-byte:' + name, safe=''))
            if ref and ref['object']['sha'] == item['sha'] and opened == []:
                api.call('git/refs/heads/' + quote(name, safe=''), 'DELETE')
        for run in plan['runs']:
            live = api.call(f"actions/runs/{run['id']}")
            if live and live['status'] == 'completed' and live['workflow_id'] == run['workflow_id']:
                api.call(f"actions/runs/{run['id']}", 'DELETE')
        for artifact in plan['artifacts']:
            live = api.call(f"actions/artifacts/{artifact['id']}")
            if live and live['expired']:
                api.call(f"actions/artifacts/{artifact['id']}", 'DELETE')
        remaining = inventory(api, manifest)
        summary['remaining_reviewed_branches'] = len(remaining['branches'])
        summary['remaining_completed_retired_runs'] = len(remaining['runs'])
        summary['remaining_expired_artifacts'] = len(remaining['artifacts'])
        summary['mutations'] = api.changed
        assert not remaining['branches'] and not remaining['runs'] and not remaining['artifacts']
        print('CLEANUP_RESULT=' + json.dumps(summary, ensure_ascii=False), flush=True)
    Path(os.environ['GITHUB_STEP_SUMMARY']).write_text('```json\n' + json.dumps(summary, ensure_ascii=False, indent=2) + '\n```\n')


if __name__ == '__main__':
    main()
