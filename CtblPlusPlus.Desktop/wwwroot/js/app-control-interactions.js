// App Control tab interactions: search, filter dropdown, select-all, bulk actions.
import { sendCommand } from './ipc.js';
import { showToast } from './utils.js';
import { renderAppRegistry, getCurrentAppsArray, getSelectedAppPaths } from './app-control-renderer.js';

// Filter state — moved here from renderer.js; owned by event handlers, not the renderer
let currentStatusFilter = null; // 'Allowed', 'Blocked', 'Detected'
let currentFieldFilter = null;  // 'Name', 'Publisher', 'Path'

// Initializing listeners for Search and Select All (called once from main.js)
export function initAppControlInteractions() {
    initSearchFilter();

    const searchInput = document.getElementById('app-search');
    const selectAllCheckbox = document.getElementById('app-select-all');
    const bulkAllowBtn = document.getElementById('bulk-allow-btn');
    const bulkRevokeBtn = document.getElementById('bulk-revoke-btn');

    if (searchInput) {
        let debounceTimer;
        searchInput.addEventListener('input', () => {
            clearTimeout(debounceTimer);
            debounceTimer = setTimeout(() => {
                renderAppRegistry(getCurrentAppsArray(), currentStatusFilter, currentFieldFilter);
            }, 200);
        });
    }

    if (selectAllCheckbox) {
        selectAllCheckbox.addEventListener('change', (e) => {
            const isChecked = e.target.checked;
            const checkboxes = document.querySelectorAll('.app-row-checkbox');
            const selectedPaths = getSelectedAppPaths();
            
            checkboxes.forEach(cb => {
                const path = cb.getAttribute('data-path');
                cb.checked = isChecked;
                if (isChecked) {
                    selectedPaths.add(path);
                } else {
                    selectedPaths.delete(path);
                }
            });
            // Re-render to update bulk bar count
            renderAppRegistry(getCurrentAppsArray(), currentStatusFilter, currentFieldFilter);
        });
    }

    if (bulkAllowBtn) {
        bulkAllowBtn.addEventListener('click', () => {
            const selectedPaths = getSelectedAppPaths();
            if (selectedPaths.size === 0) return;
            const paths = Array.from(selectedPaths);
            
            // IMPORTANT: Must use the imported sendCommand, NOT window.sendCommand (which is undefined).
            // The { data: { paths, status } } wrapper is required — sendCommand spreads the payload
            // into the top-level JSON, so this produces { command: "...", data: { paths, status } }
            // which maps to IpcMessage.Data (JsonElement) on the C# side.
            sendCommand(window.AppCommands.BulkUpdateAppControlRules, { 
                data: { paths, status: 'Allowed' } 
            });

            showToast('app-control-toast', `Bulk allow queued for ${paths.length} app(s).`, 3000);

            selectedPaths.clear();
            if (selectAllCheckbox) selectAllCheckbox.checked = false;
            renderAppRegistry(getCurrentAppsArray(), currentStatusFilter, currentFieldFilter);
        });
    }

    if (bulkRevokeBtn) {
        bulkRevokeBtn.addEventListener('click', () => {
            const selectedPaths = getSelectedAppPaths();
            if (selectedPaths.size === 0) return;
            const paths = Array.from(selectedPaths);
            
            // IMPORTANT: Must use the imported sendCommand, NOT window.sendCommand (which is undefined).
            // See the bulkAllowBtn handler above for full explanation of the payload structure.
            sendCommand(window.AppCommands.BulkUpdateAppControlRules, { 
                data: { paths, status: 'Detected' } 
            });

            showToast('app-control-toast', `Bulk revoke queued for ${paths.length} app(s).`, 3000);

            selectedPaths.clear();
            if (selectAllCheckbox) selectAllCheckbox.checked = false;
            renderAppRegistry(getCurrentAppsArray(), currentStatusFilter, currentFieldFilter);
        });
    }
}

