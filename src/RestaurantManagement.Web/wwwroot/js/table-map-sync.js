(() => {
    'use strict';
    const root = document.querySelector('.snapshot-map');
    if (!root || root.dataset.loadFailed === 'true') return;
    const notice = root.querySelector('.map-sync-notice');
    const clock = root.querySelector('.map-sync-time time');
    const cards = new Map(Array.from(root.querySelectorAll('[data-table-code]'), card => [card.dataset.tableCode, card]));
    const statuses = { Available: ['Trống', 'available'], Reserved: ['Đã đặt trước', 'reserved'], Serving: ['Đang phục vụ', 'serving'], Cleaning: ['Đang dọn', 'cleaning'] };
    const highlights = new Map();
    let cursor = root.dataset.cursor;
    let pending = null;
    let stopped = false;
    let generation = 0;

    function warn(message = 'Mất kết nối. Dữ liệu có thể đã cũ. Hệ thống sẽ tự thử lại.') {
        notice.textContent = message;
        notice.hidden = false;
    }
    function apply(table) {
        const card = cards.get(table.code);
        if (!card) return;
        const [label, css] = statuses[table.status];
        if (card.dataset.status === table.status) return;
        for (const [, statusClass] of Object.values(statuses)) card.classList.remove('status-' + statusClass);
        card.classList.add('status-' + css);
        card.dataset.status = table.status;
        card.querySelector('.table-status-label').textContent = label;
        card.setAttribute('aria-label', `Bàn ${table.code}, ${table.capacity} chỗ, ${label}`);
        card.classList.remove('table-just-changed');
        void card.offsetWidth;
        card.classList.add('table-just-changed');
        clearTimeout(highlights.get(card));
        highlights.set(card, setTimeout(() => { card.classList.remove('table-just-changed'); highlights.delete(card); }, 3000));
        const area = card.closest('.area-section');
        area.querySelector('[data-area-available]').textContent = String(Array.from(area.querySelectorAll('[data-table-code]')).filter(item => item.dataset.status === 'Available').length);
        root.dispatchEvent(new CustomEvent('tablemap:changed', { detail: { code: table.code } }));
    }
    async function sync() {
        if (stopped || document.hidden || pending) return;
        if (!navigator.onLine) { warn(); return; }
        const request = new AbortController();
        const revision = generation;
        pending = request;
        const timeout = setTimeout(() => request.abort(), 1800);
        try {
            const response = await fetch(root.dataset.changesUrl + '?after=' + encodeURIComponent(cursor), { signal: request.signal, cache: 'no-store', credentials: 'same-origin', headers: { Accept: 'application/json' } });
            if (response.redirected || response.status === 401 || response.status === 403) {
                stopped = true;
                warn('Phiên đăng nhập hoặc quyền truy cập đã thay đổi. Vui lòng đăng nhập lại.');
                return;
            }
            if (response.status === 409) { stopped = true; warn('Dữ liệu đã được đặt lại. Vui lòng tải lại sơ đồ.'); return; }
            if (!response.ok) throw new Error('Synchronization failed');
            const changes = await response.json();
            if (revision !== generation || document.hidden) return;
            if (!/^\d+$/.test(changes.cursor) || !Array.isArray(changes.tables) || changes.tables.some(table => !statuses[table.status]) || !Number.isFinite(Date.parse(changes.syncedAtUtc))) throw new Error('Invalid synchronization response');
            changes.tables.forEach(apply);
            cursor = changes.cursor;
            clock.dateTime = changes.syncedAtUtc;
            clock.textContent = new Date(changes.syncedAtUtc).toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' });
            notice.hidden = true;
        } catch {
            if (revision === generation && !document.hidden) warn();
        } finally {
            clearTimeout(timeout);
            if (pending === request) pending = null;
        }
    }
    function cancel() { generation++; if (pending) pending.abort(); pending = null; }
    document.addEventListener('visibilitychange', () => { cancel(); if (!document.hidden) void sync(); });
    window.addEventListener('offline', () => { cancel(); warn(); });
    window.addEventListener('online', () => { cancel(); void sync(); });
    const interval = setInterval(() => void sync(), 2000);
    window.addEventListener('pagehide', () => { stopped = true; cancel(); clearInterval(interval); });
    window.addEventListener('pageshow', event => { if (event.persisted) window.location.reload(); });
    void sync();
})();
