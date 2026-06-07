import { showToast } from './utils.js';
import { generateCtblContent } from './ctbl-generator.js';

let allTemplates = [];
let filteredTemplates = [];
let currentSearchField = 'all'; // 'all', 'title', 'author', 'tags'
let currentSearch = '';

/**
 * Initializes the template gallery.
 */
export async function initTemplateGallery() {
    const gallery = document.getElementById('templates-gallery');
    const searchInput = document.getElementById('templates-search');
    
    if (!gallery) return;

    try {
        // Fetch local templates
        const localData = await fetchTemplates('templates.json');

        allTemplates = [...(localData.templates || [])];
        filteredTemplates = [...allTemplates];
        
        renderGallery();

        if (searchInput) {
            searchInput.addEventListener('input', (e) => {
                currentSearch = e.target.value.toLowerCase();
                applyFilters();
            });
        }

        setupDropdownLogic();
        setupModalListeners();
    } catch (err) {
        console.error('Failed to load templates:', err);
        gallery.innerHTML = `<div class="error-state">Error loading templates. Please check your connection.</div>`;
    }
}

function setupDropdownLogic() {
    const trigger = document.getElementById('templates-dd-trigger');
    const menu = document.getElementById('templates-dd-menu');
    const slider = document.getElementById('templates-dd-slider');

    // Submenu navigation
    const showFieldBtn = document.getElementById('templates-dd-show-field');
    const hideSubmenuBtn = document.getElementById('templates-dd-hide-submenu');
    const submenuTitleText = document.getElementById('templates-submenu-title-text');

    // Filter items
    const allTemplatesBtn = document.getElementById('templates-dd-all');
    const fieldItems = document.querySelectorAll('.filter-group-field');
    const clearBtn = document.getElementById('templates-clear-filters');

    // Filter summary elements
    const filterSummary = document.getElementById('templates-filter-summary');
    const summaryField = document.getElementById('templates-summary-field');
    const summaryFieldValue = document.getElementById('templates-summary-field-value');

    if (!trigger || !menu || !allTemplatesBtn) return;

    // ── Open / Close ──────────────────────────────────────────────────────────

    function openDropdown() {
        menu.classList.add('show');
        trigger.classList.add('open');
    }

    function closeDropdown() {
        menu.classList.remove('show');
        trigger.classList.remove('open');
        slider.style.transition = 'none';
        slider.style.transform = 'translateX(0)';
        setTimeout(() => { slider.style.transition = ''; }, 50);
    }

    trigger.addEventListener('click', (e) => {
        e.stopPropagation();
        if (menu.classList.contains('show')) closeDropdown();
        else openDropdown();
    });

    document.addEventListener('click', (e) => {
        if (menu.classList.contains('show') && !trigger.contains(e.target) && !menu.contains(e.target)) {
            closeDropdown();
        }
    });

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && menu.classList.contains('show')) {
            closeDropdown();
            trigger.focus();
        }
    });

    // ── Submenu navigation ────────────────────────────────────────────────────

    if (showFieldBtn) {
        showFieldBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            const title = showFieldBtn.getAttribute('data-title');
            if (submenuTitleText && title) submenuTitleText.textContent = title;
            document.getElementById('templates-submenu-field').style.display = 'block';
            slider.style.transform = 'translateX(-50%)';
        });
    }

    if (hideSubmenuBtn) {
        hideSubmenuBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            slider.style.transform = 'translateX(0)';
        });
    }

    // ── Filter summary ────────────────────────────────────────────────────────

    function updateFilterSummary() {
        const activeField = document.querySelector('.filter-group-field.active');

        if (summaryField && summaryFieldValue) {
            if (activeField) {
                summaryFieldValue.textContent = activeField.textContent;
                summaryField.style.display = 'inline-flex';
            } else {
                summaryField.style.display = 'none';
            }
        }

        if (filterSummary) {
            filterSummary.classList.toggle('visible', !!activeField);
        }
    }

    function updateTriggerState() {
        const hasFilters = !allTemplatesBtn.classList.contains('active');
        trigger.classList.toggle('has-filters', hasFilters);
    }

    // ── Selection handler ─────────────────────────────────────────────────────

    function handleFieldSelection(item) {
        fieldItems.forEach(i => i.classList.remove('active'));
        item.classList.add('active');

        // Mirror App Control: make the parent "Field" button go green too
        const parentId = item.getAttribute('data-parent');
        if (parentId) document.getElementById(parentId)?.classList.add('active');

        allTemplatesBtn.classList.remove('active');
        currentSearchField = item.dataset.value;

        updateTriggerState();
        updateFilterSummary();
        applyFilters();
        closeDropdown();
    }

    fieldItems.forEach(item => {
        item.addEventListener('click', (e) => {
            e.stopPropagation();
            handleFieldSelection(item);
        });
    });

    // ── Reset ─────────────────────────────────────────────────────────────────

    function resetAllFilters() {
        fieldItems.forEach(i => i.classList.remove('active'));
        if (showFieldBtn) showFieldBtn.classList.remove('active');
        allTemplatesBtn.classList.add('active');
        currentSearchField = 'all';

        const searchInput = document.getElementById('templates-search');
        if (searchInput) searchInput.value = '';
        currentSearch = '';

        updateTriggerState();
        updateFilterSummary();
        applyFilters();
        closeDropdown();
    }

    allTemplatesBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        resetAllFilters();
    });

    if (clearBtn) {
        clearBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            resetAllFilters();
        });
    }
}