// ============================
// Nested Dropdown Filter Logic
// ============================
function initSearchFilter() {
    const ddTrigger = document.getElementById('dd-trigger');
    const ddMenu = document.getElementById('dd-menu');
    const ddSlider = document.getElementById('dd-slider');
    const showSubmenuBtns = document.querySelectorAll('.dd-show-submenu');
    const hideSubmenuBtn = document.getElementById('dd-hide-submenu');
    const submenuBlocks = document.querySelectorAll('.submenu-content-block');
    const submenuTitleText = document.getElementById('submenu-title-text');
    const filterSummary = document.getElementById('filter-summary');

    if (!ddTrigger) return;

    function openDropdown() {
        ddMenu.classList.add('show');
        ddTrigger.classList.add('open');
    }

    function closeDropdown() {
        ddMenu.classList.remove('show');
        ddTrigger.classList.remove('open');
        ddSlider.style.transition = 'none';
        ddSlider.style.transform = 'translateX(0)';
        setTimeout(() => { ddSlider.style.transition = ''; }, 50);
    }

    ddTrigger.addEventListener('click', (e) => {
        e.stopPropagation();
        if (ddMenu.classList.contains('show')) closeDropdown();
        else openDropdown();
    });

    document.addEventListener('click', (e) => {
        if (ddMenu.classList.contains('show') && !ddTrigger.contains(e.target) && !ddMenu.contains(e.target)) {
            closeDropdown();
        }
    });

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && ddMenu.classList.contains('show')) {
            closeDropdown();
            ddTrigger.focus();
        }
    });

    showSubmenuBtns.forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            const target = btn.getAttribute('data-target');
            const title = btn.getAttribute('data-title');
            
            submenuBlocks.forEach(block => block.style.display = 'none');
            document.getElementById('submenu-' + target).style.display = 'block';
            if (submenuTitleText) submenuTitleText.textContent = title;

            ddSlider.style.transform = 'translateX(-50%)';
        });
    });

    if (hideSubmenuBtn) {
        hideSubmenuBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            ddSlider.style.transform = 'translateX(0)';
        });
    }

    const allAppsBtn = document.getElementById('dd-all-apps');
    const statusItems = document.querySelectorAll('.filter-group-status');
    const fieldItems = document.querySelectorAll('.filter-group-field');
    const clearFiltersBtn = document.getElementById('dd-clear-filters');

    function handleSelection(item, groupItems, parentId, filterType) {
         groupItems.forEach(i => i.classList.remove('active'));
         item.classList.add('active');
         
         document.getElementById(parentId).classList.add('active');
         allAppsBtn.classList.remove('active');
         
         if (filterType === 'status') {
             currentStatusFilter = item.textContent;
         } else if (filterType === 'field') {
             currentFieldFilter = item.textContent;
         }
         
         updateTriggerState();
         updateFilterSummary();
         closeDropdown();
         
         // Trigger re-render with current filter state passed as parameters
         renderAppRegistry(getCurrentAppsArray(), currentStatusFilter, currentFieldFilter);
    }

    statusItems.forEach(item => {
         item.addEventListener('click', (e) => {
             e.stopPropagation();
             handleSelection(item, statusItems, item.getAttribute('data-parent'), 'status');
         });
    });

    fieldItems.forEach(item => {
         item.addEventListener('click', (e) => {
             e.stopPropagation();
             handleSelection(item, fieldItems, item.getAttribute('data-parent'), 'field');
         });
    });

    function resetAllFilters() {
         statusItems.forEach(i => i.classList.remove('active'));
         fieldItems.forEach(i => i.classList.remove('active'));
         document.getElementById('dd-show-status').classList.remove('active');
         document.getElementById('dd-show-field').classList.remove('active');
         allAppsBtn.classList.add('active');
         
         currentStatusFilter = null;
         currentFieldFilter = null;
         
         updateTriggerState();
         updateFilterSummary();
         closeDropdown();
         
         renderAppRegistry(getCurrentAppsArray(), currentStatusFilter, currentFieldFilter);
    }

    allAppsBtn.addEventListener('click', (e) => {
         e.stopPropagation();
         resetAllFilters();
    });

    clearFiltersBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        resetAllFilters();
    });

    function updateTriggerState() {
        const hasFilters = !allAppsBtn.classList.contains('active');
        ddTrigger.classList.toggle('has-filters', hasFilters);
    }

    function updateFilterSummary() {
        const activeStatus = document.querySelector('.filter-group-status.active');
        const activeField = document.querySelector('.filter-group-field.active');

        const summaryStatus = document.getElementById('summary-status');
        const summaryField = document.getElementById('summary-field');
        const summaryStatusValue = document.getElementById('summary-status-value');
        const summaryFieldValue = document.getElementById('summary-field-value');

        if (activeStatus) {
            summaryStatusValue.textContent = activeStatus.textContent;
            summaryStatus.style.display = 'inline-flex';
        } else {
            summaryStatus.style.display = 'none';
        }

        if (activeField) {
            summaryFieldValue.textContent = activeField.textContent;
            summaryField.style.display = 'inline-flex';
        } else {
            summaryField.style.display = 'none';
        }

        const hasAny = activeStatus || activeField;
        filterSummary.classList.toggle('visible', !!hasAny);
    }
}
