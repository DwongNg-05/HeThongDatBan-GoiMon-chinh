(() => {
    const config = document.querySelector('[data-cleaning-config]');
    if (!config) return;
    const message = config.querySelector('[data-cleaning-message]');
    const cards = new Map();
    let warnings = new Map();
    function updateWarning(card, badge) {
        const warning = warnings.get(card.dataset.tableCode);
        const visible = card.dataset.status === 'Cleaning' && warning?.overdue === true
            && Date.parse(warning.startedAtUtc) >= Date.parse(card.dataset.changedAt);
        badge.hidden = !visible;
        card.classList.toggle('has-cleaning-warning', Boolean(visible));
        if (visible) badge.textContent = 'Cảnh báo: Cần dọn quá 10 phút';
    }
    document.querySelectorAll('.table-card[data-table-code]').forEach(card => {
        const badge = document.createElement('div');
        badge.className = 'cleaning-warning'; badge.dataset.cleaningWarning = '';
        badge.setAttribute('role', 'status'); badge.hidden = true;
        cards.set(card, badge); card.append(badge);
        const guest = document.createElement('div');
        guest.className = 'table-upcoming-guest'; guest.dataset.upcomingGuestLabel = '';
        guest.setAttribute('role', 'status'); card.append(guest);
        const button = document.createElement('button');
        button.type = 'button'; button.className = 'btn btn-outline-success btn-sm mt-2';
        button.dataset.completeCleaning = ''; button.textContent = 'Báo đã dọn xong';
        const update = () => {
            button.hidden = card.dataset.status !== 'Cleaning';
            updateWarning(card, badge);
            guest.hidden = card.dataset.status !== 'Reserved' || !card.dataset.upcomingGuest;
            const time = Date.parse(card.dataset.upcomingStartsAt);
            guest.textContent = guest.hidden ? '' : `Khách sắp tới: ${card.dataset.upcomingGuest}`
                + (Number.isFinite(time) ? ` · ${new Date(time).toLocaleTimeString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', hour: '2-digit', minute: '2-digit' })}` : '');
        };
        const feedback = document.createElement('small');
        feedback.setAttribute('role', 'status'); feedback.className = 'd-block text-danger';
        update(); card.append(button, feedback);
        new MutationObserver(update).observe(card, { attributes: true, attributeFilter: ['data-status', 'data-changed-at', 'data-upcoming-guest', 'data-upcoming-starts-at'] });
        button.addEventListener('click', async event => {
            event.stopPropagation(); button.disabled = true; message.textContent = ''; feedback.textContent = '';
            try {
                const response = await fetch(`${config.dataset.url}/${encodeURIComponent(card.dataset.tableCode)}/complete`, {
                    method: 'POST', body: new URLSearchParams({ __RequestVerificationToken: config.querySelector('[name=__RequestVerificationToken]').value })
                });
                if (response.redirected) throw new Error('Phiên đăng nhập đã hết. Vui lòng đăng nhập lại.');
                const data = await response.json();
                if (!response.ok) throw new Error(data.message || 'Không thể báo đã dọn xong.');
                message.textContent = data.message;
                warnings.delete(card.dataset.tableCode);
                updateWarning(card, badge);
            } catch (error) { feedback.textContent = error.message || 'Không kết nối được máy chủ. Vui lòng thử lại.'; }
            finally {
                window.dispatchEvent(new Event('cleaning-completed'));
                button.disabled = false;
            }
        });
    });
    let pending = false;
    const error = config.querySelector('[data-cleaning-sync-error]');
    async function syncWarnings() {
        if (pending || document.hidden) return;
        pending = true;
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 1800);
        try {
            const response = await fetch(`${config.dataset.url}/warnings`, { cache: 'no-store', signal: controller.signal });
            if (!response.ok || response.redirected) throw new Error();
            const data = await response.json();
            warnings = new Map(data.map(item => [item.code, item]));
            cards.forEach((badge, card) => updateWarning(card, badge));
            error.hidden = true;
        } catch {
            error.textContent = 'Chưa đồng bộ được cảnh báo dọn bàn. Dữ liệu có thể đã cũ; đang thử lại.';
            error.hidden = false;
        } finally { clearTimeout(timeout); pending = false; }
    }
    const timer = setInterval(syncWarnings, 2000);
    window.addEventListener('cleaning-completed', syncWarnings);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) syncWarnings(); });
    window.addEventListener('online', syncWarnings);
    window.addEventListener('pagehide', () => clearInterval(timer));
    syncWarnings();
})();
