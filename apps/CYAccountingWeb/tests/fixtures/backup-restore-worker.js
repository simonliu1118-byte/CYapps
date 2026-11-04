import { verifyRestoreInMemory } from '../../src/backup-restore-runtime.js';
export default {
  async fetch(request) {
    try {
      const input = await request.json();
      const result = [];
      for (const backup of Array.isArray(input) ? input : [input]) result.push(await verifyRestoreInMemory(backup));
      return Response.json(Array.isArray(input) ? result : result[0]);
    } catch (error) { return Response.json({ error: error.message, stack: error.stack }, { status: 500 }); }
  }
};
