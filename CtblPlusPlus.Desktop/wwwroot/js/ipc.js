import { renderBlocksDropdown, renderQueue } from './queue-renderer.js';
import { renderAuditLog }                    from './audit-renderer.js';
import { handleSecurityState }                          from './security-renderer.js';
import { renderAppRegistry, handleAppSelected,
         handleAppControlState }                        from './app-control-renderer.js';

export function sendCommand(command, payload = {}) {
    window.chrome?.webview?.postMessage(JSON.stringify({ command, ...payload }));
    if (!window.chrome?.webview) {
        console.warn('WebView2 IPC not available.');
    }
}

export function setupIpc() {
    if (window.chrome?.webview) {
        window.chrome.webview.addEventListener('message', event => {
            try {
                const message = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
                
                switch (message.command) {
                    case window.AppCommands.BlocksData:
                        renderBlocksDropdown(message.data || {});
                        break;
                    case window.AppCommands.QueueData:
                        renderQueue(message.data || []);
                        break;
                    case window.AppCommands.AuditLogData:
                        renderAuditLog(message.data || []);
                        break;
                    case window.AppCommands.SecurityState:
                        handleSecurityState(message.data);
                        break;
                    case window.AppCommands.SettingsData:
                        handleSettingsData(message.data);
                        break;
                    case window.AppCommands.AppRegistryData:
                        renderAppRegistry(message.data || []);
                        break;
                    case window.AppCommands.AppSelected:
                        handleAppSelected(message.data);
                        break;
                    case window.AppCommands.AppControlState:
                        handleAppControlState(message.data);
                        break;
                }
            } catch (err) {
                console.error("Failed to parse IPC message", err);
            }
        });
    }
}

function handleSettingsData(data) {
    const totalHours = parseFloat(data.GlobalDelayHours) || 1.0;
    const h = Math.floor(totalHours);
    const m = Math.round((totalHours - h) * 60);
    const hInput = document.getElementById('global-delay-hours');
    const mInput = document.getElementById('global-delay-minutes');
    if (hInput) hInput.value = h;
    if (mInput) mInput.value = m;
}
