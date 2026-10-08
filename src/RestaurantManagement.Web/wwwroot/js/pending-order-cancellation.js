(() => {
    const root = document.querySelector('[data-sent-orders]');
    if (!root) return;
    const area = root.querySelector('[data-order-lines]'), dialog = root.querySelector('dialog');
    const form = root.querySelector('[data-cancel-form]'), submit = form.querySelector('[data-cancel-submit]');
    const notice = root.querySelector('[data-order-message]'), error = form.querySelector('[data-cancel-message]');
    let selected, requestId, busy = false, polling = false, blocked = false, lines = [];
    const money = value => new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(value);
    const reasons = { ChangedMind: 'Khách đổi ý', Mistake: 'Gọi nhầm', SoldOut: 'Hết nguyên liệu', ManagerOverride: 'Quản lý huỷ · Vẫn tính tiền' };
    function node(tag, value, cls) {
        const element = document.createElement(tag);
        if (value !== undefined) element.textContent = value;
        if (cls) element.className = cls;
        return element;
    }
    function validity() { submit.disabled = busy || blocked || !form.checkValidity(); }
    function render() {
        area.replaceChildren();
        const sessions = Map.groupBy(lines, line => line.sessionId);
        if (!sessions.size) area.append(node('p', 'Chưa có món đã gửi bếp của bàn đang phục vụ.'));
        for (const [session, items] of sessions) {
            const section = node('section', undefined, 'mb-4');
            section.append(node('h2', `Bàn ${items[0].tableCode} · Phiên ${session}`));
            const subtotal = node('p', `Tạm tính: ${money(items[0].subtotal)}`, 'fw-bold');
            subtotal.dataset.sessionSubtotal = session;
            section.append(subtotal);
            const table = node('table', undefined, 'table table-bordered align-middle');
            const head = node('thead'), labels = node('tr');
            for (const label of ['Món', 'Số lượng', 'Đơn giá', 'Trạng thái', 'Thao tác']) labels.append(node('th', label));
            head.append(labels); table.append(head);
            const body = node('tbody');
            for (const item of items) {
                const row = node('tr'); row.dataset.orderItem = item.id; row.dataset.status = item.status;
                row.append(node('td', item.itemName), node('td', `${item.quantity} ${item.unit}`),
                    node('td', money(item.unitPrice)), node('td', item.statusLabel + (item.cancelReason ? ` · ${reasons[item.cancelReason] || item.cancelReason}` : '')));
                const action = node('td'), button = node('button', 'Huỷ món', 'btn btn-sm btn-outline-danger');
                button.type = 'button'; button.disabled = item.status !== 'Pending';
                if (button.disabled) action.append(node('small', item.status === 'Cancelled' ? 'Món đã huỷ.' : 'Chỉ được huỷ món còn chờ bếp.', 'd-block text-muted'));
                button.addEventListener('click', () => {
                    selected = item; requestId = crypto.randomUUID(); blocked = false; form.reset(); error.textContent = '';
                    form.elements.quantity.value = item.quantity; form.elements.quantity.max = item.quantity;
                    root.querySelector('[data-cancel-name]').textContent = `${item.itemName} · ${item.quantity} ${item.unit}`;
                    validity(); dialog.showModal();
                });
                action.append(button); row.append(action); body.append(row);
            }
            table.append(body); section.append(table); area.append(section);
        }
    }
    async function refresh() {
        if (polling) return;
        polling = true;
        try {
            const response = await fetch('/Ordering/Sent/Snapshot', { cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' }, signal: AbortSignal.timeout(4000) });
            if (!response.ok) throw new Error('Không tải được trạng thái mới nhất. Vui lòng thử lại.');
            lines = await response.json(); render();
        } catch (ex) { notice.textContent = ex.message; }
        finally { polling = false; }
    }
    form.addEventListener('input', validity); form.addEventListener('change', validity);
    root.querySelector('[data-cancel-close]').addEventListener('click', () => { if (!busy) dialog.close(); });
    dialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
    form.addEventListener('submit', async event => {
        event.preventDefault(); if (busy || blocked || !selected || !form.reportValidity()) return;
        busy = true; validity(); error.textContent = '';
        const data = new FormData(form); data.set('requestId', requestId);
        try {
            const response = await fetch(`/Ordering/Sent/${selected.id}/Cancel`, {
                method: 'POST', body: data, headers: { 'X-Requested-With': 'XMLHttpRequest' }, signal: AbortSignal.timeout(10000)
            });
            const result = await response.json();
            if (!response.ok) { error.textContent = result.message || 'Không huỷ được món.'; }
            else { notice.textContent = result.message; dialog.close(); }
            await refresh();
            if (!response.ok) {
                const latest = lines.find(line => line.id === selected.id);
                if (!latest || latest.status !== 'Pending') { blocked = true; form.elements.quantity.disabled = true; form.elements.reason.disabled = true; }
                else { form.elements.quantity.max = latest.quantity; }
            }
        } catch { error.textContent = 'Chưa xác nhận được kết quả. Bạn có thể thử lại; cùng yêu cầu sẽ không huỷ thêm.'; }
        finally { busy = false; validity(); }
    });
    // Re-enable controls when opening another cancellation after a conflict.
    dialog.addEventListener('close', () => { form.elements.quantity.disabled = false; form.elements.reason.disabled = false; });
    refresh(); setInterval(refresh, 2000);
})();
