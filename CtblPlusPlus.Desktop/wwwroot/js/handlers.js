import { sendCommand } from './ipc.js';
import { sanitizeInput, showToast } from './utils.js';
import { populateRemovalDropdown } from './queue-renderer.js';

export function setupTabRouter() {
    const sidebarTabs = document.querySelectorAll('.sidebar-tab');
    const tabPanels = document.querySelectorAll('.tab-panel');
    const contentArea = document.getElementById('content-area');
    const brandPlus = document.getElementById('brand-plus');

    sidebarTabs.forEach(tab => {
        tab.addEventListener('click', () => {
            const targetTab = tab.dataset.tab;

            // Deactivate all
            sidebarTabs.forEach(t => t.classList.remove('active'));
            tabPanels.forEach(p => p.classList.remove('active'));
            if (brandPlus) brandPlus.classList.remove('active');

            // Activate target
            tab.classList.add('active');
            const targetPanel = document.getElementById('tab-' + targetTab);
            if (targetPanel) {
                targetPanel.classList.add('active');
            }

            // Reset scroll position
            if (contentArea) {
                contentArea.scrollTop = 0;
            }
        });
    });
}

export function setupTemplateActivation() {
    const brand = document.querySelector('.sidebar-brand');
    const brandPlus = document.getElementById('brand-plus');
    const sidebarTabs = document.querySelectorAll('.sidebar-tab');
    const tabPanels = document.querySelectorAll('.tab-panel');
    
    if (!brand || !brandPlus) return;

    brand.addEventListener('dblclick', () => {
        // Deactivate all standard tabs
        sidebarTabs.forEach(t => t.classList.remove('active'));
        tabPanels.forEach(p => p.classList.remove('active'));
        
        // Activate visual state on logo
        brandPlus.classList.add('active');
        
        // Show the templates panel
        const templatesPanel = document.getElementById('tab-templates');
        if (templatesPanel) {
            templatesPanel.classList.add('active');
        }
        
        // Reset scroll
        const contentArea = document.getElementById('content-area');
        if (contentArea) contentArea.scrollTop = 0;
    });
}




export function setupActionForm() {
    const actionForm = document.getElementById('action-form');
    const blockSelect = document.getElementById('action-block');
    const typeGroup = document.getElementById('action-type-group');
    const typeSelect = document.getElementById('action-type');
    const submitBtn = document.getElementById('submit-action-btn');
    
    // Detail containers
    const detailAddException = document.getElementById('detail-add-exception');
    const detailRemoveException = document.getElementById('detail-remove-exception');
    const urlPreview = document.getElementById('target-url-preview');

    function hideAllDetails() {
        if (detailAddException) detailAddException.style.display = 'none';
        if (detailRemoveException) detailRemoveException.style.display = 'none';
        if (submitBtn) submitBtn.style.display = 'none';
        if (urlPreview) urlPreview.style.display = 'none';
        const addList = document.getElementById('detail-add-list');
        if (addList) addList.value = '';
    }

    const unsupportedMsg = document.getElementById('unsupported-block-msg');

    if (blockSelect) {
        blockSelect.addEventListener('change', (e) => {
            hideAllDetails();
            if (typeSelect) typeSelect.value = '';
            if (unsupportedMsg) unsupportedMsg.style.display = 'none';
            
            const selectedValue = e.target.value;
            if (selectedValue) {
                if (selectedValue.startsWith('Frozen Turkey,')) {
                    if (unsupportedMsg) unsupportedMsg.style.display = 'block';
                    if (typeGroup) typeGroup.style.display = 'none';
                } else {
                    if (typeGroup) typeGroup.style.display = 'block';
                }
            } else {
                if (typeGroup) typeGroup.style.display = 'none';
            }
        });
    }

    if (typeSelect) {
        typeSelect.addEventListener('change', (e) => {
            hideAllDetails();
            const action = e.target.value;
            const blockName = blockSelect.value;
            
            if (!action || !blockName) return;

            if (action === 'add_exception') {
                if (detailAddException) detailAddException.style.display = 'block';
                if (submitBtn) submitBtn.style.display = 'block';
            } else if (action === 'remove_exception') {
                if (detailRemoveException) detailRemoveException.style.display = 'block';
                if (submitBtn) submitBtn.style.display = 'block';
                populateRemovalDropdown(blockName);
            }
        });
    }

    if (actionForm) {
        actionForm.addEventListener('submit', (e) => {
            e.preventDefault();
            
            const blockName = blockSelect.value;
            const action = typeSelect.value;
            
            if (!blockName || !action) return;

            if (action === 'add_exception') {
                const addList = document.getElementById('detail-add-list');
                const lines = addList.value.split('\n');
                let hasValidInputs = false;
                
                lines.forEach(line => {
                    const targetUrl = sanitizeInput(line);
                    if (targetUrl) {
                        sendCommand(window.AppCommands.RequestException, { blockName, targetUrl });
                        hasValidInputs = true;
                    }
                });
                
                if (hasValidInputs) {
                    addList.value = '';
                    if (detailAddException) detailAddException.style.display = 'none';
                    actionForm.reset();
                    blockSelect.value = '';
                    
                    // Trigger input event to clear preview
                    addList.dispatchEvent(new Event('input', { bubbles: true }));
                }
            } else if (action === 'remove_exception') {
                const selectEl = document.getElementById('detail-remove-list');
                Array.from(selectEl.selectedOptions).forEach(option => {
                    const val = option.value;
                    if (val) {
                        sendCommand(window.AppCommands.RequestRemoval, { blockName, targetUrl: val });
                    }
                });
            }
            
            // Form reset
            blockSelect.value = '';
            if (typeGroup) typeGroup.style.display = 'none';
            typeSelect.value = '';
            hideAllDetails();

            // Immediate re-fetch
            sendCommand(window.AppCommands.GetQueue);
            sendCommand(window.AppCommands.GetAuditLog);
        });
    }
}

