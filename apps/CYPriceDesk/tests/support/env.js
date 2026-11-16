import fs from 'node:fs';
import path from 'node:path';
import {createD1} from './fake-d1.js';
import {createFakeIdentity, APPLICATION_ID, WORKSPACE_ID} from './fake-identity.js';

export const FETCHER_SECRET = 'test-secret';

const PUBLIC_DIR = new URL('../../public/', import.meta.url).pathname;
const TYPES = {'.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8'};

const ASSETS = {
  async fetch(request) {
    const file = path.join(PUBLIC_DIR, new URL(request.url).pathname);
    if (!file.startsWith(PUBLIC_DIR) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) return new Response('not found', {status: 404});
    return new Response(fs.readFileSync(file), {headers: {'content-type': TYPES[path.extname(file)] || 'application/octet-stream'}});
  }
};

export function createTestEnv(options) {
  const identity = createFakeIdentity(options);
  const DB = createD1();
  return {
    identity,
    env: {
      DB, ASSETS, IDENTITY: identity,
      CYID_APPLICATION_ID: APPLICATION_ID, CYID_WORKSPACE_ID: WORKSPACE_ID, CYWEB_PORTAL_URL: 'https://portal.example.test/',
      CYPD_FETCHER_URL: 'https://fetcher.example.test', CYPD_FETCHER_SECRET: FETCHER_SECRET
    }
  };
}
