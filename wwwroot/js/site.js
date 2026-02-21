// CNC Tooling Drawing Processor - Site JavaScript

// Check Ollama status on page load
document.addEventListener('DOMContentLoaded', async function () {
    await checkOllamaStatus();
});

async function checkOllamaStatus() {
    const badge = document.getElementById('ollamaStatus');
    if (!badge) return;

    try {
        const response = await fetch('/api/ollama-status');
        const data = await response.json();

        if (data.online) {
            badge.className = 'badge bg-success';
            badge.innerHTML = '<i class="bi bi-circle-fill me-1"></i>AI Online';
        } else {
            badge.className = 'badge bg-danger';
            badge.innerHTML = '<i class="bi bi-circle-fill me-1"></i>AI Offline';
        }
    } catch (error) {
        badge.className = 'badge bg-danger';
        badge.innerHTML = '<i class="bi bi-circle-fill me-1"></i>AI Offline';
    }
}

// Auto-refresh status every 30 seconds
setInterval(checkOllamaStatus, 30000);

// Tooltip initialization
var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
tooltipTriggerList.map(function (el) {
    return new bootstrap.Tooltip(el);
});
