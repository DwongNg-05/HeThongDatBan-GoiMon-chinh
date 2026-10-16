/* Query committed totals every 20 seconds; no client-side accumulation or duplicate counting. */
(function (global) {
    'use strict';
    const intervalMs = 20000;
    const timeoutMs = 12000;

    // Dependencies are supplied by the browser, or a deterministic clock in the tests.
    function startLiveReport(env) {
        const { document, window } = env;
        const root = document.querySelector('[data-report-live]');
        if (!root || root.dataset.enabled !== 'true') return null;
        const panel = root.querySelector('[data-report-panel]');
        const status = root.querySelector('[data-report-refresh-status]');
        const button = root.querySelector('[data-report-refresh]');
        const form = root.querySelector('form');
        let active = null;
        let stopped = false;
        let interval;
        const currentQuery = window.location.search;
        const url = root.dataset.endpoint + currentQuery;
        button.hidden = false;

        function message(text, failed) {
            status.textContent = text;
            status.className = failed ? 'text-danger' : 'text-muted';
        }

        async function refresh() {
            if (stopped || active) return;
            const controller = new env.AbortController();
            active = controller;
            const timeout = env.setTimeout(() => controller.abort(), timeoutMs);
            panel.setAttribute('aria-busy', 'true');
            button.disabled = true;
            message('Đang cập nhật số liệu…', false);
            try {
                const response = await env.fetch(url, {
                    method: 'GET', cache: 'no-store', credentials: 'same-origin',
                    headers: { 'Accept': 'text/html', 'X-Requested-With': 'XMLHttpRequest' },
                    signal: controller.signal
                });
                if (stopped) return;
                if (response.status === 401 || response.status === 403 || response.redirected) {
                    message('Phiên đăng nhập đã hết hoặc bạn không còn quyền xem. Vui lòng đăng nhập lại.', true);
                    stopped = true;
                    button.hidden = true;
                    env.clearInterval(interval);
                    return;
                }
                if (!response.ok || response.headers.get('X-Report-Snapshot') !== '1') throw new Error('Report unavailable');
                const html = await response.text();
                if (stopped || controller.signal.aborted) return;
                const parsed = new env.DOMParser().parseFromString(html, 'text/html');
                const snapshot = parsed.querySelector('[data-report-snapshot]');
                if (!snapshot || snapshot.dataset.state !== 'ready') throw new Error('Invalid report snapshot');
                const scroll = panel.querySelector('.revenue-scroll')?.scrollLeft ?? 0;
                const expanded = panel.querySelector('details')?.open ?? false;
                panel.replaceChildren(document.importNode(snapshot, true));
                const newScroll = panel.querySelector('.revenue-scroll');
                if (newScroll) newScroll.scrollLeft = scroll;
                const details = panel.querySelector('details');
                if (details) details.open = expanded;
                // The default "today" can roll to a new business day. Never overwrite unsaved inputs.
                if (!currentQuery && !form.contains(document.activeElement)) {
                    const from = form.querySelector('[name="from"]');
                    const to = form.querySelector('[name="to"]');
                    if (from.value === from.defaultValue && to.value === to.defaultValue) {
                        from.value = from.defaultValue = snapshot.dataset.from;
                        to.value = to.defaultValue = snapshot.dataset.to;
                    }
                }
                message('Đã cập nhật. Tự động kiểm tra lại sau 20 giây.', false);
            } catch (error) {
                if (!stopped) message('Không thể cập nhật số liệu. Dữ liệu đang hiển thị có thể đã cũ; hệ thống sẽ thử lại sau 20 giây.', true);
            } finally {
                env.clearTimeout(timeout);
                if (active === controller) active = null;
                panel.setAttribute('aria-busy', 'false');
                button.disabled = false;
            }
        }

        function stop() {
            stopped = true;
            env.clearInterval(interval);
            active?.abort();
        }
        function resume() {
            if (stopped) {
                stopped = false;
                interval = env.setInterval(refresh, intervalMs);
            }
            refresh();
        }
        button.addEventListener('click', refresh);
        form.addEventListener('submit', () => { if (form.checkValidity()) stop(); });
        document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
        window.addEventListener('online', refresh);
        window.addEventListener('focus', refresh);
        window.addEventListener('pagehide', stop);
        window.addEventListener('pageshow', event => { if (event.persisted) resume(); });
        interval = env.setInterval(refresh, intervalMs);
        // Recover immediately from an initial SQL error instead of displaying a false successful update time.
        if (panel.querySelector('[data-report-snapshot]')?.dataset.state !== 'ready') refresh();
        return { refresh, stop };
    }

    if (typeof module !== 'undefined' && module.exports) module.exports = { startLiveReport, intervalMs, timeoutMs };
    if (global.document) startLiveReport({
        document: global.document, window: global, fetch: global.fetch.bind(global),
        AbortController: global.AbortController, DOMParser: global.DOMParser,
        setInterval: global.setInterval.bind(global), clearInterval: global.clearInterval.bind(global),
        setTimeout: global.setTimeout.bind(global), clearTimeout: global.clearTimeout.bind(global)
    });
})(typeof window !== 'undefined' ? window : globalThis);
