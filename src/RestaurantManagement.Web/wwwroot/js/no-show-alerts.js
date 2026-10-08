(() => {
    const root = document.getElementById('no-show-alerts');
    if (!root) return;
    const list = root.querySelector('[data-no-show-items]');
    const message = root.querySelector('[data-no-show-message]');
    const history = root.querySelector('[data-no-show-history]');
    let timer, busy = false, reading = false, previousIds;
    const button = (text, action) => {
        const b = document.createElement('button'); b.type = 'button';
        b.className = 'btn btn-outline-danger btn-sm me-2'; b.textContent = text;
        b.addEventListener('click', action); return b;
    };
    async function json(url, options = {}) {
        const response = await fetch(url, { cache: 'no-store', signal: AbortSignal.timeout(10000), ...options });
        if (!response.headers.get('content-type')?.includes('application/json')) throw new Error('Phiên đăng nhập hết hạn hoặc kết nối lỗi. Vui lòng đăng nhập lại.');
        const value = await response.json();
        if (!response.ok) throw new Error(value.message || 'Không thể xử lý. Vui lòng thử lại.');
        return value;
    }
    async function showHistory(id) {
        const items = await json(`${root.dataset.historyUrl}?id=${id}`);
        renderHistory(items);
    }
    function renderHistory(items) {
        history.replaceChildren();
        const title = document.createElement('h3'); title.className = 'h6';
        title.textContent = `Lịch sử không tới của cùng số điện thoại: ${items.length} lần`;
        history.append(title);
        for (const item of items) {
            const p = document.createElement('p');
            p.textContent = `${item.code} · Giờ hẹn: ${item.appointment} · Ghi nhận: ${item.recordedAt} (giờ Việt Nam)`;
            history.append(p);
        }
    }
    async function load() {
        clearTimeout(timer);
        if (reading) return;
        if (busy) { timer = setTimeout(load, 1000); return; }
        reading = true;
        try {
            const items = await json(root.dataset.alertsUrl);
            if (busy) return;
            const ids = items.map(item => item.id).join(',');
            if (previousIds !== undefined && previousIds !== ids)
                document.dispatchEvent(new Event('reservations-changed'));
            previousIds = ids;
            list.replaceChildren();
            document.querySelectorAll('[data-no-show-badge]').forEach(el => el.remove());
            if (!items.length) { const p = document.createElement('p'); p.textContent = 'Không có lượt đặt cần cảnh báo.'; list.append(p); }
            for (const item of items) {
                const box = document.createElement('div'); box.className = 'alert alert-warning';
                const text = document.createElement('p'); text.textContent = `${item.code} · ${item.customerName} · Bàn ${item.tableCode} · Giờ hẹn ${item.appointment}`;
                box.append(text);
                box.append(button('Đánh dấu khách không tới', async event => {
                    if (!confirm(`Xác nhận khách của lượt ${item.code} không tới và giải phóng lượt giữ bàn?`)) return;
                    busy = true; event.target.disabled = true;
                    try {
                        const body = new URLSearchParams({ id: item.id, __RequestVerificationToken: root.querySelector('[name="__RequestVerificationToken"]').value });
                        const result = await json(root.dataset.markUrl, { method: 'POST', body });
                        message.textContent = result.message;
                        await showHistory(item.id);
                    } catch (error) { message.textContent = error.message; }
                    finally { busy = false; document.dispatchEvent(new Event('reservations-changed')); await load(); }
                }));
                box.append(button('Xem lịch sử số điện thoại', () => showHistory(item.id).catch(e => message.textContent = e.message)));
                list.append(box);
                document.querySelectorAll('[data-table-code]').forEach(card => {
                    if (card.dataset.tableCode !== item.tableCode) return;
                    const badge = document.createElement('span'); badge.dataset.noShowBadge = ''; badge.className = 'badge bg-danger';
                    badge.textContent = 'Khách trễ từ 15 phút'; card.append(badge);
                });
            }
        } catch (error) { message.textContent = 'Không cập nhật được cảnh báo: ' + error.message; }
        finally { reading = false; timer = setTimeout(load, 5000); }
    }
    document.addEventListener('visibilitychange', () => { if (!document.hidden) load(); });
    document.addEventListener('no-show-history', event => showHistory(event.detail).catch(e => message.textContent = e.message));
    const phoneForm = root.querySelector('[data-phone-history-form]');
    phoneForm.addEventListener('submit', async event => {
        event.preventDefault();
        const body = new URLSearchParams({ phone: phoneForm.elements.phone.value,
            __RequestVerificationToken: root.querySelector('[name="__RequestVerificationToken"]').value });
        try { renderHistory(await json(phoneForm.action, { method: 'POST', body })); }
        catch (error) { message.textContent = error.message; }
    });
    load();
})();
