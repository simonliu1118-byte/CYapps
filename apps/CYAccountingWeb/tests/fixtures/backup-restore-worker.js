import { verifyRestoreInMemory, advanceRestoreSnapshot } from '../../src/backup-restore-runtime.js';
export default {
  async fetch(request, env) {
    try {
      const path = new URL(request.url).pathname;
      if (path === '/batch/start') {
        await env.SNAPSHOTS.put('backup', await request.text());
        await env.SNAPSHOTS.delete(['snapshot','cursor']);
        return Response.json({ok:true});
      }
      if (path === '/batch/step') {
        const input = await (await env.SNAPSHOTS.get('backup')).json();
        const prior = await env.SNAPSHOTS.get('snapshot'), cursor = await env.SNAPSHOTS.get('cursor');
        const result = await advanceRestoreSnapshot(prior ? new Uint8Array(await prior.arrayBuffer()) : null,
          input, cursor ? await cursor.json() : null);
        if (result.snapshot) await env.SNAPSHOTS.put('snapshot',result.snapshot);
        await env.SNAPSHOTS.put('cursor',JSON.stringify(result.cursor));
        return Response.json({proof:result.proof,cursor:result.cursor,written:result.written});
      }
      const input = await request.json();
      const result = [];
      for (const backup of Array.isArray(input) ? input : [input]) result.push(await verifyRestoreInMemory(backup));
      return Response.json(Array.isArray(input) ? result : result[0]);
    } catch (error) { return Response.json({ error: error.message, stack: error.stack }, { status: 500 }); }
  }
};
