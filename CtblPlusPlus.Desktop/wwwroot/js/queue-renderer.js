// Queue tab rendering: blocks dropdown, removal dropdown, queue table.
import { formatCSharpDate } from './utils.js';

export let globalBlocksData = {};

export function populateRemovalDropdown(blockName) {
    const removeList = document.getElementById('detail-remove-list');
    if (!removeList) return;
    removeList.innerHTML = '';
    
    if (globalBlocksData[blockName] && globalBlocksData[blockName].web && globalBlocksData[blockName].web.length > 0) {
        globalBlocksData[blockName].web.forEach((url) => {
            const option = document.createElement('option');
            option.value = url;
            option.textContent = url;
            removeList.appendChild(option);
        });
    } else {
        const option = document.createElement('option');
        option.textContent = '-- No blocked websites --';
        option.disabled = true;
        removeList.appendChild(option);
    }
}

export function renderBlocksDropdown(blocksDict) {
    globalBlocksData = blocksDict;
    const select = document.getElementById('action-block');
    if (!select) return;
    select.innerHTML = '<option value="">Select a block</option>';

    const blockNames = Object.keys(blocksDict);
    if (blockNames.length === 0) {
        select.innerHTML = '<option value="">No blocks found</option>';
        return;
    }

    blockNames.forEach(name => {
        const option = document.createElement('option');
        option.value = name;
        option.textContent = name;
        select.appendChild(option);
    });
}

export function renderQueue(queueArray) {
    const tbody = document.getElementById('queue-body');
    if (!tbody) return;
    tbody.innerHTML = '';

    if (!queueArray || queueArray.length === 0) {
        tbody.innerHTML = '<tr><td colspan="4" class="empty-table-state">No pending requests in the queue.</td></tr>';
        return;
    }

    let renderedCount = 0;
    queueArray.forEach(req => {
        const tr = document.createElement('tr');
        
        const unlockDate = formatCSharpDate(req.UnlockAt || req.unlockAt);
        const now = new Date();
        const diffMs = unlockDate - now;
        
        // Hide items that are already due (0 delay or countdown finished).
        // The backend worker will clean them up in the next 5-second polling cycle.
        if (diffMs <= 0) {
            return;
        }

        renderedCount++;
        let formattedTime;
        const diffMins = Math.ceil(diffMs / 60000);
        if (diffMins > 60) {
            const h = Math.floor(diffMins / 60);
            const m = diffMins % 60;
            formattedTime = `<strong>${h}h ${m}m</strong> left`;
        } else {
            formattedTime = `<strong>${diffMins}</strong> min left`;
        }

        const targetUrl = req.TargetUrl || req.targetUrl || "";
        const blockName = req.BlockName || req.blockName || "";

        let displayUrl = targetUrl;
        let displayAction = "Website Exception (Add)";
        let actionColor = "#3b82f6";

        if (targetUrl.startsWith("REMOVE|")) {
            displayUrl = targetUrl.substring(7);
            displayAction = "Blocked Website (Remove)";
            actionColor = "#ef4444";
        } else if (targetUrl.startsWith("APP_ALLOW|")) {
            const parts = targetUrl.substring(10).split('\\');
            displayUrl = parts[parts.length - 1];
            displayAction = "App Allow";
            actionColor = "#10b981";
        } else if (targetUrl === "APP_DISABLE_CONTROL") {
            displayUrl = "Disable App Control";
            displayAction = "App Toggle (2\u00d7 Delay)";
            actionColor = "#f59e0b";
        } else if (targetUrl === "APP_ENABLE_CONTROL") {
            displayUrl = "Enable App Control";
            displayAction = "App Toggle";
            actionColor = "#10b981";
        } else if (targetUrl === "STOP_BLOCK") {
            displayUrl = "Entirely Disable Block";
            displayAction = "Block Stop";
            actionColor = "#ef4444";
        } else if (blockName === "System") {
            displayAction = "System Setting";
            actionColor = "#8b5cf6";
            if (targetUrl.startsWith("GlobalDelayHours|")) {
                const totalHours = parseFloat(targetUrl.split('|')[1]);
                const h = Math.floor(totalHours);
                const m = Math.round((totalHours - h) * 60);
                let timeStr = "";
                if (h > 0) timeStr += `${h}h `;
                if (m > 0 || h === 0) timeStr += `${m}m`;
                displayUrl = `Global Delay \u2192 ${timeStr.trim()}`;
            }
        }

        tr.innerHTML = `
            <td>
                <div style="font-weight: 600;">${displayUrl}</div>
                <div style="font-size: 0.75rem; font-weight: 500; color: ${actionColor}; margin-top: 2px;">${displayAction}</div>
            </td>
            <td style="text-align: center;">${blockName}</td>
            <td title="${unlockDate.toLocaleString()}" style="text-align: center; cursor: help;">${formattedTime}</td>
            <td style="text-align: center;">
                <button onclick="cancelQueueItem('${req.Id || req.id}')" class="btn btn-ghost" style="color: #ef4444; padding: 4px;" title="Cancel Request">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <circle cx="12" cy="12" r="10"></circle>
                        <line x1="15" y1="9" x2="9" y2="15"></line>
                        <line x1="9" y1="9" x2="15" y2="15"></line>
                    </svg>
                </button>
            </td>
        `;
        tbody.appendChild(tr);
    });

    if (renderedCount === 0) {
        tbody.innerHTML = '<tr><td colspan="4" class="empty-table-state">No pending requests in the queue.</td></tr>';
    }
}
