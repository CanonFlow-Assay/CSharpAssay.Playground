import { validateEntry, safeLink } from './recorded.js';
const el = id => document.getElementById(id);
let current = null, revision = 0, scenarioRevision = 0;
const rules = new Map(), scenarios = new Map();
function status(text) { el('fixtureStatus').textContent = text; }
function invalidate() { el('export').disabled = true; el('observation').textContent = ''; el('findingSummary').textContent = ''; status('Unverified edit — no checks ran. Reset to inspect the recorded example or reproduce locally.'); }
async function selectRule() {
  const requested = ++revision; current = null; el('export').disabled = true; el('observation').textContent = ''; el('findingSummary').textContent = ''; el('code').disabled = true;
  try {
    const entry = rules.get(el('case').value); if (!entry) throw Error('Unknown fixture ID. No recorded evidence is associated with this selection.');
    const payload = await validateEntry(entry); if (requested !== revision) return;
    current = { entry, payload }; el('code').value = payload.code; el('code').disabled = false; el('reset').disabled = false;
    el('ruleTitle').textContent = payload.title; el('ruleMeta').textContent = payload.ruleId + ' · ' + payload.ruleStatus + ' · ' + payload.boundary;
    el('guide').textContent = payload.guide; el('provenance').textContent = JSON.stringify(payload.provenance, null, 2);
    el('observation').textContent = JSON.stringify(payload.observation, null, 2);
    const recorded = payload.observation;
    el('findingSummary').textContent = [recorded.headline || recorded.detector, ...(recorded.findings || []).map(f => f.ruleId + ': ' + f.message + ' (' + f.scope + ', ' + f.effectiveLevel + ')')].filter(Boolean).join('\n');
    const docs = el('ruleDocs'); docs.hidden = !safeLink(payload.documentation); if (!docs.hidden) { docs.href = payload.documentation; docs.rel = 'noreferrer noopener'; }
    status('Recorded example — input and evidence digest validated. This is not a live scan.'); el('export').disabled = false;
    const url = new URL(location.href); url.search = ''; url.searchParams.set('case', entry.id); history.replaceState(null, '', url);
  } catch (error) { status('Evidence unavailable: ' + error.message); }
}
async function selectScenario() {
  const requested = ++scenarioRevision;
  el('scenarioResult').textContent = ''; el('scenarioStatus').textContent = 'Validating recorded scenario…';
  try { const entry = scenarios.get(el('scenario').value); if (!entry) throw Error('Unknown scenario.'); const p = await validateEntry(entry); if (requested !== scenarioRevision) return; el('scenarioResult').textContent = JSON.stringify(p, null, 2); el('scenarioStatus').textContent = 'Recorded scenario. Simulated advice and payments; not a current claim or live model run.'; }
  catch (error) { el('scenarioStatus').textContent = 'Evidence unavailable: ' + error.message; }
}
el('case').addEventListener('change', selectRule); el('scenario').addEventListener('change', selectScenario);
el('code').addEventListener('input', invalidate); el('reset').addEventListener('click', selectRule);
el('export').addEventListener('click', async () => {
  el('export').disabled = true;
  try {
    const selected = current; if (!selected) throw Error('No validated recorded input.'); const p = await validateEntry(selected.entry);
    if (current !== selected || el('code').value !== p.code) throw Error('Edited or changed input is Unverified. Reset before exporting recorded evidence.');
    const blob = new Blob([JSON.stringify({ kind: 'Recorded example export', entry: selected.entry }, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob); const a = document.createElement('a'); a.href = url; a.download = p.id + '.json'; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    status('Recorded example exported. It does not verify edited source.'); el('export').disabled = false;
  } catch (error) { status('Export failed: ' + error.message); }
});
el('copyCommands').addEventListener('click', async () => { try { await navigator.clipboard.writeText(el('commands').textContent); el('copyStatus').textContent = 'Commands copied. Follow the prerequisite guide first.'; } catch { el('copyStatus').textContent = 'Copy unavailable. Select the command text and copy it manually.'; } });
try {
  const response = await fetch('examples/catalogue.json', { cache: 'no-store' }); if (!response.ok) throw Error('Fixture file could not be loaded (HTTP ' + response.status + ').');
  const text = await response.text(); if (text.length > 1500000) throw Error('Fixture file exceeds the public example budget.');
  const pack = JSON.parse(text); if (pack.schema !== 'csassay-recorded-examples/0.1' || !Array.isArray(pack.entries) || pack.entries.length < 1 || pack.entries.length > 32) throw Error('Recorded fixtures are missing or invalid.');
  for (const entry of pack.entries) {
    const p = await validateEntry(entry), map = p.kind === 'rule' ? rules : scenarios; if (map.has(entry.id)) throw Error('Duplicate fixture ID.'); map.set(entry.id, entry);
  }
  for (const [id, map] of [['case', rules], ['scenario', scenarios]]) { el(id).replaceChildren(); for (const [key, entry] of map) { const p = JSON.parse(entry.payload), option = document.createElement('option'); option.value = key; option.textContent = p.title + ' · ' + (p.variant || key); el(id).append(option); } el(id).disabled = map.size === 0; }
  const requested = new URL(location.href).searchParams.get('case');
  if (requested && !rules.has(requested)) { el('case').selectedIndex = -1; status('Unknown fixture ID. Choose a recorded case; no evidence was inherited.'); } else { if (requested) el('case').value = requested; await selectRule(); }
  await selectScenario();
} catch (error) { status('Evidence unavailable: ' + error.message); el('scenarioStatus').textContent = 'Recorded scenarios unavailable. Use the local reproduction guide.'; }
