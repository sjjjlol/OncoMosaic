export async function api<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch('/api' + path, options);
  if (!response.ok) { const error = await response.json().catch(() => ({message: '服务暂时不可用'})); throw new Error(error.message || `请求失败 (${response.status})`); }
  return response.json();
}
export function post<T>(path: string, input?: unknown) { return api<T>(path, { method: 'POST', headers: {'Content-Type': 'application/json'}, body: input === undefined ? undefined : JSON.stringify(input) }); }
