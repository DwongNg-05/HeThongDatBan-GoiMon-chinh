// S3-09 Task 1: dùng chung cho sơ đồ bàn và danh sách đặt bàn.
// - mark(): xác nhận rồi gửi POST /api/reservation-alerts/{id}/no-show (mã chống giả mạo trong header RequestVerificationToken).
//   Trong lúc gửi, mọi nút của cùng lượt đặt bị khoá nên bấm nhanh hai lần chỉ gửi một yêu cầu; máy chủ vẫn tự chặn ghi trùng.
// - nextDelay(): thời gian chờ tới lần tải lại kế tiếp — đúng lúc một lượt đạt mốc cảnh báo (theo đồng hồ máy chủ), tối đa 30 giây.
// - notify(): thông báo kết quả ngắn ở góc màn hình.
(() => {
    const api = document.querySelector('[data-reservation-alerts-api]')?.dataset.reservationAlertsApi;
    const pending = new Set();
    let toastTimer = 0;

    function token() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    function notify(message, kind = 'success') {
        let toast = document.getElementById('no-show-toast');
        if (!toast) {
            toast = Object.assign(document.createElement('div'), { id: 'no-show-toast', className: 'no-show-toast' });
            toast.setAttribute('role', 'status');
            toast.setAttribute('aria-live', 'polite');
            document.body.append(toast);
        }
        toast.textContent = message;
        toast.dataset.kind = kind;
        toast.hidden = false;
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => { toast.hidden = true; }, 7000);
    }

    function setBusy(id, busy) {
        document.querySelectorAll(`[data-no-show-id="${id}"]`).forEach(button => {
            button.disabled = busy;
            if (busy) button.dataset.label = button.textContent;
            button.textContent = busy ? 'Đang ghi nhận…' : (button.dataset.label || button.textContent);
        });
    }

    async function mark(id, description) {
        if (!api || pending.has(id)) return null;
        if (!window.confirm(`Đánh dấu khách không tới cho ${description}?\nBàn giữ cho lượt này sẽ được giải phóng và số điện thoại được ghi thêm một lần không tới.`))
            return null;
        pending.add(id);
        setBusy(id, true);
        try {
            const response = await fetch(`${api}/${encodeURIComponent(id)}/no-show`, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { RequestVerificationToken: token(), 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json' }
            });
            const data = await response.json().catch(() => null);
            if (!response.ok) {
                notify(data?.message || 'Không ghi nhận được. Vui lòng tải lại trang và thử lại.', 'error');
                return { ok: false, status: response.status };
            }
            notify(data.message, data.alreadyMarked ? 'info' : 'success');
            return { ok: true, data };
        } catch {
            notify('Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.', 'error');
            return { ok: false, status: 0 };
        } finally {
            pending.delete(id);
            setBusy(id, false);
        }
    }

    // nextWarnAtUtc/evaluatedAtUtc theo đồng hồ máy chủ nên không lệch khi đồng hồ máy khách sai.
    function nextDelay(snapshot, receivedAt, maxMs = 30000) {
        if (!snapshot?.nextWarnAtUtc || !snapshot.evaluatedAtUtc) return maxMs;
        const untilWarn = Date.parse(snapshot.nextWarnAtUtc) - Date.parse(snapshot.evaluatedAtUtc) - (Date.now() - receivedAt);
        return Number.isFinite(untilWarn) ? Math.max(500, Math.min(maxMs, untilWarn + 500)) : maxMs;
    }

    window.ReservationNoShow = { api, mark, notify, nextDelay };
})();
