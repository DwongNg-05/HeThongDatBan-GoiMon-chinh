(() => {
    const page = document.querySelector('.map-page');
    if (!page) return;

    const apiUrl = page.dataset.statusApi;
    const detailsUrl = page.dataset.detailsApi;
    const detailsDialog = document.querySelector('#table-details-dialog');
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
    let selectedDetailCode = null;

    const byId = id => document.getElementById(id);
    const formattedTime = value => value
        ? new Date(value).toLocaleString('vi-VN', { dateStyle: 'medium', timeStyle: 'short' })
        : 'Chưa có dữ liệu';

    async function refreshDetails(code) {
        const response = await fetch(`${detailsUrl}/${encodeURIComponent(code)}`, { cache: 'no-store' });
        if (!response.ok) throw new Error('Không tải được thông tin bàn.');
        const details = await response.json();
        if (!detailsDialog.open || selectedDetailCode !== code) return;

        byId('table-details-title').textContent = `Bàn ${details.code}`;
        byId('details-area').textContent = `${details.area} · ${details.capacity} chỗ`;
        const badge = byId('details-status');
        document.querySelector('.details-status-row').className = `details-status-row status-${details.statusClass}`;
        badge.className = 'table-status';
        byId('details-status-label').textContent = details.statusLabel;

        const reservation = details.upcomingReservation;
        const serving = details.status === 'Serving';
        const reserved = details.status === 'Reserved';
        const cleaning = details.status === 'Cleaning';
        const personHeading = byId('details-person-heading');
        personHeading.textContent = serving ? 'Khách đang ngồi' : reserved ? 'Khách đặt bàn' : 'Khách hiện tại';
        byId('details-person').textContent = serving
            ? (details.currentGuestName || 'Khách vãng lai')
            : reserved && reservation ? reservation.customerName : 'Chưa có khách';
        const personNote = serving
            ? [details.currentGuestPhone, details.currentGuestCount ? `${details.currentGuestCount} khách` : null].filter(Boolean).join(' · ')
            : reserved && reservation ? `${reservation.phone} · ${reservation.guestCount} khách` : '';
        byId('details-person-note').textContent = personNote;

        byId('details-started').textContent = serving && details.serviceStartedAtUtc
            ? formattedTime(details.serviceStartedAtUtc)
            : 'Chưa bắt đầu';
        byId('details-duration').textContent = serving && details.serviceElapsedMinutes != null
            ? `Đã phục vụ ${details.serviceElapsedMinutes} phút`
            : 'Không có phiên phục vụ';

        byId('details-subtotal').textContent = details.currentSubtotal == null
            ? '—'
            : new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 }).format(details.currentSubtotal);
        byId('details-subtotal-note').textContent = details.currentSubtotal == null
            ? 'Chưa có phiên hoặc phát sinh'
            : 'Tổng món hiện tại, chưa trừ giảm giá';

        byId('details-context').textContent = serving
            ? 'Bàn đang phục vụ khách.'
            : reserved
                ? 'Bàn đã giữ cho lượt đặt sắp tới.'
                : cleaning
                    ? 'Bàn đang được dọn; chưa có phiên phục vụ hiện tại.'
                    : 'Bàn đang trống.';

        const reservationPanel = byId('details-reservation');
        reservationPanel.hidden = !reservation;
        if (reservation) {
            byId('details-reservation-name').textContent = reservation.customerName;
            byId('details-reservation-phone').textContent = reservation.phone;
            byId('details-reservation-guests').textContent = `${reservation.guestCount} khách`;
            byId('details-reservation-time').textContent = formattedTime(reservation.startsAtUtc);
        }
        byId('details-source').hidden = !details.isDemoData;
    }

    async function openDetails(code) {
        selectedDetailCode = code;
        byId('table-details-title').textContent = `Bàn ${code}`;
        byId('details-context').textContent = 'Đang tải thông tin…';
        byId('details-reservation').hidden = true;
        if (!detailsDialog.open) detailsDialog.showModal();
        try {
            await refreshDetails(code);
        } catch {
            byId('details-context').textContent = 'Không tải được thông tin. Vui lòng thử lại.';
        }
    }

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
            const update = JSON.parse(event.data);
            applyStatus(update);
            if (detailsDialog.open && selectedDetailCode === update.code)
                refreshDetails(update.code).catch(() => {});
        } catch {
            connectionLabel.textContent = 'Nhận được cập nhật không hợp lệ';
        }
    });

    document.querySelectorAll('[data-open-details]').forEach(button => {
        button.addEventListener('click', () => openDetails(button.dataset.openDetails));
    });
    document.querySelector('[data-close-details]')?.addEventListener('click', () => detailsDialog.close());
    detailsDialog.addEventListener('click', event => {
        if (event.target === detailsDialog) detailsDialog.close();
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
