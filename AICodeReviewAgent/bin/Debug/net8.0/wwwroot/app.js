/**
 * AI Code Review Agent – app.js
 *
 * Plain ES2020 JavaScript, no frameworks or build tools required.
 * Every function is documented so a beginner can follow along.
 */

// ─────────────────────────────────────────────────────────────
//  DOM references  (grab once, reuse everywhere)
// ─────────────────────────────────────────────────────────────
const $ = id => document.getElementById(id);

const tabs              = document.querySelectorAll('.tab');
const tabPanes          = document.querySelectorAll('.tab-pane');
const diffInput         = $('diff-input');
const fileInput         = $('file-input');
const dropzone          = $('dropzone');
const fileNameLabel     = $('file-name');
const repoContext       = $('repo-context');
const btnReview         = $('btn-review');
const btnLoadSample     = $('btn-load-sample');
const btnClear          = $('btn-clear');
const reviewStatus      = $('review-status');
const resultsSection    = $('results-section');
const summaryBadges     = $('summary-badges');
const summaryText       = $('summary-text');
const severityFilters   = $('severity-filters');
const categoryFilters   = $('category-filters');
const btnClearFilters   = $('btn-clear-filters');
const findingsList      = $('findings-list');
const noFindings        = $('no-findings');
const metaToolCalls     = $('meta-tool-calls');
const metaReviewedAt    = $('meta-reviewed-at');
const errorBanner       = $('error-banner');
const errorMessage      = $('error-message');
const btnDismissError   = $('btn-dismiss-error');
const loadingOverlay    = $('loading-overlay');

// ─────────────────────────────────────────────────────────────
//  State
// ─────────────────────────────────────────────────────────────

/** All findings returned by the last review (raw from API). */
let allFindings = [];

/** Which severity / category chips are currently active. */
let activeSeverities  = new Set();
let activeCategories  = new Set();

// ─────────────────────────────────────────────────────────────
//  Tab switching
// ─────────────────────────────────────────────────────────────

tabs.forEach(tab => {
  tab.addEventListener('click', () => {
    // Deactivate all tabs and panes.
    tabs.forEach(t => t.classList.remove('active'));
    tabPanes.forEach(p => p.classList.remove('active'));

    // Activate the clicked tab and its matching pane.
    tab.classList.add('active');
    const pane = document.getElementById('tab-' + tab.dataset.tab);
    if (pane) pane.classList.add('active');
  });
});

// ─────────────────────────────────────────────────────────────
//  File upload – dropzone + input
// ─────────────────────────────────────────────────────────────

dropzone.addEventListener('click', () => fileInput.click());

fileInput.addEventListener('change', () => {
  const file = fileInput.files[0];
  if (file) showSelectedFile(file);
});

// Drag-and-drop support.
dropzone.addEventListener('dragover', e => {
  e.preventDefault();
  dropzone.classList.add('drag-over');
});
dropzone.addEventListener('dragleave', () => dropzone.classList.remove('drag-over'));
dropzone.addEventListener('drop', e => {
  e.preventDefault();
  dropzone.classList.remove('drag-over');
  const file = e.dataTransfer.files[0];
  if (file) showSelectedFile(file);
});

/** Reads the dropped / selected file and shows its name. */
function showSelectedFile(file) {
  fileNameLabel.textContent = `✓ Selected: ${file.name} (${formatBytes(file.size)})`;
}

// ─────────────────────────────────────────────────────────────
//  Load sample diff
// ─────────────────────────────────────────────────────────────

btnLoadSample.addEventListener('click', async () => {
  btnLoadSample.disabled = true;
  btnLoadSample.textContent = '⏳ Loading…';
  try {
    const res  = await fetch('/api/review/sample');
    const data = await res.json();
    diffInput.value = data.diffContent;

    // Switch to the paste tab so the user can see it.
    document.querySelector('[data-tab="paste"]').click();
    reviewStatus.textContent = 'Sample diff loaded. Click "Run Code Review" to analyse it.';
  } catch (err) {
    showError('Could not load sample diff: ' + err.message);
  } finally {
    btnLoadSample.disabled = false;
    btnLoadSample.textContent = '📄 Load Sample Diff';
  }
});

