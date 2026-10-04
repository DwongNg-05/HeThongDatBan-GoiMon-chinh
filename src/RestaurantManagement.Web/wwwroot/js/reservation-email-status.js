// S2-09 Task 3: khu vực "Email xác nhận" trong chi tiết đặt bàn tự tải lại khi còn email chưa có kết quả cuối cùng.
// Máy chủ trả về đúng khu vực của mã đặt bàn đang xem (/Reservations/EmailStatus/{id}).
(() => {
    const POLL_MS = 15000;
    let timer = null;

    const panel = () => document.getElementById('reservation-email-status');

    async function refresh() {
        const current = panel();
        const url = current?.dataset.refreshUrl;
        if (!current || !url) return;
        try {
            const response = await fetch(url, { cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            // Hết phiên đăng nhập (bị chuyển tới trang đăng nhập) hoặc lỗi: dừng tự cập nhật, giữ nội dung đang có.
            if (!response.ok || response.redirected) { stop(); return; }
            const html = await response.text();
            const template = document.createElement('template');
            template.innerHTML = html.trim();
            const next = template.content.querySelector('#reservation-email-status');
            if (!next || next.dataset.reservationId !== current.dataset.reservationId) { stop(); return; }
            current.replaceWith(next);
            bind();
            schedule();
        } catch {
            schedule();
        }
    }

    function stop() {
        if (timer) clearTimeout(timer);
        timer = null;
    }

    function schedule() {
        stop();
        if (panel()?.dataset.final === 'false') timer = setTimeout(refresh, POLL_MS);
    }

    function bind() {
        panel()?.querySelector('.reservation-email-refresh')?.addEventListener('click', () => { stop(); refresh(); });
    }

    bind();
    schedule();
})();
