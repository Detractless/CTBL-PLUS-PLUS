// Audit tab rendering: log entry formatter and table renderer.
import { formatCSharpDate } from './utils.js';

export function getAuditLogFormat(action, targetUrl, blockName) {
    let sentence = "";
    let actionStyle = 'font-weight: 500; ';

    if (targetUrl.startsWith("REMOVE|")) {
        const parsedUrl = targetUrl.substring(7);
        if (action.includes("Queued")) {
            sentence = `Queued removal of blocked website <strong>${parsedUrl}</strong> from the <strong>${blockName}</strong> blocklist.`;
            actionStyle += 'color: #3b82f6;'; // blue
        } else {
            sentence = `Successfully removed <strong>${parsedUrl}</strong> from the <strong>${blockName}</strong> blocklist.`;
            actionStyle += 'color: #10b981;'; // green
        }
    } 
    else if (targetUrl === "STOP_BLOCK" || targetUrl === "Block State") {
        if (action.includes("Queued")) {
            sentence = `Queued total shutdown of the <strong>${blockName}</strong> block engine.`;
            actionStyle += 'color: #3b82f6;';
        } else if (action.includes("Reverted")) {
            sentence = `Protected the <strong>${blockName}</strong> block engine from unauthorized modification.`;
            actionStyle += 'color: #ef4444;';
        } else {
            sentence = `Successfully disabled the <strong>${blockName}</strong> block engine.`;
            actionStyle += 'color: #10b981;';
        }
    } 
    else if (targetUrl === "GlobalDelayHours" || targetUrl.startsWith("GlobalDelayHours|")) {
        const paramMatches = action.match(/to (.*)/);
        const timeVal = paramMatches ? paramMatches[1] : "an unknown time";
        
        if (action.includes("Queued")) {
            sentence = `Requested to decrease the global security delay to <strong>${timeVal}</strong>.`;
            actionStyle += 'color: #3b82f6;';
        } else {
            sentence = `System global security delay updated to <strong>${timeVal}</strong>.`;
            actionStyle += 'color: #10b981;';
        }
    } 
    else if (targetUrl === "Queue") {
        if (action.includes("Cancelled")) {
            sentence = `User manually cancelled a queued security action.`;
        } else {
            sentence = `System executed queue maintenance task.`;
        }
        actionStyle += 'color: #ef4444;';
    } 
    else if (blockName === 'AppControl' || targetUrl.startsWith('APP_')) {
        let cleanTarget = targetUrl;
        if (cleanTarget.startsWith("APP_ALLOW|")) cleanTarget = cleanTarget.substring(10);
        if (cleanTarget.startsWith("APP_REVOKE|")) cleanTarget = cleanTarget.substring(11);

        if (action.includes("Queued")) {
            sentence = `Queued a new application exception for <strong>${cleanTarget}</strong>.`;
            actionStyle += 'color: #3b82f6;';
        } else if (action.includes("Revoke") || action.includes("Removed")) {
            sentence = `Successfully revoked access for application <strong>${cleanTarget}</strong>.`;
            actionStyle += 'color: #ef4444;';
        } else {
            sentence = `Successfully added a new application exception for <strong>${cleanTarget}</strong>.`;
            actionStyle += 'color: #10b981;';
        }
    }
    else if (targetUrl.includes("://") || targetUrl.includes("*") || targetUrl.includes(".")) {
        if (action === "Removed Website") {
            sentence = `Successfully removed <strong>${targetUrl}</strong> from the <strong>${blockName}</strong> blocklist.`;
            actionStyle += 'color: #10b981;';
        } else if (action.includes("Queued")) {
            sentence = `Queued a new website exception for <strong>${targetUrl}</strong> on the <strong>${blockName}</strong> blocklist.`;
            actionStyle += 'color: #3b82f6;';
        } else {
            sentence = `Successfully added a new website exception for <strong>${targetUrl}</strong> on the <strong>${blockName}</strong> blocklist.`;
            actionStyle += 'color: #10b981;';
        }
    } 
    else {
        sentence = `${action} for ${targetUrl} [${blockName}].`;
        actionStyle += 'color: #6b7280;';
    }

    if (action.includes('Failed')) {
        sentence = `Action failed: ${targetUrl} on ${blockName}.`;
        actionStyle = 'font-weight: 500; color: #f59e0b;';
    } else if (action.includes('Reverted') && !sentence.includes('Protected')) {
        sentence = `Security Engine blocked an unauthorized change to ${targetUrl}.`;
        actionStyle = 'font-weight: 500; color: #ef4444;';
    }
    
    return { sentence, actionStyle };
}

export function renderAuditLog(logArray) {
    const tbody = document.getElementById('audit-body');
    if (!tbody) return;
    tbody.innerHTML = '';

    if (!logArray || logArray.length === 0) {
        tbody.innerHTML = '<tr><td colspan="2" class="empty-table-state">No audit logs available.</td></tr>';
        return;
    }

    logArray.forEach(log => {
        const tr = document.createElement('tr');
        
        const date = formatCSharpDate(log.Timestamp || log.timestamp);
        const formattedTime = date.toLocaleString();
        
        const action = log.Action || log.action || "";
        const targetUrl = log.TargetUrl || log.targetUrl || "";
        const blockName = log.BlockName || log.blockName || "";

        const { sentence, actionStyle } = getAuditLogFormat(action, targetUrl, blockName);

        tr.innerHTML = `
            <td style="white-space: nowrap; color: #a1a1aa; font-size: 14px; vertical-align: top; padding-top: 15px;">${formattedTime}</td>
            <td style="${actionStyle} line-height: 1.5; padding-top: 1.1rem;">${sentence}</td>
        `;
        tbody.appendChild(tr);
    });
}
