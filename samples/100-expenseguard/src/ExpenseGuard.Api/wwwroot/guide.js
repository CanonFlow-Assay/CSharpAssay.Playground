import { validateEntry } from './recorded.js';
const get = id => document.getElementById(id); let entries = new Map(), revision = 0;
async function show() {
  const requested = ++revision;
  get('guidedResult').textContent = ''; get('guidedStatus').textContent = 'Validating recorded evidence…';
  try { const entry = entries.get(get('guidedCase').value); if (!entry) throw Error('Unknown recorded scenario.'); const p = await validateEntry(entry); if (requested !== revision) return; get('guidedResult').textContent = JSON.stringify(p, null, 2); get('guidedStatus').textContent = 'Recorded example. Observe the named detector and limits. No claim was changed.'; }
  catch (e) { if (requested !== revision) return; get('guidedStatus').textContent = 'Evidence unavailable: ' + e.message; }
}
get('openGuide').addEventListener('click', async () => {
  const opening = ++revision;
  get('guided').hidden = false;
  get('guidedResult').textContent = ''; get('guidedStatus').textContent = 'Loading recorded evidence…'; get('guidedCase').disabled = true;
  try {
    const response = await fetch('/guide-examples.json', { cache: 'no-store' }); if (!response.ok) throw Error('Recorded scenarios missing.');
    const text = await response.text(); if (text.length > 500000) throw Error('Evidence exceeds the guide budget.'); const pack = JSON.parse(text);
    if (pack.schema !== 'csassay-recorded-examples/0.1' || !Array.isArray(pack.entries)) throw Error('Invalid recorded evidence.');
    const loaded = new Map(); for (const entry of pack.entries) { const p = await validateEntry(entry); if (p.kind === 'scenario' && /^D0[1-6]$/.test(p.id)) { if (loaded.has(p.id)) throw Error('Duplicate scenario.'); loaded.set(p.id, entry); } }
    if (loaded.size !== 6) throw Error('The six executed scenarios have not been staged.');
    if (opening !== revision) return;
    entries = loaded;
    get('guidedCase').replaceChildren(); for (const [id, entry] of [...entries].sort()) { const p = JSON.parse(entry.payload), option = document.createElement('option'); option.value = id; option.textContent = id + ' · ' + p.title; get('guidedCase').append(option); }
    get('guidedCase').disabled = false; get('guidedCase').focus(); await show();
  } catch (e) { if (opening !== revision) return; get('guidedResult').textContent = ''; get('guidedCase').disabled = true; get('guidedStatus').textContent = 'Evidence unavailable: ' + e.message; }
});
get('guidedCase').addEventListener('change', show);
get('closeGuide').addEventListener('click', () => { ++revision; get('guidedResult').textContent = ''; get('guided').hidden = true; get('openGuide').focus(); });
if (new URL(location.href).searchParams.get('demo') === 'recorded') get('openGuide').click();
