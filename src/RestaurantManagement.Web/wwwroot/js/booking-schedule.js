(() => {
    const input = document.querySelector('[data-schedule-url]');
    const feedback = document.getElementById('booking-schedule-feedback');
    if (!input || !feedback) return;
    const date = document.getElementById('ReservationDate');
    const time = document.getElementById('ReservationTime');
    const sync = () => {
        if (date && time) input.value = date.value && time.value ? date.value + 'T' + time.value : '';
    };
    let pending;
    const check = async () => {
        sync();
        pending?.abort();
        const request = new AbortController();
        pending = request;
        feedback.textContent = '';
        if (!input.value) return;
        try {
            const response = await fetch(`${input.dataset.scheduleUrl}?startsAt=${encodeURIComponent(input.value)}`, { signal: request.signal, cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok) throw new Error('Schedule check failed');
            const result = await response.json();
            if (request.signal.aborted) return;
            feedback.className = result.allowed ? 'mt-2 text-success' : 'mt-2 text-danger';
            feedback.textContent = result.message;
        } catch (error) {
            if (error.name === 'AbortError') return;
            feedback.className = 'mt-2 text-muted';
            feedback.textContent = 'Chưa kiểm tra được lịch. Hệ thống sẽ kiểm tra lại khi gửi đặt bàn.';
        }
    };
    input.addEventListener('change', check);
    date?.addEventListener('change', check);
    time?.addEventListener('change', check);
    input.form?.addEventListener('submit', sync);
    check();
})();
