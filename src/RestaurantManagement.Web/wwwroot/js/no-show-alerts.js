(() => {
    const root = document.getElementById('no-show-alerts');
    if (!root) return;
    const list = root.querySelector('[data-no-show-items]');
    const extended = root.querySelector('[data-extended-holds]');
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
    async function act(item, url, prompt, withHistory) {
        if (busy || !confirm(prompt)) return;
        busy = true;
        root.querySelectorAll('[data-no-show-items] button, [data-extended-holds] button').forEach(b => b.disabled = true);
        try {
            const body = new URLSearchParams({ id: item.id,
                __RequestVerificationToken: root.querySelector('[name="__RequestVerificationToken"]').value });
            const result = await json(url, { method: 'POST', body });
            message.textContent = result.message;
            if (withHistory) await showHistory(item.id);
        } catch (error) { message.textContent = error.message; }
        finally { busy = false; document.dispatchEvent(new Event('reservations-changed')); await load(); }
    }
    async function load() {
        clearTimeout(timer);
        if (reading) return;
        if (busy) { timer = setTimeout(load, 1000); return; }
        reading = true;
        try {
            const items = await json(root.dataset.alertsUrl);
            if (busy) return;
            const ids = items.map(item => `${item.id}:${item.isOverdue}:${item.holdUntil}:${item.canExtend}`).join(',');
            if (previousIds !== undefined && previousIds !== ids)
                document.dispatchEvent(new Event('reservations-changed'));
            previousIds = ids;
            list.replaceChildren();
            extended.replaceChildren();
            document.querySelectorAll('[data-no-show-badge], [data-hold-badge]').forEach(el => el.remove());
            if (!items.some(item => item.isOverdue)) { const p = document.createElement('p'); p.textContent = 'Không có lượt đặt cần cảnh báo.'; list.append(p); }
            for (const item of items) {
                const box = document.createElement('div'); box.className = item.isOverdue ? 'alert alert-warning' : 'alert alert-info';
                box.dataset.reservationId = item.id;
                const text = document.createElement('p'); text.textContent = `${item.code} · ${item.customerName} · Bàn ${item.tableCode} · Giờ hẹn ${item.appointment}`;
                box.append(text);
                if (item.holdUntil) {
                    const deadline = document.createElement('p');
                    deadline.textContent = `${item.isOverdue ? 'Đã hết hạn gia hạn' : 'Đang gia hạn'} · Giữ đến ${item.holdUntil} (giờ Việt Nam) · Đã gia hạn 1/1 lần.`;
                    box.append(deadline);
                }
                if (item.isOverdue) box.append(button('Đánh dấu khách không tới', () => act(item, root.dataset.markUrl,
                    `Xác nhận khách của lượt ${item.code} không tới và giải phóng lượt giữ bàn?`, true)));
                if (item.canExtend) box.append(button('Gia hạn 15 phút', () => act(item, root.dataset.extendUrl,
                    `Gia hạn lượt ${item.code} đến ${item.extensionDeadline} (giờ hẹn +30 phút)? Chỉ được gia hạn một lần.`, false)));
                else {
                    const disabled = button(item.extensionCount > 0 ? 'Đã gia hạn 1/1 lần' : 'Đã quá mốc gia hạn', () => {});
                    disabled.disabled = true;
                    box.append(disabled);
                }
                box.append(button('Xem lịch sử số điện thoại', () => showHistory(item.id).catch(e => message.textContent = e.message)));
                (item.isOverdue ? list : extended).append(box);
                document.querySelectorAll('[data-table-code]').forEach(card => {
                    if (card.dataset.tableCode !== item.tableCode) return;
                    const badge = document.createElement('span');
                    if (item.isOverdue) badge.dataset.noShowBadge = ''; else badge.dataset.holdBadge = '';
                    badge.className = item.isOverdue ? 'badge bg-danger' : 'badge bg-info text-dark';
                    badge.textContent = item.isOverdue
                        ? (item.extensionCount > 0 ? 'Đã hết hạn gia hạn' : 'Khách trễ từ 15 phút')
                        : `Giữ đến ${item.holdUntil} · Đã gia hạn 1/1`;
                    card.append(badge);
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
