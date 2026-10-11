// Isolated, short-lived transport probe. No public authority or business endpoint.
export default {
  async fetch(request, env) {
    if (request.method !== 'GET' || new URL(request.url).pathname !== '/probe' ||
        !env.PROBE_AUTH_KEY || request.headers.get('x-probe-key') !== env.PROBE_AUTH_KEY ||
        !/^\d+$/.test(env.PROBE_EXPIRES_AT ?? '') || !Number.isSafeInteger(Number(env.PROBE_EXPIRES_AT)) ||
        Number(env.PROBE_EXPIRES_AT) <= Date.now())
      return new Response(null, { status: 404 });
    try {
      const response = await env.IDENTITY.fetch('https://identity.invalid/v1/health');
      const health = await response.json();
      if (!response.ok || health.ok !== true || health.service !== 'cycloud-identity' ||
          health.environment !== 'development' || health.serviceVersion !== env.EXPECTED_VERSION)
        throw new Error('PROVIDER_FETCHER_HEALTH_FAILED');
      const denied = await env.IDENTITY.fetch('https://identity.invalid/v1/identity/session/resolve', {
        method: 'POST', headers: { authorization: `Bearer ${env.INVALID_SESSION}`, 'x-identity-application': env.CORE_APPLICATION },
      });
      if (denied.status !== 401 || (await denied.json()).error?.code !== 'SESSION_INVALID')
        throw new Error('PROVIDER_FETCHER_DENIAL_FAILED');
      const input = { workspaceId: env.SYNTHETIC_WORKSPACE, applicationId: env.SYNTHETIC_APPLICATION,
        authorities: [{ employeeId: env.SYNTHETIC_EMPLOYEE, credentialVersion: 1, employeeRevision: 1,
          workspaceRole: 'USER', isIdentityAdmin: false, emailVerified: true }] };
      const result = await env.IDENTITY_AUTHORITY.invalidateCache(input);
      if (JSON.stringify(Object.keys(result ?? {}).sort()) !== JSON.stringify(['applicationId', 'available', 'invalidated', 'workspaceId']) ||
          result.workspaceId !== input.workspaceId || result.applicationId !== input.applicationId || result.available !== false ||
          !Array.isArray(result.invalidated) || result.invalidated.length !== 1 || result.invalidated[0] !== env.SYNTHETIC_EMPLOYEE)
        throw new Error('PROVIDER_RPC_DENIAL_FAILED');
      const scopeConfigured = !!env.TARGET_WORKSPACE && !!env.TARGET_APPLICATION;
      let scopeAvailable = null;
      if (scopeConfigured) {
        const readiness = await env.IDENTITY_AUTHORITY.invalidateCache({workspaceId:env.TARGET_WORKSPACE, applicationId:env.TARGET_APPLICATION, authorities:[]});
        if (readiness.workspaceId !== env.TARGET_WORKSPACE || readiness.applicationId !== env.TARGET_APPLICATION ||
            typeof readiness.available !== 'boolean' || !Array.isArray(readiness.invalidated) || readiness.invalidated.length)
          throw new Error('PROVIDER_RPC_DENIAL_FAILED');
        scopeAvailable = readiness.available;
      }
      return Response.json({ ok: true, defaultFetcher: true, invalidSessionDenied: true, namedInvalidationRpc: true, scopeConfigured, scopeAvailable });
    } catch (error) {
      const code = ['PROVIDER_FETCHER_HEALTH_FAILED', 'PROVIDER_FETCHER_DENIAL_FAILED', 'PROVIDER_RPC_DENIAL_FAILED'].includes(error?.message)
        ? error.message : 'PRIVATE_BINDING_PROBE_FAILED';
      return Response.json({ ok: false, code }, { status: 503 });
    }
  },
};
