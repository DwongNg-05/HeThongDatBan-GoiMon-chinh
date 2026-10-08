(() => {
    const board = document.getElementById('kitchen-board');
    const message = document.getElementById('kitchen-message');
    const labels = { Pending: ['Chờ bếp', 'bg-secondary'], Preparing: ['Đang chế biến', 'bg-warning text-dark'], Ready: ['Đã xong', 'bg-success'] };
    let busy = false;
    let fingerprint = '';
    function render(lines) {
        const key = JSON.stringify(lines);
        if (fingerprint === key) return;
        fingerprint = key;
        for (const [target, ready] of [['kitchen-queue', false], ['kitchen-ready', true]]) {
            const container = document.getElementById(target);
            container.replaceChildren();
            for (const line of lines.filter(item => (item.status === 'Ready') === ready)) {
                const card = document.createElement('article');
                card.className = 'card p-3 mb-2 d-flex flex-row gap-3 align-items-center flex-wrap';
                const text = document.createElement('span');
                text.textContent = `Bàn ${line.tableCode} · Phiếu #${line.batchId} · ${line.itemName} × ${line.quantity}${line.notes ? ' · ' + line.notes : ''}`;
                const badge = document.createElement('span');
                badge.className = 'badge ' + labels[line.status][1];
                badge.textContent = labels[line.status][0];
                card.append(text, badge);
                if (!ready && board.dataset.canEdit === 'true') {
                    const button = document.createElement('button');
                    button.type = 'button'; button.className = 'btn btn-primary';
                    button.textContent = line.status === 'Pending' ? 'Bắt đầu' : 'Xong';
                    button.addEventListener('click', () => transition(line, button));
                    card.append(button);
                }
                container.append(card);
            }
            if (!container.childElementCount) container.textContent = ready ? 'Chưa có món chờ mang ra.' : 'Không có món chờ chế biến.';
        }
    }
    async function refresh() {
        const response = await fetch('/Kitchen/Snapshot', { cache: 'no-store' });
        if (!response.ok || response.redirected) throw new Error('Không thể cập nhật. Kiểm tra kết nối hoặc đăng nhập lại.');
        render(await response.json());
    }
    async function transition(line, button) {
        if (busy) return;
        const nextLabel = line.status === 'Pending' ? 'Đang chế biến' : 'Đã xong';
        if (!window.confirm(`Chuyển ${line.itemName} × ${line.quantity} — bàn ${line.tableCode} sang “${nextLabel}”? Không thể chuyển lùi.`)) return;
        busy = true; button.disabled = true;
        try {
            const data = new URLSearchParams({ id: line.id, from: line.status, to: line.status === 'Pending' ? 'Preparing' : 'Ready', version: line.version,
                __RequestVerificationToken: document.querySelector('#kitchen-token input').value });
            const response = await fetch('/Kitchen/Transition', { method: 'POST', body: data });
            if (!response.ok || response.redirected) {
                const error = await response.json().catch(() => null);
                throw new Error(error?.message || 'Không thể chuyển trạng thái. Kiểm tra quyền hoặc đăng nhập lại.');
            }
            message.textContent = 'Đã cập nhật món.';
        } catch (error) { message.textContent = error.message; }
        finally {
            try { await refresh(); } catch (error) { message.textContent = error.message; }
            busy = false; button.disabled = false;
        }
    }
    async function poll() {
        if (!busy) {
            try { await refresh(); }
            catch (error) { message.textContent = error.message; }
        }
        setTimeout(poll, 1000);
    }
    poll();
})();
