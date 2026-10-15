(() => {
    const form = document.getElementById('daily-revenue-filter');
    const dashboard = document.getElementById('daily-revenue-dashboard');
    const refreshButton = document.getElementById('daily-revenue-refresh');
    const retryButton = document.getElementById('daily-revenue-retry');
    const error = document.getElementById('daily-revenue-refresh-error');
    const errorText = document.getElementById('daily-revenue-refresh-error-text');
    if (!form || !dashboard || !refreshButton || !retryButton || !error || !errorText) return;
    let refreshing = false;

    async function refresh() {
        const active = dashboard.querySelector('[data-dashboard-state]');
        if (!active || refreshing) return;
        refreshing = true;
        refreshButton.disabled = true;
        try {
            const query = new URLSearchParams({
                fromDate: active.dataset.fromDate,
                toDate: active.dataset.toDate,
                fragment: 'true'
            });
            const response = await fetch(`${form.action}?${query}`, { cache: 'no-store', headers: { 'X-Requested-With': 'fetch' } });
            if (!response.ok) throw new Error('Máy chủ chưa trả được số liệu mới. Vui lòng thử lại.');
            const html = await response.text();
            if (!html.includes('data-dashboard-state')) throw new Error('Phiên đăng nhập có thể đã hết hạn. Hãy đăng nhập lại rồi thử tải số liệu.');
            dashboard.innerHTML = html;
            error.hidden = true;
        } catch (e) {
            errorText.textContent = e instanceof Error ? e.message : 'Mất kết nối khi tải số liệu. Vui lòng thử lại.';
            error.hidden = false;
        } finally {
            refreshing = false;
            refreshButton.disabled = false;
        }
    }

    refreshButton.addEventListener('click', refresh);
    retryButton.addEventListener('click', refresh);
    window.setInterval(refresh, 30000);
})();
