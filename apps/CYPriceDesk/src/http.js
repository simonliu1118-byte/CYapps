// Small helpers shared by the API modules.
export class ApiError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}

export function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: {'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store', 'x-content-type-options': 'nosniff'}
  });
}

export async function readBody(request) {
  const body = await request.json().catch(() => null);
  if (!body || typeof body !== 'object') throw new ApiError(400, '資料格式不正確');
  return body;
}
