(() => {
    'use strict';
    const map = document.querySelector('.snapshot-map');
    const dialog = document.querySelector('#snapshot-table-details');
    if (!map || !dialog) return;
    const field = name => dialog.querySelector('[data-details-' + name + ']');
    const title = dialog.querySelector('#snapshot-details-title');
    const money = new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 });
    const time = value => value ? new Date(value).toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' }) : 'Chưa có thông tin';
    let selected = null, opener = null, pending = null, revision = 0, denied = false;
    function text(name, value) { field(name).textContent = value ?? ''; }
    function clearContent() {
        ['guest', 'phone', 'count', 'started', 'duration', 'subtotal', 'subtotal-note', 'reservation-name', 'reservation-phone', 'reservation-count', 'reservation-time', 'reservation-more', 'updated'].forEach(name => text(name, ''));
        field('content').hidden = true;
    }
    function render(details) {
        const active = details.status === 'Serving' && details.hasActiveSession;
        const reservation = details.upcomingReservation;
        title.textContent = 'Bàn ' + details.code;
        text('status', details.statusLabel);
        text('area', `${details.area} · ${details.capacity} chỗ`);
        text('context', {
            Available: 'Bàn đang trống.', Reserved: 'Bàn đã đặt trước.',
            Serving: active ? 'Bàn đang phục vụ khách.' : 'Chưa có thông tin phiên phục vụ của bàn.',
            Cleaning: 'Bàn đang dọn. Không có khách hoặc tạm tính của phiên đã kết thúc.'
        }[details.status] || 'Chưa xác định trạng thái bàn.');
        text('guest', active ? (details.currentGuestName || 'Khách vãng lai') : 'Chưa có khách đang ngồi');
        text('phone', active ? (details.currentGuestPhone || 'Chưa có số điện thoại') : '');
        text('count', active && details.currentGuestCount != null ? `${details.currentGuestCount} người` : '');
        text('started', active ? time(details.serviceStartedAtUtc) : 'Chưa bắt đầu phục vụ');
        text('duration', active && details.serviceElapsedMinutes != null ? `Đã phục vụ ${details.serviceElapsedMinutes} phút` : '');
        text('subtotal', active && details.currentSubtotal != null ? money.format(details.currentSubtotal) : '—');
        text('subtotal-note', active ? details.subtotalExplanation : 'Không có phiên phục vụ hiện tại để tính tiền.');
        field('reservation-empty').hidden = !!reservation;
        field('reservation-content').hidden = !reservation;
        text('reservation-name', reservation?.customerName);
        text('reservation-phone', reservation?.phone);
        text('reservation-count', reservation ? `${reservation.guestCount} người` : '');
        text('reservation-time', reservation ? time(reservation.startsAtUtc) : '');
        text('reservation-more', reservation && details.upcomingReservationCount > 1 ? `Hiển thị lượt gần nhất; còn ${details.upcomingReservationCount - 1} lượt đã xác nhận sau lượt này.` : '');
        text('updated', time(details.syncedAtUtc));
        field('updated').dateTime = details.syncedAtUtc;
        field('content').hidden = false;
        text('message', 'Thông tin tự cập nhật khi bảng đang mở.');
        field('retry').hidden = true;
    }
    async function refresh() {
        if (!dialog.open || !selected || document.hidden || pending || denied) return;
        const code = selected, version = revision;
        const request = new AbortController(); pending = request;
        const timeout = setTimeout(() => request.abort(), 3000);
        try {
            const response = await fetch(map.dataset.detailsUrl + '/' + encodeURIComponent(code), { cache: 'no-store', credentials: 'same-origin', signal: request.signal, headers: { Accept: 'application/json' } });
            if (version !== revision || !dialog.open) return;
            if (response.redirected || response.status === 401 || response.status === 403) {
                denied = true; clearContent();
                text('message', 'Bạn không còn quyền xem hoặc phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
                field('retry').hidden = true;
                return;
            }
            if (response.status === 404) { clearContent(); throw new Error('Không tìm thấy bàn.'); }
            if (!response.ok) throw new Error('Không tải được chi tiết. Dữ liệu có thể đã cũ. Hệ thống sẽ tự thử lại.');
            const details = await response.json();
            if (version === revision && selected === code && dialog.open && !document.hidden) render(details);
        } catch (error) {
            if (version !== revision || !dialog.open || document.hidden) return;
            text('message', error.name === 'AbortError' ? 'Không tải được chi tiết. Dữ liệu có thể đã cũ. Vui lòng thử lại.' : error.message);
            field('retry').hidden = false;
        } finally { clearTimeout(timeout); if (pending === request) pending = null; }
    }
    function cancel() { revision++; pending?.abort(); pending = null; }
    function close() { dialog.close(); }
    map.querySelectorAll('[data-open-details]').forEach(button => button.addEventListener('click', () => {
        cancel(); selected = button.dataset.openDetails; opener = button; denied = false;
        title.textContent = 'Bàn ' + selected; clearContent(); field('retry').hidden = true;
        text('message', 'Đang tải thông tin…');
        if (!dialog.open) dialog.showModal();
        field('close').focus(); void refresh();
    }));
    field('close').addEventListener('click', close);
    field('retry').addEventListener('click', () => { cancel(); void refresh(); });
    dialog.addEventListener('close', () => { cancel(); selected = null; clearContent(); opener?.focus(); });
    dialog.addEventListener('click', event => {
        if (event.target !== dialog) return;
        const box = dialog.getBoundingClientRect();
        if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) close();
    });
    map.addEventListener('tablemap:changed', event => { if (event.detail.code === selected) void refresh(); });
    document.addEventListener('visibilitychange', () => { cancel(); if (!document.hidden) void refresh(); });
    window.addEventListener('offline', () => { cancel(); if (dialog.open) { text('message', 'Mất kết nối. Chi tiết có thể đã cũ.'); field('retry').hidden = false; } });
    window.addEventListener('online', () => { cancel(); void refresh(); });
    const interval = setInterval(() => void refresh(), 2000);
    window.addEventListener('pagehide', () => { cancel(); clearInterval(interval); if (dialog.open) close(); });
})();
