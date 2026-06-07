// Global lockdown overlay state.
export function handleSecurityState(data) {
    const overlay = document.getElementById('lockdown-overlay');
    if (!overlay) return;

    if (data.isSecure) {
        overlay.classList.remove('active');
    } else {
        overlay.classList.add('active');
    }
}
