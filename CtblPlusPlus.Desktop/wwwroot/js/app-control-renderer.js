// App Control tab rendering: registry table, selection state, bulk bar, toggle/preview state.
import { sendCommand } from './ipc.js';
import { showToast } from './utils.js';

let selectedAppPaths = new Set();
let currentAppsArray = [];

// Last-used filter state, owned here so IPC polling calls automatically re-apply
// the active filter when fresh data arrives — without needing to import from
// app-control-interactions.js (which would create a circular dependency).
let lastStatusFilter = null;
let lastFieldFilter = null;

// Getters used by app-control-interactions.js to avoid cross-module state coupling
export function getCurrentAppsArray() { return currentAppsArray; }
export function getSelectedAppPaths() { return selectedAppPaths; }

export function handleAppControlState(data) {
    const toggleSwitch = document.getElementById('app-control-toggle-switch');
    if (!toggleSwitch) return;
    if (data.enabled) {
        toggleSwitch.textContent = 'ON';
        toggleSwitch.classList.add('active');
    } else {
        toggleSwitch.textContent = 'OFF';
        toggleSwitch.classList.remove('active');
    }
}

export function handleAppSelected(data) {
    const pathInput = document.getElementById('app-control-app-path');
    const preview = document.getElementById('app-control-app-preview');
    const nameEl = document.getElementById('app-control-app-name');
    const publisherEl = document.getElementById('app-control-app-publisher');
    const badgeEl = document.getElementById('app-control-app-badge');
    const queueBtn = document.getElementById('app-control-queue-btn');

    if (pathInput) {
        pathInput.value = data.path;
        pathInput.dataset.path = data.path;
    }

    if (nameEl) nameEl.textContent = data.appName || 'Unknown Application';
    if (publisherEl) publisherEl.textContent = data.publisher || '(No publisher info)';

    if (badgeEl) {
        if (data.isSigned) {
            badgeEl.textContent = '🔒 Signed';
            badgeEl.className = 'ds-state-badge ds-semantic-success';
        } else {
            badgeEl.textContent = '⚠️ Unsigned';
            badgeEl.className = 'ds-state-badge ds-semantic-warning';
        }
    }

    if (preview) preview.style.display = 'block';
    if (queueBtn) queueBtn.style.display = 'block';
}

