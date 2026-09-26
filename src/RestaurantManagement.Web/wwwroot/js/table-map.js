(() => {
    const page = document.querySelector('.map-page');
    if (!page) return;

    const apiUrl = page.dataset.statusApi;
    const connectionLabel = document.querySelector('#table-map-connection-label');
    const connectionIndicator = document.querySelector('#table-map-connection');
    const lastUpdate = document.querySelector('#table-map-last-update');
    const statusClasses = ['available', 'reserved', 'serving', 'cleaning', 'unknown'];
    const statusLabels = {
        Available: 'Trống',
        Reserved: 'Đã đặt trước',
        Serving: 'Đang phục vụ',
        Cleaning: 'Đang dọn',
        Unknown: 'Không xác định'
    };

    function applyStatus(update) {
        const card = [...document.querySelectorAll('[data-table-code]')]
            .find(item => item.dataset.tableCode === update.code);
        if (!card) return;

        const incomingTime = Date.parse(update.changedAtUtc);
        const currentTime = Date.parse(card.dataset.changedAt || '1970-01-01T00:00:00Z');
        if (Number.isFinite(incomingTime) && incomingTime < currentTime) return;

        const status = statusLabels[update.status] ? update.status : 'Unknown';
        const cssClass = status === 'Unknown' ? 'unknown' : status.toLowerCase();
        card.classList.remove(...statusClasses.map(value => `status-${value}`));
        card.classList.add(`status-${cssClass}`);
        card.dataset.status = status;
        card.dataset.changedAt = update.changedAtUtc;
        card.setAttribute('aria-label', `Bàn ${update.code}, sức chứa ${update.capacity} người, trạng thái ${statusLabels[status]}`);
        card.querySelector('.table-status-label').textContent = statusLabels[status];
        const editor = card.querySelector('[data-status-editor]');
        if (editor) editor.value = status === 'Unknown' ? '' : status;

        const available = [...document.querySelectorAll('[data-table-code]')]
            .filter(item => item.dataset.status === 'Available').length;
        const availableCount = document.querySelector('#available-table-count');
        if (availableCount) availableCount.textContent = available;

        if (lastUpdate) {
            const time = new Date(update.changedAtUtc);
            lastUpdate.textContent = `· Cập nhật bàn ${update.code} lúc ${time.toLocaleTimeString('vi-VN')}`;
        }
    }

    async function refreshSnapshot() {
        const response = await fetch(apiUrl, { cache: 'no-store' });
        if (!response.ok) throw new Error('Không thể đồng bộ sơ đồ bàn.');
        const tables = await response.json();
        tables.forEach(applyStatus);
    }

    const events = new EventSource(`${apiUrl}/stream`);
    events.addEventListener('open', () => {
        connectionLabel.textContent = 'Đang đồng bộ trực tiếp';
        connectionIndicator.classList.add('connected');
        refreshSnapshot().catch(() => {
            connectionLabel.textContent = 'Đã kết nối; chưa đồng bộ được sơ đồ';
        });
    });
    events.addEventListener('statusChanged', event => {
        try {
            applyStatus(JSON.parse(event.data));
        } catch {
            connectionLabel.textContent = 'Nhận được cập nhật không hợp lệ';
        }
    });
    events.addEventListener('error', () => {
        connectionLabel.textContent = 'Mất kết nối, đang tự kết nối lại…';
        connectionIndicator.classList.remove('connected');
    });

    document.querySelectorAll('[data-status-editor]').forEach(editor => {
        editor.addEventListener('change', async () => {
            const card = editor.closest('[data-table-code]');
            const previous = card.dataset.status;
            editor.disabled = true;
            try {
                const response = await fetch(`${apiUrl}/${encodeURIComponent(card.dataset.tableCode)}`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ status: editor.value })
                });
                if (!response.ok) throw new Error('Không cập nhật được trạng thái bàn.');
                applyStatus(await response.json());
                connectionLabel.textContent = 'Đang đồng bộ trực tiếp';
            } catch {
                editor.value = previous;
                connectionLabel.textContent = 'Chưa gửi được cập nhật; hãy thử lại';
            } finally {
                editor.disabled = false;
            }
        });
    });
})();