export function setupExceptionsForm() {
    const addList = document.getElementById('detail-add-list');
    const urlPreview = document.getElementById('target-url-preview');
    if (!addList || !urlPreview) return;
    
    addList.addEventListener('input', (e) => {
        const rawLines = e.target.value.split('\n');
        let validSanitizedLines = [];
        
        rawLines.forEach(line => {
            const sanitized = sanitizeInput(line);
            if (sanitized) {
                validSanitizedLines.push(sanitized);
            }
        });

        if (validSanitizedLines.length === 0) {
            urlPreview.style.display = 'none';
        } else {
            let previewHtml = `Preview (${validSanitizedLines.length} item${validSanitizedLines.length > 1 ? 's' : ''}): <br>`;
            previewHtml += `<ul style="margin: 0; padding-left: 20px; color: #65a30d;">`;
            validSanitizedLines.forEach(item => {
                previewHtml += `<li><strong>${item}</strong></li>`;
            });
            previewHtml += `</ul>`;
            
            urlPreview.innerHTML = previewHtml;
            urlPreview.style.display = 'block';
        }
    });
}

export function setupSettingsForm() {
    const settingsForm = document.getElementById('settings-form');
    if (!settingsForm) return;
    settingsForm.addEventListener('submit', (e) => {
        e.preventDefault();
        const h = parseInt(document.getElementById('global-delay-hours').value) || 0;
        const m = parseInt(document.getElementById('global-delay-minutes').value) || 0;
        const delayHours = h + (m / 60);
        if (delayHours > 0) {
            sendCommand(window.AppCommands.RequestSettingChange, { targetUrl: 'GlobalDelayHours', delayHours });
            
            const submitBtn = document.getElementById('settings-submit-btn');
            if (submitBtn) {
                submitBtn.textContent = 'Updated ✓';
                showToast('settings-toast', 'Settings updated successfully.', 2000);
                setTimeout(() => submitBtn.textContent = 'Update Global Delay', 2000);
            }
        }
    });
}

export function setupAppControlTab() {
    const appBrowseBtn = document.getElementById('app-control-browse-btn');
    const appPathInput = document.getElementById('app-control-app-path');
    const appQueueBtn = document.getElementById('app-control-queue-btn');
    const appToggleSwitch = document.getElementById('app-control-toggle-switch');

    if (appBrowseBtn) {
        appBrowseBtn.addEventListener('click', () => {
            sendCommand(window.AppCommands.BrowseForApp);
        });
    }

    if (appPathInput) {
        appPathInput.addEventListener('input', () => {
            const val = appPathInput.value.trim();
            appPathInput.dataset.path = val;
            
            if (val.length > 0) {
                if (appQueueBtn) appQueueBtn.style.display = 'block';
                const preview = document.getElementById('app-control-app-preview');
                if (preview) preview.style.display = 'none';
            } else {
                if (appQueueBtn) appQueueBtn.style.display = 'none';
            }
        });
    }

    if (appQueueBtn) {
        appQueueBtn.addEventListener('click', () => {
            const appPath = appPathInput.dataset.path;
            if (!appPath) return;

            sendCommand(window.AppCommands.RequestAppControlRule, { appPath });

            appPathInput.value = '';
            appPathInput.dataset.path = '';
            const preview = document.getElementById('app-control-app-preview');
            if (preview) preview.style.display = 'none';
            appQueueBtn.style.display = 'none';

            showToast('app-control-toast', 'App access request queued successfully.', 3000);

            sendCommand(window.AppCommands.GetQueue);
        });
    }

    if (appToggleSwitch) {
        appToggleSwitch.addEventListener('click', () => {
            sendCommand(window.AppCommands.ToggleAppControl);
        });
    }
}

// Global exposure for onclick handlers rendered in HTML strings
window.cancelQueueItem = function(id) {
    sendCommand(window.AppCommands.CancelRequest, { id });
};

window.revokeAppControlRule = function(id) {
    sendCommand(window.AppCommands.RevokeAppControlRule, { id });
};

window.allowDetectedApp = function(appPath) {
    sendCommand(window.AppCommands.RequestAppControlRule, { appPath });
    showToast('app-control-toast', 'App access request queued successfully.', 3000);
};