// ─────────────────────────────────────────────────────────────
//  Clear textarea
// ─────────────────────────────────────────────────────────────

btnClear.addEventListener('click', () => {
  diffInput.value = '';
  reviewStatus.textContent = '';
});

// ─────────────────────────────────────────────────────────────
//  Run review
// ─────────────────────────────────────────────────────────────

btnReview.addEventListener('click', runReview);

async function runReview() {
  hideError();

  // Decide whether we're using the paste tab or the upload tab.
  const activeTab = document.querySelector('.tab.active').dataset.tab;

  if (activeTab === 'paste') {
    await runReviewFromText();
  } else {
    await runReviewFromFile();
  }
}

/** POST the pasted diff text as JSON. */
async function runReviewFromText() {
  const diff = diffInput.value.trim();
  if (!diff) {
    showError('Please paste a Git diff before running the review.');
    return;
  }

  setLoading(true);

  try {
    const res = await fetch('/api/review', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({
        diffContent:       diff,
        repositoryContext: repoContext.value.trim() || null
      })
    });

    await handleReviewResponse(res);
  } catch (err) {
    showError('Network error: ' + err.message);
  } finally {
    setLoading(false);
  }
}

/** POST the uploaded file as multipart/form-data. */
async function runReviewFromFile() {
  const file = fileInput.files[0];
  if (!file) {
    showError('Please select a .patch or .diff file first.');
    return;
  }

  setLoading(true);

  try {
    const formData = new FormData();
    formData.append('file', file);
    const ctx = repoContext.value.trim();
    if (ctx) formData.append('repositoryContext', ctx);

    const res = await fetch('/api/review/upload', {
      method: 'POST',
      body:   formData
      // Note: do NOT set Content-Type when using FormData;
      // the browser sets it automatically with the boundary.
    });

    await handleReviewResponse(res);
  } catch (err) {
    showError('Network error: ' + err.message);
  } finally {
    setLoading(false);
  }
}

/** Reads the API response and renders results or an error. */
async function handleReviewResponse(res) {
  const data = await res.json();

  if (!res.ok) {
    showError(data.error || `Server error ${res.status}`);
    return;
  }

  renderResults(data);
}

// ─────────────────────────────────────────────────────────────
//  Render results
// ─────────────────────────────────────────────────────────────

/**
 * Takes the ReviewResponse object from the API and builds the
 * results UI.
 */
