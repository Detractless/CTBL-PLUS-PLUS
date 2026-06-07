import { setupIpc, sendCommand } from './ipc.js';
import { 
    setupTabRouter, setupActionForm, setupExceptionsForm, 
    setupSettingsForm, setupAppControlTab, setupTemplateActivation
} from './handlers.js';

import { initAppControlInteractions } from './app-control-interactions.js';
import { initTemplateGallery } from './template-manager.js';
import { loadComponent } from './utils.js';


async function initializeApp() {
    // 1. Load all UI components into the skeleton
    // 1. Load all UI components into the skeleton
    // We use Promise.all to load them in parallel
    await Promise.all([
        loadComponent('#overlay-container', 'components/overlay-lockdown.html'),
        loadComponent('#app-shell', 'components/sidebar.html', true), // Append to shell (since main exists)
        loadComponent('#content-area', 'components/tab-queue.html', true),
        loadComponent('#content-area', 'components/tab-app-control.html', true),
        loadComponent('#content-area', 'components/tab-audit.html', true),
        loadComponent('#content-area', 'components/tab-settings.html', true),
        loadComponent('#content-area', 'components/tab-templates.html', true)
    ]);


    // Move sidebar before main if it was appended after
    const shell = document.getElementById('app-shell');
    const sidebar = shell.querySelector('aside.sidebar');
    const main = shell.querySelector('main.content-area');
    if (sidebar && main && shell.lastElementChild === sidebar) {
        shell.insertBefore(sidebar, main);
    }
    
    // 2. Initialize application logic once DOM is ready
    setupIpc();
    setupActionForm();
    setupExceptionsForm();
    setupSettingsForm();
    setupAppControlTab();
    setupTemplateActivation();
    setupTabRouter();
    initTemplateGallery();

    initAppControlInteractions();

    
    // 3. Initial fetch from C# backend
    sendCommand(window.AppCommands.GetBlocks);
    sendCommand(window.AppCommands.GetQueue);
    sendCommand(window.AppCommands.GetAuditLog);
    sendCommand(window.AppCommands.GetSecurityState);
    sendCommand(window.AppCommands.GetSettings);
    sendCommand(window.AppCommands.GetAppControlState);
    sendCommand(window.AppCommands.GetAppRegistry);

    // 4. Polling for active tab data
    setInterval(() => {
        const activePanel = document.querySelector('.tab-panel.active');
        const activeTab = activePanel ? activePanel.id : '';

        if (activeTab === 'tab-queue') {
            sendCommand(window.AppCommands.GetQueue);
        } else if (activeTab === 'tab-app-control') {
            sendCommand(window.AppCommands.GetAppRegistry);
            sendCommand(window.AppCommands.GetAppControlState);
        } else if (activeTab === 'tab-audit') {
            sendCommand(window.AppCommands.GetAuditLog);
        }
    }, 5000);

    // 5. Global UI listeners
    document.addEventListener('wheel', (e) => {
        if(e.ctrlKey) e.preventDefault();
    }, { passive: false });
    
    document.addEventListener('keydown', (e) => {
        if(e.ctrlKey && (e.key === '=' || e.key === '-' || e.key === '+' || e.keyCode === 187 || e.keyCode === 189)) {
            e.preventDefault();
        }
    });
}

document.addEventListener('DOMContentLoaded', initializeApp);
