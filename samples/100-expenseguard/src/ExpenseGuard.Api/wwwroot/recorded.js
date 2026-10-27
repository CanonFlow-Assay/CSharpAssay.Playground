export function safeLink(value) { try { const u = new URL(value); return u.protocol === 'https:' && !u.username && !u.password && !/[\u0000-\u001f\\<>]/.test(value); } catch { return false; } }
export async function hash(text) { return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text))), x => x.toString(16).padStart(2, '0')).join(''); }
export async function validateEntry(entry) {
  if (!entry || !/^[A-Za-z0-9-]{1,70}$/.test(entry.id) || typeof entry.payload !== 'string' || entry.payload.length > 100000 || !/^[a-f0-9]{64}$/.test(entry.sha256)) throw Error('Malformed fixture.');
  if (await hash(entry.payload) !== entry.sha256) throw Error('Fixture evidence digest mismatch.');
  const p = JSON.parse(entry.payload);
  if (p.id !== entry.id || !['rule', 'scenario'].includes(p.kind) || p.status !== 'Recorded example' || !p.observation || !p.provenance) throw Error('Incomplete recorded evidence.');
  if (p.kind === 'rule' && (typeof p.code !== 'string' || await hash(p.code) !== p.codeSha256)) throw Error('Input/evidence mismatch.');
  return p;
}
