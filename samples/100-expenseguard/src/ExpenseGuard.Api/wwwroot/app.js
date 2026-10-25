'use strict';
const el = id => document.getElementById(id);
let token = '', me = null, claims = [], current = null, editing = null, busy = false;
const uuid = /^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i;
const entry = location.pathname.match(/^\/claims\/([\da-f-]+)\/evidence$/i);
if (entry) el('claim').value = entry[1];
el('draftForm').elements.spentOn.value = new Date().toISOString().slice(0, 10);
el('draftForm').elements.spentOn.max = new Date().toISOString().slice(0, 10);
function status(message, failed = false) { el('status').textContent = message; el('status').classList.toggle('error', failed); }
async function request(path, method = 'GET', body, mode = 'json') {
  const headers = { 'X-ExpenseGuard-Key': token };
  if (body !== undefined && mode === 'json') headers['Content-Type'] = 'application/json';
  const response = await fetch('/api' + path, { method, headers, body: body === undefined ? undefined : mode === 'json' ? JSON.stringify(body) : body, cache: 'no-store' });
  if (!response.ok) {
    let code = '';
    try { code = (await response.json()).error ?? ''; } catch { /* Framework errors can have an empty response. */ }
    const friendly = { VersionConflict: 'This claim changed. Refresh its evidence before retrying.', PaymentAlreadyRequested: 'A reimbursement is already queued.', ReceiptIntegrityFailure: 'Stored receipt bytes differ from their recorded hash.', LedgerIntegrityFailure: 'Audit integrity failed. This operation is blocked.', InvalidReceiptFile: 'Upload a PDF, PNG or JPEG of at most 3 MiB.' };
    throw new Error(friendly[code] ?? (response.status === 401 ? 'Identity key is missing or invalid.' : response.status === 404 ? 'Claim or receipt is unavailable to this identity.' : response.status === 413 ? 'Receipt exceeds 3 MiB.' : code || 'Operation failed (' + response.status + ').'));
  }
  return mode === 'download' ? response.blob() : response.json();
}
async function work(action) {
  if (busy) return;
  busy = true; document.body.classList.add('busy'); el('status').setAttribute('aria-busy', 'true');
  try { await action(); } catch (error) { status(error.message || 'Operation failed.', true); }
  finally { busy = false; document.body.classList.remove('busy'); el('status').removeAttribute('aria-busy'); }
}
function view(name) { for (const key of ['claims', 'new', 'evidence']) el(key + 'View').hidden = key !== name; }
function node(tag, text, className = '') { const n = document.createElement(tag); n.textContent = text; n.className = className; return n; }
const money = c => Number(c.amount).toFixed(2) + ' ' + c.currency;
function renderClaims() {
  el('totalCount').textContent = claims.length;
  el('reviewCount').textContent = claims.filter(r => ['ManagerApprovalRequired', 'FinanceApprovalRequired'].includes(r.claim.state)).length;
  el('infoCount').textContent = claims.filter(r => r.claim.state === 'NeedsMoreInfo').length;
  el('approvedCount').textContent = claims.filter(r => ['Approved', 'Reimbursed'].includes(r.claim.state)).length;
  const filtered = claims.filter(r => el('filter').value === 'all' || r.claim.state === el('filter').value);
  el('empty').hidden = filtered.length > 0;
  el('claimList').replaceChildren(...filtered.map(row => {
    const c = row.claim, card = document.createElement('article'); card.className = 'claimRow';
    const description = document.createElement('div'); description.append(node('strong', c.merchant), node('p', c.employee + ' · ' + c.category + ' · ' + c.spentOn));
    const decision = document.createElement('div'); decision.append(node('strong', money(c)), node('p', row.hashChainValid ? c.state : 'AUDIT INTEGRITY FAILURE', 'badge'));
    const open = node('button', 'Inspect →', 'secondary'); open.addEventListener('click', () => work(() => inspect(c.id)));
    card.append(description, decision, open); return card;
  }));
}
async function refresh() { claims = await request('/claims'); renderClaims(); }
el('login').addEventListener('submit', event => { event.preventDefault(); work(async () => {
  token = el('key').value; current = null; me = null; el('workspace').hidden = true; el('result').hidden = true;
  el('raw').textContent = ''; el('trail').replaceChildren(); el('claimList').replaceChildren(); el('identity').textContent = '';
  try { me = await request('/me'); } catch (error) { token = ''; throw error; }
  el('key').value = ''; el('key').required = false;
  el('identity').textContent = me.employee + ' / ' + me.role;
  el('workspace').hidden = false; el('disconnect').hidden = false; el('newNav').hidden = me.role !== 'Employee';
  el('inboxTitle').textContent = me.role === 'Employee' ? 'Your claims' : me.role + ' review inbox';
  await refresh(); status('Connected as ' + me.employee + '. Server-configured role: ' + me.role + '.');
  if (uuid.test(el('claim').value)) await inspect(el('claim').value); else view('claims');
}); });
el('disconnect').addEventListener('click', () => {
  token = ''; me = null; claims = []; current = null; editing = null; el('key').value = ''; el('key').required = true; el('identity').textContent = '';
  el('workspace').hidden = true; el('result').hidden = true; el('disconnect').hidden = true; el('raw').textContent = ''; el('trail').replaceChildren();
  status('Disconnected. Identity key cleared from page memory.');
});
document.querySelectorAll('[data-view]').forEach(button => button.addEventListener('click', () => {
  if (button.dataset.view === 'new') { editing = null; el('formTitle').textContent = 'Create a draft'; el('saveDraft').textContent = 'Create draft →'; el('revisionNote').hidden = true; }
  view(button.dataset.view);
}));
el('refresh').addEventListener('click', () => work(async () => { await refresh(); if (current) await inspect(current.claim.id); else status('Claim list refreshed.'); }));
el('filter').addEventListener('change', renderClaims);
el('draftForm').addEventListener('submit', event => { event.preventDefault(); work(async () => {
  const form = event.target.elements;
  const body = { amount: Number(form.amount.value), currency: form.currency.value, category: form.category.value, merchant: form.merchant.value.trim(), spentOn: form.spentOn.value, units: Number(form.units.value) };
  const result = editing ? await request('/claims/' + editing.id, 'PUT', { ...body, expectedVersion: editing.version }) : await request('/claims', 'POST', body);
  editing = null; await refresh(); await inspect(result.claim.id); status('Draft saved. Attach evidence, then submit and assess.');
}); });
el('lookup').addEventListener('submit', event => { event.preventDefault(); work(() => inspect(el('claim').value.trim())); });
function action(label, handler, className = '') { const b = node('button', label, className); b.addEventListener('click', () => work(handler)); el('commandActions').append(b); }
async function command(name) {
  const body = { expectedVersion: current.version, action: name };
  if (name === 'reject' || name === 'hold') { const reason = prompt('Enter a concrete ' + (name === 'hold' ? 'audit hold' : 'rejection') + ' reason (max 300 characters):'); if (!reason?.trim()) return; body.reason = reason.trim().slice(0, 300); }
  await request('/claims/' + current.claim.id + '/commands', 'POST', body);
  await refresh(); await inspect(current.claim.id);
  status(name === 'reimburse' ? 'Simulated reimbursement queued. The worker must process it; no money moves.' : 'Command recorded with an audit receipt.');
}
async function inspect(id) {
  if (!uuid.test(id)) throw new Error('Enter a valid claim UUID.');
  if (!current || current.claim.id !== id) el('extractionResult').textContent = '';
  const data = await request('/claims/' + id + '/evidence'); current = data;
  el('claim').value = id; view('evidence'); el('result').hidden = false;
  const c = data.claim;
  el('state').textContent = c.state; el('claimOwner').textContent = c.employee + ' · ' + c.merchant + ' · ' + c.spentOn;
  el('amount').textContent = money(c) + ' · ' + c.category; el('authority').textContent = data.decisionAuthority;
  el('receiptStorage').textContent = 'Receipt content: ' + data.receiptStorage;
  el('verdict').textContent = data.csassay.verdict; el('scope').textContent = data.csassay.scope; el('proofDetail').textContent = data.csassay.detail;
  el('audit').textContent = data.audit.hashChainValid ? 'Chain verified' : 'INTEGRITY FAILURE';
  el('version').textContent = data.audit.receipts.length + ' accepted changes · version ' + data.version;
  if (data.paymentWork) el('version').textContent += ' · Payment: ' + data.paymentWork.status + ' · attempts ' + data.paymentWork.attempts;
  el('policy').textContent = JSON.stringify(c.policy ?? { status: 'Not assessed' }, null, 2);
  el('policySummary').replaceChildren(...(c.policy?.evidence ?? []).map(r => node('p', r.code + ': ' + r.detail)));
  if (!c.policy) el('policySummary').textContent = 'Submit this draft and run assessment to capture policy evidence.';
  el('advice').textContent = data.aiAdvice.proposedState + ': ' + data.aiAdvice.summary;
  el('trail').replaceChildren(...data.audit.receipts.map(r => node('li', r.sequence + '. ' + r.event + ' · ' + r.actor + ' (' + r.role + ') · ' + r.at + ' · ' + r.hash.slice(0, 12) + '…')));
  el('raw').textContent = JSON.stringify(data, null, 2); el('commandActions').replaceChildren();
  const owner = me.role === 'Employee' && me.employee === c.employee, editable = owner && ['Draft', 'NeedsMoreInfo'].includes(c.state);
  el('extractionCard').hidden = !owner || !c.receipt?.reference?.startsWith('blob:');
  el('extractionMode').textContent = me.receiptExtraction; el('extract').disabled = !me.extractionEnabled || data.receiptStorage !== 'VerifiedHash';
  el('attachCard').hidden = !editable || !data.audit.hashChainValid; el('downloadReceipt').hidden = !c.receipt?.reference?.startsWith('blob:');
  if (data.audit.hashChainValid && !['Missing', 'Invalid'].includes(data.receiptStorage)) {
    if (editable) {
      action('Submit', () => command('submit'));
      action('Edit draft', async () => { editing = { id: c.id, version: data.version }; const form = el('draftForm').elements;
        for (const key of ['amount', 'currency', 'category', 'merchant', 'spentOn', 'units']) form[key].value = c[key];
        el('formTitle').textContent = 'Revise draft'; el('saveDraft').textContent = 'Save revision →'; el('revisionNote').hidden = false; view('new');
      }, 'secondary');
    }
    if (owner && c.state === 'Submitted') action('Run policy assessment', async () => {
      await request('/claims/' + c.id + '/assess', 'POST', { expectedVersion: data.version }); await refresh(); await inspect(c.id); status('Deterministic policy assessment recorded.');
    });
    if ((me.role === 'Manager' && c.state === 'ManagerApprovalRequired') || (me.role === 'Finance' && c.state === 'FinanceApprovalRequired')) {
      if (me.employee !== c.employee && me.employee !== (c.state === 'FinanceApprovalRequired' ? c.stateDetail : '')) {
        action('Approve', () => command('approve')); action('Reject with reason', () => command('reject'), 'secondary danger');
      }
    }
    if (me.role === 'Finance' && c.state === 'Approved' && !data.audit.receipts.some(r => r.event === 'ReimbursementRequested')) action('Queue simulated reimbursement', () => command('reimburse'));
  }
  if (data.audit.hashChainValid && me.role === 'Auditor' && !['Draft', 'Reimbursed', 'AuditHeld'].includes(c.state)) action('Place audit hold', () => command('hold'), 'secondary danger');
  const hints = { Draft: 'Attach a receipt if required, then submit. Revising clears prior receipt and assessment.', Submitted: 'The claimant can now run policy assessment.', NeedsMoreInfo: 'Missing evidence: ' + c.stateDetail + '. Attach/confirm evidence and resubmit.', ManagerApprovalRequired: c.route === 'ManagerThenFinance' ? 'Manager review first, then a distinct finance reviewer. Self-approval is blocked.' : 'A manager other than the claimant must review.', FinanceApprovalRequired: 'A finance reviewer distinct from the claimant and approving manager must decide.', Approved: 'Finance can queue a simulated reimbursement. The worker checks approval again before recording payment.', Reimbursed: 'Simulated payment recorded. No real money moved.', Rejected: 'Rejected: ' + c.stateDetail + '. This terminal claim cannot be silently reopened.', AuditHeld: 'Audit hold: ' + c.stateDetail + '. Release and investigation are outside this version.' };
  el('nextStep').textContent = !data.audit.hashChainValid ? 'Audit integrity failed. State changes and payment processing are blocked.' :
    data.paymentWork?.status === 'ManualReview' ? 'Payment requires manual review: ' + data.paymentWork.lastErrorCode + '. No payment success is being claimed. Ask finance/operations to investigate before requeueing.' :
    ['Missing', 'Invalid'].includes(data.receiptStorage) ? 'Stored receipt content is unavailable or changed. Approval/payment is blocked. Replace it while editable, or ask an auditor to hold the claim.' : hints[c.state] ?? '';
  status(data.paymentMode); history.replaceState(null, '', '/claims/' + id + '/evidence');
}
el('uploadForm').addEventListener('submit', event => { event.preventDefault(); work(async () => {
  const file = el('receiptFile').files[0]; if (!file) throw new Error('Choose a receipt file.'); if (file.size > 3 * 1024 * 1024) throw new Error('Receipt exceeds 3 MiB.');
  const id = current.claim.id;
  await request('/claims/' + id + '/receipt-file?expectedVersion=' + current.version + '&itemized=' + el('itemized').checked, 'POST', file, 'upload');
  el('receiptFile').value = ''; await refresh(); await inspect(id); status('Receipt stored privately; the server computed its hash.');
}); });
el('referenceForm').addEventListener('submit', event => { event.preventDefault(); work(async () => {
  const id = current.claim.id; await request('/claims/' + id + '/receipt', 'POST', { expectedVersion: current.version, reference: el('reference').value.trim(), sha256: el('receiptHash').value.trim(), itemized: el('itemized').checked });
  await refresh(); await inspect(id); status('External assertion attached. Automatic approval requires a stored upload and reassessment; external bytes were not fetched or verified.');
}); });
el('extractionForm').addEventListener('submit', event => { event.preventDefault(); work(async () => {
  const id = current.claim.id;
  const result = await request('/claims/' + id + '/receipt-extraction', 'POST', { expectedVersion: current.version, confirmExternalDisclosure: el('disclosure').checked });
  await refresh(); await inspect(id);
  el('extractionResult').textContent = JSON.stringify(result.proposal, null, 2);
  el('disclosure').checked = false;
  status('Receipt proposal only. Review every field; use Edit draft for any confirmed correction. No AI result changed the claim.');
}); });
function download(blob, name) { const url = URL.createObjectURL(blob), link = document.createElement('a'); link.href = url; link.download = name; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
el('export').addEventListener('click', () => { if (current) download(new Blob([JSON.stringify(current, null, 2)], { type: 'application/json' }), 'expenseguard-' + current.claim.id + '-v' + current.version + '.json'); });
el('downloadReceipt').addEventListener('click', () => work(async () => {
  const blob = await request('/claims/' + current.claim.id + '/receipt-file', 'GET', undefined, 'download');
  download(blob, 'receipt.' + (blob.type === 'application/pdf' ? 'pdf' : blob.type === 'image/png' ? 'png' : 'jpg'));
}));