function renderResults(data) {
  allFindings = data.findings || [];

  // Count by severity for the badge row.
  const high   = allFindings.filter(f => f.severity === 'High').length;
  const medium = allFindings.filter(f => f.severity === 'Medium').length;
  const low    = allFindings.filter(f => f.severity === 'Low').length;

  // Badges.
  summaryBadges.innerHTML = `
    <span class="badge badge-high">🔴 ${high} High</span>
    <span class="badge badge-medium">🟡 ${medium} Medium</span>
    <span class="badge badge-low">🔵 ${low} Low</span>
  `;

  summaryText.textContent = data.summary;

  // Build filter chips.
  buildFilters();

  // Render the findings.
  applyFilters();

  // Footer meta.
  metaToolCalls.textContent  = `🛠 Tool calls: ${data.toolCallsExecuted}`;
  metaReviewedAt.textContent = `🕐 ${new Date(data.reviewedAt).toLocaleString()}`;

  // Show the results section.
  resultsSection.classList.remove('hidden');
  resultsSection.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

// ─────────────────────────────────────────────────────────────
//  Filter chips
// ─────────────────────────────────────────────────────────────

function buildFilters() {
  activeSeverities.clear();
  activeCategories.clear();

  const severities = [...new Set(allFindings.map(f => f.severity))].sort();
  const categories = [...new Set(allFindings.map(f => f.category))].sort();

  severityFilters.innerHTML = severities.map(s =>
    `<button class="filter-chip" data-type="severity" data-value="${s}">${s}</button>`
  ).join('');

  categoryFilters.innerHTML = categories.map(c =>
    `<button class="filter-chip" data-type="category" data-value="${c}">${c}</button>`
  ).join('');

  // Wire chip click events.
  document.querySelectorAll('.filter-chip').forEach(chip => {
    chip.addEventListener('click', () => {
      const { type, value } = chip.dataset;
      const set = type === 'severity' ? activeSeverities : activeCategories;

      if (set.has(value)) {
        set.delete(value);
        chip.classList.remove('active');
      } else {
        set.add(value);
        chip.classList.add('active');
      }
      applyFilters();
    });
  });
}

btnClearFilters.addEventListener('click', () => {
  activeSeverities.clear();
  activeCategories.clear();
  document.querySelectorAll('.filter-chip').forEach(c => c.classList.remove('active'));
  applyFilters();
});

/** Re-renders the visible findings according to active filter chips. */
function applyFilters() {
  const visible = allFindings.filter(f => {
    const severityOk  = activeSeverities.size === 0 || activeSeverities.has(f.severity);
    const categoryOk  = activeCategories.size === 0 || activeCategories.has(f.category);
    return severityOk && categoryOk;
  });

  findingsList.innerHTML = visible.map(renderFindingCard).join('');
  noFindings.classList.toggle('hidden', visible.length > 0);
}

// ─────────────────────────────────────────────────────────────
//  Finding card HTML
// ─────────────────────────────────────────────────────────────

function renderFindingCard(finding) {
  const sev  = (finding.severity  || 'low').toLowerCase();
  const conf = (finding.confidence || 'potential').toLowerCase();
  const line = finding.lineNumber > 0 ? `line ${finding.lineNumber}` : '';

  return `
    <div class="finding-card finding-${sev}">
      <div class="finding-header">
        <span class="severity-pill pill-${sev}">${finding.severity}</span>
        <span class="category-tag">${finding.category}</span>
        ${finding.fileName ? `<span class="finding-file">${escapeHtml(finding.fileName)}</span>` : ''}
        ${line ? `<span class="finding-line">${line}</span>` : ''}
        <span class="confidence-tag confidence-${conf}">${finding.confidence}</span>
      </div>
      <div class="finding-body">
        <p class="explanation">${escapeHtml(finding.explanation)}</p>
        ${finding.suggestedFix ? `
          <p class="suggested-fix-label">💡 Suggested fix</p>
          <pre class="suggested-fix">${escapeHtml(finding.suggestedFix)}</pre>
        ` : ''}
      </div>
    </div>
  `;
}

// ─────────────────────────────────────────────────────────────
//  UI helpers
// ─────────────────────────────────────────────────────────────

function setLoading(isLoading) {
  btnReview.disabled = isLoading;
  loadingOverlay.classList.toggle('hidden', !isLoading);
  reviewStatus.textContent = isLoading ? 'Analysing…' : '';
}

function showError(message) {
  errorMessage.textContent = message;
  errorBanner.classList.remove('hidden');
}

function hideError() {
  errorBanner.classList.add('hidden');
  errorMessage.textContent = '';
}

btnDismissError.addEventListener('click', hideError);

/** Prevents XSS by escaping user-controlled content before inserting it as HTML. */
function escapeHtml(str) {
  if (!str) return '';
  return str
    .replace(/&/g,  '&amp;')
    .replace(/</g,  '&lt;')
    .replace(/>/g,  '&gt;')
    .replace(/"/g,  '&quot;')
    .replace(/'/g,  '&#39;');
}

function formatBytes(bytes) {
  if (bytes < 1024)        return bytes + ' B';
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
  return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
}

