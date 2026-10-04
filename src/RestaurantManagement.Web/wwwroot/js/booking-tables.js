// Form đặt bàn: tải lại danh sách bàn còn trống khi khách đổi giờ, số khách hoặc khu vực.
// Máy chủ vẫn kiểm tra lại khi gửi (bàn có thể vừa bị người khác đặt).
(() => {
    const select = document.querySelector('[data-tables-url]');
    const startsAt = document.getElementById('StartsAt');
    const guests = document.getElementById('GuestCount');
    const area = document.getElementById('PreferredAreaId');
    const feedback = document.getElementById('booking-tables-feedback');
    if (!select || !startsAt || !guests) return;

    let pending;
    const refresh = async () => {
        pending?.abort();
        const request = new AbortController();
        pending = request;
        if (!startsAt.value || !guests.value) return;
        const params = new URLSearchParams({ startsAt: startsAt.value, guestCount: guests.value });
        if (area?.value) params.set('preferredAreaId', area.value);
        try {
            const response = await fetch(`${select.dataset.tablesUrl}?${params}`, { signal: request.signal, cache: 'no-store' });
            if (!response.ok || response.redirected) throw new Error('Tables request failed');
            const result = await response.json();
            if (request.signal.aborted) return;
            const previous = select.value;
            select.querySelectorAll('option:not([value=""])').forEach(option => option.remove());
            for (const table of result.tables) {
                const option = new Option(table.label, String(table.id));
                option.selected = String(table.id) === previous;
                select.add(option);
            }
            if (previous && select.value !== previous) select.value = '';
            if (feedback) {
                feedback.className = result.message ? 'mt-2 text-danger' : 'mt-2 text-muted';
                feedback.textContent = result.message
                    ?? (previous && !select.value ? 'Bàn đã chọn không còn trống với lựa chọn mới. Vui lòng chọn lại bàn.' : '');
            }
        } catch (error) {
            if (error.name === 'AbortError') return;
            if (feedback) {
                feedback.className = 'mt-2 text-muted';
                feedback.textContent = 'Chưa tải được danh sách bàn. Hệ thống sẽ kiểm tra lại khi gửi đặt bàn.';
            }
        }
    };
    for (const input of [startsAt, guests, area]) input?.addEventListener('change', refresh);
})();
