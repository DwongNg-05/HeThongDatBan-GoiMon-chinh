// S4-01 Task 4: nút "Xuất PDF" trên màn hình tạm tính (docs/S4-01-Task4.md).
// Tải tệp qua fetch (gửi X-Requested-With: fetch) để: tệp tải về đúng tên máy chủ đặt (TamTinh_{mã bàn}_{yyyyMMdd-HHmm}.pdf);
// không tạo được tệp (bàn không còn khách, lỗi máy chủ, mất mạng, hết phiên đăng nhập…) thì hiện thông báo ngay trên màn hình.
// Chỉ đọc (GET): không đổi trạng thái bàn / đơn gọi món.
(function (root) {
    'use strict';
    const failureMessage = 'Không tạo được tệp PDF. Vui lòng thử lại; nếu vẫn lỗi, bấm “In” rồi chọn máy in “Lưu dưới dạng PDF”.';

    /** Tên tệp trong Content-Disposition (ưu tiên filename*=UTF-8''…), không có thì dùng tên dự phòng. */
    function fileNameFrom(disposition, fallback) {
        if (!disposition) return fallback;
        const encoded = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(disposition);
        if (encoded) {
            try { return decodeURIComponent(encoded[1].trim().replace(/^"|"$/g, '')); } catch { /* dùng filename= */ }
        }
        const plain = /filename\s*=\s*"?([^";]+)"?/i.exec(disposition);
        return plain ? plain[1].trim() : fallback;
    }

    const api = { failureMessage, fileNameFrom };
    if (typeof module === 'object' && module.exports) { module.exports = api; return; }

    const doc = root.document;
    const link = doc.querySelector('[data-preview-pdf]');
    const error = doc.querySelector('[data-preview-error]');
    if (!link) return;
    let busy = false;

    function showError(message) {
        if (!error) return;
        error.textContent = message || failureMessage;
        error.hidden = false;
    }

    link.addEventListener('click', async event => {
        event.preventDefault();
        if (busy) return;
        busy = true;
        const label = link.textContent;
        link.textContent = 'Đang tạo PDF…';
        link.setAttribute('aria-disabled', 'true');
        if (error) error.hidden = true;
        try {
            const response = await root.fetch(link.href, { headers: { 'X-Requested-With': 'fetch' }, credentials: 'same-origin', cache: 'no-store' });
            const type = response.headers.get('Content-Type') || '';
            if (!response.ok || !type.toLowerCase().includes('application/pdf')) {
                let message = failureMessage;
                try {
                    const body = await response.json();
                    if (body && typeof body.message === 'string' && body.message) message = body.message;
                } catch { /* không phải JSON (ví dụ trang đăng nhập khi hết phiên) */ }
                showError(message);
                return;
            }
            const blob = await response.blob();
            const url = root.URL.createObjectURL(blob);
            const anchor = doc.createElement('a');
            anchor.href = url;
            anchor.download = fileNameFrom(response.headers.get('Content-Disposition'), 'TamTinh.pdf');
            doc.body.appendChild(anchor);
            anchor.click();
            anchor.remove();
            root.setTimeout(() => root.URL.revokeObjectURL(url), 10000);
        } catch {
            showError(failureMessage);
        } finally {
            busy = false;
            link.textContent = label;
            link.removeAttribute('aria-disabled');
        }
    });
})(typeof window !== 'undefined' ? window : globalThis);
