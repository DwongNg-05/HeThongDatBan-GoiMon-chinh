(() => {
    const page = document.querySelector('.map-page');
    if (!page) return;

    const apiUrl = page.dataset.statusApi;
    const detailsUrl = page.dataset.detailsApi;
    const expectedTableCount = Number(page.dataset.renderedTableCount || 0);
    const detailsDialog = document.querySelector('#table-details-dialog');
    const detailsOverlay = document.querySelector('#table-details-overlay');
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
    let detailOpener = null;

    // The map is server-rendered. Measure until two paint opportunities after its
    // complete card grid is in the DOM so a console trace reflects visible readiness.
    requestAnimationFrame(() => requestAnimationFrame(() => {
        const renderedTableCount = document.querySelectorAll('[data-table-code]').length;
        const navigation = performance.getEntriesByType('navigation')[0];
        if (!navigation || renderedTableCount !== expectedTableCount) return;
        const visibleAt = performance.now();
        performance.measure('table-map-visible', { start: navigation.startTime, end: visibleAt });
        const duration = Math.round(performance.getEntriesByName('table-map-visible').at(-1).duration);
        document.documentElement.dataset.tableMapVisibleMs = String(duration);
        console.info(`[table-map] ${renderedTableCount} tables visible in ${duration} ms`);
    }));

    const byId = id => document.getElementById(id);
    const formattedTime = value => value
        ? new Date(value).toLocaleString('vi-VN', { dateStyle: 'medium', timeStyle: 'short' })
        : 'Chưa có dữ liệu';

    async function refreshDetails(code) {
        const response = await fetch(`${detailsUrl}/${encodeURIComponent(code)}`, { cache: 'no-store' });
        if (!response.ok) throw new Error('Không tải được thông tin bàn.');
        const details = await response.json();
        if (detailsOverlay.hidden || selectedDetailCode !== code) return;

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

        // S3-04 Task 4: bàn đang có phiên phục vụ (dữ liệu thật) → nút mở hoá đơn tạm tính của bàn.
        const billLink = byId('details-bill-link');
        if (billLink) {
            const hasBill = details.hasActiveSession && !details.isDemoData;
            billLink.hidden = !hasBill;
            billLink.href = hasBill ? `${billLink.dataset.billUrl}${encodeURIComponent(details.code)}` : '#';
        }
    }

    async function openDetails(code) {
        selectedDetailCode = code;
        detailOpener = [...document.querySelectorAll('[data-open-details]')]
            .find(button => button.dataset.openDetails === code) ?? null;
        byId('table-details-title').textContent = `Bàn ${code}`;
        byId('details-context').textContent = 'Đang tải thông tin…';
        byId('details-reservation').hidden = true;
        if (byId('details-bill-link')) byId('details-bill-link').hidden = true;
        detailsOverlay.hidden = false;
        byId('details-close-button').focus();
        try {
            await refreshDetails(code);
        } catch {
            byId('details-context').textContent = 'Không tải được thông tin. Vui lòng thử lại.';
        }
    }

    function closeDetails() {
        detailsOverlay.hidden = true;
        selectedDetailCode = null;
        detailOpener?.focus();
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
            refreshAlertsSoon();
            if (!detailsOverlay.hidden && selectedDetailCode === update.code)
                refreshDetails(update.code).catch(() => {});
        } catch {
            connectionLabel.textContent = 'Nhận được cập nhật không hợp lệ';
        }
    });

    document.querySelectorAll('[data-open-details]').forEach(button => {
        button.addEventListener('click', () => openDetails(button.dataset.openDetails));
    });
    document.querySelector('[data-close-details]')?.addEventListener('click', closeDetails);
    detailsOverlay.addEventListener('click', event => {
        if (event.target === detailsOverlay) closeDetails();
    });
    detailsOverlay.addEventListener('keydown', event => {
        if (event.key === 'Escape') {
            event.preventDefault();
            closeDetails();
        }
        if (event.key !== 'Tab') return;
        const focusable = [...detailsDialog.querySelectorAll('button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])')];
        if (!focusable.length) { event.preventDefault(); detailsDialog.focus(); return; }
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
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
    // ---------- S3-09 Task 1: cảnh báo đặt bàn trễ 15 phút + "Đánh dấu khách không tới" ----------
    // Tải cảnh báo ngay, rồi tải lại đúng lúc lượt kế tiếp đạt mốc (theo đồng hồ máy chủ) hoặc tối đa 30 giây/lần,
    // và mỗi khi sơ đồ nhận thay đổi trạng thái bàn. Không cần tải lại trang.
    const noShow = window.ReservationNoShow;
    const alertsPanel = byId('late-alerts');
    const alertList = byId('late-alert-list');
    let alertsTimer = 0;
    let alertsRequest = null;

    const element = (tag, className, text) => {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text != null) node.textContent = text;
        return node;
    };

    function noShowButton(alert, label) {
        const button = element('button', 'late-no-show', label);
        button.type = 'button';
        button.dataset.noShowId = alert.id;
        button.setAttribute('aria-label', `Đánh dấu khách không tới: lượt ${alert.code}, ${alert.customerName}`);
        button.addEventListener('click', () => markNoShow(alert));
        return button;
    }

    function renderAlerts(alerts) {
        const firstByTable = new Map();
        alerts.forEach(alert => { if (alert.tableCode && !firstByTable.has(alert.tableCode)) firstByTable.set(alert.tableCode, alert); });
        document.querySelectorAll('[data-table-code]').forEach(card => {
            const alert = firstByTable.get(card.dataset.tableCode);
            const slot = card.querySelector('[data-late-slot]');
            card.classList.toggle('has-late-alert', Boolean(alert));
            if (!slot) return;
            slot.hidden = !alert;
            slot.replaceChildren();
            if (!alert) return;
            slot.append(element('span', 'table-late-label', `${alert.lateLabel} · ${alert.customerName}`), noShowButton(alert, 'Khách không tới'));
        });

        alertList.replaceChildren(...alerts.map(alert => {
            const item = element('li', 'late-alert');
            item.dataset.lateReservation = alert.id;
            const text = element('div', 'late-alert-text');
            text.append(
                element('strong', 'late-alert-table', alert.tableCode ? `Bàn ${alert.tableCode}` : 'Chưa xếp bàn'),
                element('span', null, ` · ${alert.customerName} (${alert.guestCount} khách) · ${alert.phone} · hẹn ${alert.appointmentTime} · `),
                element('span', 'late-alert-minutes', alert.lateLabel),
                element('span', 'late-alert-code', ` · Mã ${alert.code} · ${alert.statusLabel}`));
            item.append(text, noShowButton(alert, 'Đánh dấu khách không tới'));
            return item;
        }));
        byId('late-alerts-count').textContent = alerts.length ? `(${alerts.length})` : '';
        alertsPanel.hidden = alerts.length === 0;
    }

    async function loadAlerts() {
        if (!noShow?.api || !alertsPanel) return;
        clearTimeout(alertsTimer);
        alertsRequest?.abort();
        const request = new AbortController();
        alertsRequest = request;
        let delay = 30000;
        try {
            const response = await fetch(noShow.api, { cache: 'no-store', signal: request.signal, headers: { Accept: 'application/json' } });
            if (!response.ok) throw new Error('Không tải được cảnh báo đặt bàn trễ.');
            const snapshot = await response.json();
            if (alertsRequest !== request) return;
            renderAlerts(snapshot.alerts);
            delay = noShow.nextDelay(snapshot, Date.now());
        } catch (failure) {
            if (failure.name === 'AbortError') return;   // đã có lần tải mới thay thế
        }
        if (alertsRequest === request) alertsTimer = setTimeout(loadAlerts, delay);
    }

    function refreshAlertsSoon() {
        clearTimeout(alertsTimer);
        alertsTimer = setTimeout(loadAlerts, 300);
    }

    async function markNoShow(alert) {
        const result = await noShow.mark(alert.id, `lượt ${alert.code} – ${alert.customerName}${alert.tableCode ? `, bàn ${alert.tableCode}` : ''}`);
        if (!result) return;
        // Thành công hoặc bị từ chối (khách vừa tới, người khác đã đánh dấu…): đồng bộ lại ngay bàn và cảnh báo.
        await Promise.allSettled([refreshSnapshot(), loadAlerts()]);
    }

    document.addEventListener('visibilitychange', () => { if (!document.hidden) refreshAlertsSoon(); });
    loadAlerts();
})();