async function fetchTemplates(url) {
    const resp = await fetch(url);
    if (!resp.ok) throw new Error(`HTTP error! status: ${resp.status}`);
    return await resp.json();
}

function renderGallery() {
    const gallery = document.getElementById('templates-gallery');
    if (!gallery) return;

    if (filteredTemplates.length === 0) {
        gallery.innerHTML = `<div class="empty-state">No templates found matching your search.</div>`;
        return;
    }

    gallery.innerHTML = filteredTemplates.map(template => `
        <div class="template-card" data-id="${template.id}" onclick="window.previewTemplate('${template.id}')">
            <div class="template-header">
                <h3 class="template-title">${template.title}</h3>
                <span class="template-author">by ${template.author}</span>
            </div>
            <p class="template-description">${template.description}</p>
            <div class="template-tags">
                ${(template.tags || []).map(tag => `<span class="template-tag">${tag}</span>`).join('')}
            </div>
        </div>
    `).join('');
}

function applyFilters() {
    filteredTemplates = allTemplates.filter(t => {
        if (!currentSearch) return true;

        if (currentSearchField === 'all') {
            return (
                t.title.toLowerCase().includes(currentSearch) ||
                t.author.toLowerCase().includes(currentSearch) ||
                t.tags.some(tag => tag.toLowerCase().includes(currentSearch)) ||
                t.description.toLowerCase().includes(currentSearch)
            );
        } else if (currentSearchField === 'tags') {
            return t.tags.some(tag => tag.toLowerCase().includes(currentSearch));
        } else {
            const val = t[currentSearchField] || '';
            return val.toLowerCase().includes(currentSearch);
        }
    });
    
    renderGallery();
}



/**
 * Global handlers for card actions
 */
function parseLockType(lockStr) {
    if (!lockStr) return "Random Text"; // CT Default
    if (lockStr.startsWith("delay")) {
        const parts = lockStr.split(',');
        if (parts.length > 2) return `Time Locked (${parts[1]} ${parts[2]})`;
    }
    if (lockStr === "window") return "Random Text";
    if (lockStr === "password") return "Password Protected";
    if (lockStr === "restart") return "Restart Required";
    return lockStr;
}

function parseSchedule(content) {
    if (content.type === "scheduled") return "Scheduled";
    return "Continuous";
}

function parseBreaks(breakStr) {
    if (!breakStr || breakStr === "none") return "None";
    if (breakStr.startsWith("allowance")) {
        const parts = breakStr.split(',');
        if (parts.length > 3) return `${parts[1]} ${parts[2]} / ${parts[3]}`;
    }
    return "Yes";
}

function renderList(containerId, items) {
    const container = document.getElementById(containerId);
    if (!items || items.length === 0) {
        container.innerHTML = `<div style="color: #a0a0a0; font-style: italic;">No items</div>`;
        return;
    }
    container.innerHTML = items.map(item => `<div>${item}</div>`).join('');
}

