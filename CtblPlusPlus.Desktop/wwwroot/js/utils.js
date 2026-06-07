export function formatCSharpDate(dateStr) {
    if (typeof dateStr === 'string' && !dateStr.endsWith('Z')) {
        return new Date(dateStr + 'Z');
    }
    return new Date(dateStr);
}

export function showToast(toastId, message, duration = 2000) {
    const toast = document.getElementById(toastId);
    if (!toast) return;
    if (message) toast.textContent = message;
    toast.classList.add('visible');
    setTimeout(() => {
        toast.classList.remove('visible');
    }, duration);
}

export function sanitizeInput(rawString) {
    if (!rawString) return "";
    
    let trimmed = rawString.trim();
    let sanitized = trimmed.toLowerCase();
    
    if (sanitized.startsWith("file://")) {
        return sanitized;
    }
    
    // Strip protocols
    sanitized = sanitized.replace(/^https?:\/\//, '');
    
    // Strip www.
    sanitized = sanitized.replace(/^www\./, '');
    
    // Keyword detection: evaluate original input for spaces, or lack of domain formatting
    let isKeyword = trimmed.includes(' ') || (!sanitized.includes('.') && !sanitized.includes(':'));
    
    if (isKeyword) {
        // Replace spaces with asterisks
        sanitized = sanitized.replace(/\s+/g, '*');
        
        // Ensure wrapped in asterisks
        if (!sanitized.startsWith('*')) sanitized = '*' + sanitized;
        if (!sanitized.endsWith('*')) sanitized = sanitized + '*';
        
        // Collapse multiple continuous asterisks into a single one
        sanitized = sanitized.replace(/\*+/g, '*');
    }
    
    return sanitized;
}

export async function loadComponent(selector, path, append = false) {
    try {
        const response = await fetch(path);
        if (!response.ok) throw new Error(`Failed to load ${path}: ${response.statusText}`);
        const html = await response.text();
        const target = document.querySelector(selector);
        if (target) {
            if (append) {
                target.insertAdjacentHTML('beforeend', html);
            } else {
                target.innerHTML = html;
            }
        } else {
            console.error(`Target selector ${selector} not found for component ${path}`);
        }
    } catch (err) {
        console.error(`Error loading component ${path}:`, err);
    }
}


