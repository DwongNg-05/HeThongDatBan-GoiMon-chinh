// S2-09 Task 2: trang xác nhận đặt bàn tự cập nhật trạng thái email khi email đang được gửi lại,
// dừng khi đã có kết quả cuối cùng (gửi thành công hoặc hết 3 lần gửi lại).
(() => {
    const POLL_MS = 15000;
    const notice = document.querySelector('.booking-email-notice[data-status-url]');
    if (!notice) return;

    const schedule = () => {
        if (notice.dataset.emailFinal === 'false') setTimeout(refresh, POLL_MS);
    };

    async function refresh() {
        try {
            const response = await fetch(notice.dataset.statusUrl, { cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) return;
            const status = await response.json();
            notice.textContent = status.message;
            notice.className = `alert ${status.cssClass} booking-email-notice`;
            notice.dataset.emailState = status.state;
            notice.dataset.emailFinal = status.final ? 'true' : 'false';
        } catch {
            // Mất mạng tạm thời: thử lại ở lần sau.
        }
        schedule();
    }

    schedule();
})();
