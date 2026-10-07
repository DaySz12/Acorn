// UI-only helpers. This file never receives or handles secret values.
(() => {
    'use strict';

    let bridge = null;
    let lastActivity = 0;
    const ACTIVITY_THROTTLE_MS = 5000;

    window.acorn = {
        attach(dotNetRef) { bridge = dotNetRef; },
        detach() { bridge = null; },
    };

    // Auto-lock: report user activity at most every few seconds.
    const reportActivity = () => {
        const now = Date.now();
        if (bridge && now - lastActivity > ACTIVITY_THROTTLE_MS) {
            lastActivity = now;
            bridge.invokeMethodAsync('OnActivity').catch(() => { });
        }
    };
    for (const type of ['keydown', 'pointerdown', 'wheel']) {
        document.addEventListener(type, reportActivity, { capture: true, passive: true });
    }

    document.addEventListener('keydown', (e) => {
        const mod = e.ctrlKey || e.metaKey;
        const key = (e.key || '').toLowerCase();

        // Ctrl/Cmd+K: quick search.
        if (mod && !e.shiftKey && !e.altKey && key === 'k') {
            e.preventDefault();
            if (bridge) {
                bridge.invokeMethodAsync('OnQuickSearch').catch(() => { });
            }
            return;
        }

        // Reload would only flash the UI; block it so it is not mistaken for a lock.
        if (key === 'f5' || (mod && key === 'r')) {
            e.preventDefault();
        }
    }, { capture: true });

    // Print buttons (recovery key). Declared with data-action because CSP forbids inline handlers.
    document.addEventListener('click', (e) => {
        const target = e.target instanceof Element ? e.target.closest('[data-action="print"]') : null;
        if (target) {
            e.preventDefault();
            window.print();
        }
    });

    // Dropping a file or link onto the window would navigate away from the app.
    for (const type of ['dragover', 'drop']) {
        window.addEventListener(type, (e) => e.preventDefault());
    }
})();
