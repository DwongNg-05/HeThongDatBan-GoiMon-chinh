(() => {
    const root = document.getElementById('order-workflow');
    if (!root) return;
    const kitchen = root.dataset.kitchen === 'true';
    const list = document.getElementById('order-list');
    const message = document.getElementById('order-message');
    const dialog = document.getElementById('order-cancel-dialog');
    const confirm = document.getElementById('order-cancel-confirm');
    const reason = document.getElementById('order-reason');
    const labels = { Pending: 'Chờ bếp', Preparing: 'Đang chế biến', Ready: 'Đã xong', Served: 'Đã phục vụ', Cancelled: 'Đã huỷ' };
    let selected;
    let posting = false;
    let version = 0;
    let timer;
    const money = value => new Intl.NumberFormat('vi-VN').format(value) + ' ₫';
    function node(tag, text, css) {
        const element = document.createElement(tag);
        if (text !== undefined) element.textContent = text;
        if (css) element.className = css;
        return element;
    }
    function button(text, action) {
        const element = node('button', text, 'btn btn-outline-danger btn-sm');
        element.type = 'button'; element.addEventListener('click', action); return element;
    }
    async function refresh() {
        clearTimeout(timer);
        const current = ++version;
        try {
            const response = await fetch(`${root.dataset.snapshot}?kitchen=${kitchen}`, { cache: 'no-store' });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('Không tải được danh sách. Nhấn Tải lại.');
            const data = await response.json();
            if (current !== version) return;
            list.replaceChildren();
            if (!data.items.length) list.append(node('p', kitchen ? 'Hàng đợi bếp trống.' : 'Chưa có món trong các phiên đang phục vụ.', 'my-3'));
            for (const session of data.sessions) {
                const section = node('section', undefined, 'my-4');
                const items = data.items.filter(item => item.sessionId === session.id);
                section.append(node('h2', `Phiên ${session.id} · Bàn ${[...new Set(items.map(item => item.tableCode))].join(', ')}`, 'h5'));
                if (!kitchen) section.append(node('p', `Tạm tính: ${money(session.subtotal)}`, 'fw-bold'));
                const wrapper = node('div', undefined, 'table-responsive');
                const table = node('table', undefined, 'table table-bordered');
                const head = node('thead'); const heading = node('tr');
                for (const text of ['Món', 'Số lượng', 'Đơn giá', 'Thành tiền', 'Trạng thái', 'Thao tác']) heading.append(node('th', text));
                head.append(heading); table.append(head);
                const body = node('tbody');
                for (const item of items) {
                    const row = node('tr');
                    for (const value of [item.itemName, item.quantity, money(item.unitPrice),
                        money(item.status === 'Cancelled' && !item.chargeWhenCancelled ? 0 : item.lineTotal), labels[item.status] || item.status]) row.append(node('td', value));
                    const actions = node('td');
                    if (!kitchen && root.dataset.canCancel === 'true' && item.status !== 'Cancelled') {
                        const cancel = button('Huỷ món', () => {
                            selected = item.id; reason.value = ''; confirm.disabled = false;
                            document.getElementById('order-cancel-name').textContent = `${item.itemName} · ${item.quantity} phần · ${money(item.lineTotal)}`;
                            dialog.showModal();
                        });
                        cancel.disabled = item.status !== 'Pending' || item.sessionStatus !== 'Open';
                        actions.append(cancel);
                        if (cancel.disabled) actions.append(node('p', item.status !== 'Pending' ? 'Món đã bắt đầu chế biến hoặc đã phục vụ; nhân viên không được huỷ.' : 'Phiên đang thanh toán; không thể huỷ.', 'small text-muted mb-0'));
                    }
                    if (kitchen && root.dataset.canStart === 'true' && item.status === 'Pending')
                        actions.append(button('Bắt đầu chế biến', () => mutate(root.dataset.start, item.id)));
                    row.append(actions); body.append(row);
                }
                table.append(body); wrapper.append(table); section.append(wrapper); list.append(section);
            }
        } catch (error) {
            if (current === version) message.textContent = error.message;
        } finally {
            if (current === version) timer = setTimeout(refresh, 2000);
        }
    }
    async function mutate(url, id, cancellationReason) {
        if (posting) return;
        posting = true; confirm.disabled = true;
        ++version; clearTimeout(timer);
        try {
            const body = new URLSearchParams({ id, __RequestVerificationToken: root.querySelector('input[name="__RequestVerificationToken"]').value });
            if (cancellationReason) body.set('reason', cancellationReason);
            const response = await fetch(url, { method: 'POST', body });
            const data = response.headers.get('content-type')?.includes('application/json') ? await response.json() : { message: 'Thao tác bị từ chối hoặc phiên đăng nhập đã hết hạn.' };
            message.textContent = data.message;
            if (dialog.open) dialog.close();
        } catch {
            message.textContent = 'Chưa xác nhận được kết quả. Tải lại để kiểm tra trước khi thử lại.';
        } finally {
            posting = false; confirm.disabled = false; await refresh();
        }
    }
    document.getElementById('order-cancel-form').addEventListener('submit', event => {
        event.preventDefault();
        if (!reason.value) { reason.reportValidity(); return; }
        mutate(root.dataset.cancel, selected, reason.value);
    });
    document.getElementById('order-cancel-close').addEventListener('click', () => { if (!posting) dialog.close(); });
    document.getElementById('order-refresh').addEventListener('click', () => { if (!posting) refresh(); });
    refresh();
})();
