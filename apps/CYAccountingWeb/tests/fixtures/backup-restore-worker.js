import { verifyRestoreInMemory } from '../../src/backup-restore-runtime.js';
export default {
  async fetch(request) {
    try {
      const result = await verifyRestoreInMemory(await request.json());
      return Response.json(result);
    } catch (error) { return Response.json({ error: error.message, stack: error.stack }, { status: 500 }); }
  }
};