window.previewTemplate = function(id) {
    const template = allTemplates.find(t => t.id === id);
    if (!template) return;

    const modal = document.getElementById('list-preview-modal');
    
    document.getElementById('preview-title').textContent = template.title;
    document.getElementById('preview-author').textContent = `by ${template.author}`;
    document.getElementById('preview-description').textContent = template.description;

    document.getElementById('preview-lock-type').textContent = parseLockType(template.content.lock);
    document.getElementById('preview-schedule').textContent = parseSchedule(template.content);
    document.getElementById('preview-breaks').textContent = parseBreaks(template.content.break);

    const websites = template.content.websites || [];
    const apps = template.content.apps || [];
    const exceptions = template.content.exceptions || [];

    document.getElementById('count-websites').textContent = websites.length;
    document.getElementById('count-apps').textContent = apps.length;
    document.getElementById('count-exceptions').textContent = exceptions.length;

    renderList('list-websites', websites);
    renderList('list-apps', apps);
    renderList('list-exceptions', exceptions);

    const rawContent = generateCtblContent(template);
    document.getElementById('preview-textarea').value = rawContent;

    document.getElementById('preview-visual-view').style.display = 'block';
    document.getElementById('preview-textarea').style.display = 'none';
    document.getElementById('toggle-raw-btn').textContent = 'View Raw JSON';

    document.querySelectorAll('.ct-preview-tab').forEach(t => {
        t.parentElement.classList.remove('active');
    });
    document.querySelectorAll('.ct-tab-pane').forEach(c => c.classList.remove('active'));
    
    document.querySelector('.ct-preview-tab[data-target="preview-websites"]').parentElement.classList.add('active');
    document.getElementById('preview-websites').classList.add('active');

    modal.dataset.activeId = id;
    modal.style.display = 'flex';
};

window.copyTemplate = function(id) {
    const template = allTemplates.find(t => t.id === id);
    if (!template) return;

    let itemsToCopy = [];
    const activePane = document.querySelector('.ct-tab-pane.active');
    const rawView = document.getElementById('preview-textarea');
    
    // If Raw JSON view is open, copy the exact JSON string
    if (rawView && rawView.style.display !== 'none') {
        navigator.clipboard.writeText(rawView.value);
        return;
    }
    
    if (activePane) {
        if (activePane.id === 'preview-websites') {
            itemsToCopy = template.content.websites || [];
        } else if (activePane.id === 'preview-apps') {
            itemsToCopy = template.content.apps || [];
        } else if (activePane.id === 'preview-exceptions') {
            itemsToCopy = template.content.exceptions || [];
        }
    } else {
        itemsToCopy = template.content.websites || [];
    }

    const content = itemsToCopy.join('\n');
    
    if (content) {
        navigator.clipboard.writeText(content);
    }
};

window.downloadTemplate = function(id) {
    const template = allTemplates.find(t => t.id === id);
    if (!template) return;

    const ctblContent = generateCtblContent(template);
    const filename = `${template.title.replace(/\s+/g, '_').toLowerCase()}.ctbbl`;
    
    if (window.chrome?.webview?.postMessage && window.AppCommands && window.AppCommands.SaveTemplateFile) {
        window.chrome.webview.postMessage(JSON.stringify({
            command: window.AppCommands.SaveTemplateFile,
            data: { filename: filename, content: ctblContent }
        }));
    } else {
        console.warn("WebView2 API not found. Cannot save file natively.");
    }
};

function setupModalListeners() {
    const modal = document.getElementById('list-preview-modal');
    const closeBtn = document.getElementById('close-preview-btn');
    const copyBtn = document.getElementById('preview-copy-btn');
    const downloadBtn = document.getElementById('preview-download-btn');
    const toggleBtn = document.getElementById('toggle-raw-btn');

    if (closeBtn) closeBtn.onclick = () => modal.style.display = 'none';
    if (copyBtn) copyBtn.onclick = () => window.copyTemplate(modal.dataset.activeId);
    if (downloadBtn) downloadBtn.onclick = () => window.downloadTemplate(modal.dataset.activeId);

    if (toggleBtn) {
        toggleBtn.onclick = () => {
            const visualView = document.getElementById('preview-visual-view');
            const rawView = document.getElementById('preview-textarea');
            if (visualView.style.display === 'none') {
                visualView.style.display = 'block';
                rawView.style.display = 'none';
                toggleBtn.textContent = 'View Raw JSON';
            } else {
                visualView.style.display = 'none';
                rawView.style.display = 'block';
                toggleBtn.textContent = 'View Dashboard';
            }
        };
    }

    document.querySelectorAll('.ct-preview-tab').forEach(tab => {
        tab.onclick = (e) => {
            const targetId = e.currentTarget.getAttribute('data-target');
            document.querySelectorAll('.ct-preview-tab').forEach(t => t.parentElement.classList.remove('active'));
            document.querySelectorAll('.ct-tab-pane').forEach(c => c.classList.remove('active'));
            
            e.currentTarget.parentElement.classList.add('active');
            document.getElementById(targetId).classList.add('active');
        };
    });

    window.addEventListener('click', (event) => {
        if (event.target === modal) modal.style.display = 'none';
    });
}