// Filter params default to lastStatusFilter/lastFieldFilter so that IPC polling
// calls — renderAppRegistry(freshData) with no filter args — automatically
// re-apply whatever filter was last set by the user, instead of resetting to
// the unfiltered view. Explicit null,null from resetAllFilters() still clears
// them correctly because the defaults are re-evaluated on every call.
export function renderAppRegistry(appsArray, statusFilter = lastStatusFilter, fieldFilter = lastFieldFilter) {
    lastStatusFilter = statusFilter;
    lastFieldFilter = fieldFilter;

    const tbody = document.getElementById('app-control-rules-body');
    const searchInput = document.getElementById('app-search');
    const selectAllCheckbox = document.getElementById('app-select-all');
    if (!tbody) return;

    currentAppsArray = appsArray || [];
    const filter = (searchInput?.value || '').toLowerCase();
    
    tbody.innerHTML = '';

    if (!currentAppsArray || currentAppsArray.length === 0) {
        tbody.innerHTML = '<tr><td colspan="5" class="empty-table-state">Scanning for applications...</td></tr>';
        return;
    }

    // Deduplicate by ExePath, keep first seen
    const seen = new Set();
    const dedupApps = [];
    currentAppsArray.forEach(a => {
        const key = (a.ExePath || a.exePath || '').toLowerCase();
        if (!seen.has(key)) {
            seen.add(key);
            dedupApps.push(a);
        }
    });

    // Filter — uses parameters instead of module-level vars
    const filteredApps = dedupApps.filter(app => {
        const name = (app.DisplayName || app.displayName || '').toLowerCase();
        const path = (app.ExePath || app.exePath || '').toLowerCase();
        
        let matchField = false;
        if (fieldFilter === 'Name') matchField = name.includes(filter);
        else if (fieldFilter === 'Path') matchField = path.includes(filter);
        else if (fieldFilter === 'Publisher') {
             const pub = (app.Publisher || app.publisher || '').toLowerCase();
             matchField = pub.includes(filter);
        } else {
             matchField = name.includes(filter) || path.includes(filter);
        }

        let matchStatus = true;
        if (statusFilter) {
             const status = app.Status || app.status || 'Detected';
             matchStatus = (status === statusFilter);
        }

        return matchField && matchStatus;
    });

    if (filteredApps.length === 0) {
        tbody.innerHTML = '<tr><td colspan="5" class="empty-table-state">No applications match your search.</td></tr>';
        return;
    }

    filteredApps.forEach(app => {
        const tr = document.createElement('tr');
        const appId = app.Id || app.id || '';
        const displayName = app.DisplayName || app.displayName || 'Unknown';
        const exePath = app.ExePath || app.exePath || '';
        const publisher = app.Publisher || app.publisher || '—';
        const status = app.Status || app.status || 'Detected';
        // IMPORTANT: safeAppPath doubles backslashes for use ONLY in onclick="..." inline JS strings,
        // where the path must survive the HTML attribute → JS parser boundary (e.g. C:\\\\foo → C:\\foo in JS).
        // Do NOT use safeAppPath for data-* attributes — getAttribute() returns the literal HTML value,
        // so doubled backslashes would be sent to C# as-is, corrupting the file path.
        const safeAppPath = exePath.replace(/\\/g, '\\\\');

        // Status badge
        let statusClass = 'ds-state-badge ds-semantic-success';
        let statusLabel = 'Allowed';
        let rowStyle = '';
        let actionButtonHtml = `
            <button onclick="revokeAppControlRule('${appId}')" class="btn btn-ghost" style="color: #ef4444; padding: 4px;" title="Revoke Access">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <circle cx="12" cy="12" r="10"></circle>
                    <line x1="15" y1="9" x2="9" y2="15"></line>
                    <line x1="9" y1="9" x2="15" y2="15"></line>
                </svg>
            </button>`;

        if (status === 'Detected') {
            statusClass = 'ds-state-badge ds-semantic-info';
            statusLabel = 'Detected';
            actionButtonHtml = `
                <button onclick="allowDetectedApp('${safeAppPath}')" class="btn btn-primary btn-nav" title="Request App Access">
                    Allow
                </button>`;
        } else if (status === 'Blocked') {
            statusClass = 'ds-state-badge ds-semantic-error';
            statusLabel = 'Blocked';
            rowStyle = 'background-color: rgba(239, 68, 68, 0.04);';
            actionButtonHtml = `
                <button onclick="allowDetectedApp('${safeAppPath}')" class="btn btn-primary btn-nav" title="Request App Access">
                    Allow
                </button>`;
        }

        const shortPath = exePath.length > 40 ? '...' + exePath.slice(-37) : exePath;

        tr.style.cssText = rowStyle;
        tr.innerHTML = `
            <td class="col-check">
                <!-- IMPORTANT: data-path must use exePath (single backslashes), NOT safeAppPath.
                     getAttribute('data-path') returns the literal attribute value — if safeAppPath
                     were used here, C# would receive doubled backslashes that don't match any real path. -->
                <input type="checkbox" class="app-row-checkbox" data-path="${exePath}" ${selectedAppPaths.has(exePath) ? 'checked' : ''} />
            </td>
            <td>
                <div style="font-weight: 600;">${displayName}</div>
                <div style="font-size: 12px; color: #a1a1aa; margin-top: 2px;" title="${exePath}">${shortPath}</div>
            </td>
            <td style="font-size: 14px; color: #a1a1aa;">${publisher}</td>
            <td style="text-align: center;"><span class="${statusClass}">${statusLabel}</span></td>
            <td style="text-align: center;">${actionButtonHtml}</td>
        `;

        // Selection listener
        const checkbox = tr.querySelector('.app-row-checkbox');
        checkbox.addEventListener('change', (e) => {
            const path = e.target.getAttribute('data-path');
            if (e.target.checked) {
                selectedAppPaths.add(path);
            } else {
                selectedAppPaths.delete(path);
                if (selectAllCheckbox) selectAllCheckbox.checked = false;
            }
            updateBulkActionBar();
        });

        tbody.appendChild(tr);
    });

    updateBulkActionBar();
}

function updateBulkActionBar() {
    const bar = document.getElementById('bulk-action-bar');
    const countEl = document.getElementById('bulk-selection-count');
    if (!bar || !countEl) return;

    const count = selectedAppPaths.size;
    if (count > 0) {
        countEl.textContent = `${count} application${count > 1 ? 's' : ''} selected`;
        bar.style.display = 'flex';
    } else {
        bar.style.display = 'none';
    }
}
