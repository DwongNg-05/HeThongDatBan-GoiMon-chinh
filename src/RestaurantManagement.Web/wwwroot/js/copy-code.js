// Nút "Sao chép" mã đặt bàn (trang đặt bàn thành công, tra cứu đặt bàn, chi tiết lượt đặt của nhân viên).
// <button data-copy-code="ED095C">…<i class="fa-regular fa-copy"></i>…<span data-copy-label>Sao chép</span></button>
// Sao chép bằng Clipboard API; trình duyệt cũ / trang không an toàn (http) dùng cách chọn chữ + execCommand.
// Sao chép xong: icon đổi thành dấu ✓, chữ "Đã sao chép" trong 2 giây và đọc thông báo cho trình đọc màn hình.
(() => {
    const buttons = document.querySelectorAll('[data-copy-code]');
    if (!buttons.length) return;

    let live = document.getElementById('copy-code-status');
    if (!live) {
        live = Object.assign(document.createElement('span'), { id: 'copy-code-status', className: 'visually-hidden' });
        live.setAttribute('role', 'status');
        live.setAttribute('aria-live', 'polite');
        document.body.append(live);
    }

    function fallbackCopy(text) {
        const area = Object.assign(document.createElement('textarea'), { value: text, readOnly: true });
        area.style.position = 'fixed';
        area.style.opacity = '0';
        document.body.append(area);
        area.select();
        let ok = false;
        try { ok = document.execCommand('copy'); } catch { ok = false; }
        area.remove();
        return ok;
    }

    async function copy(text) {
        try {
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(text);
                return true;
            }
        } catch { /* thử cách dự phòng */ }
        return fallbackCopy(text);
    }

    buttons.forEach(button => {
        const icon = button.querySelector('i');
        const label = button.querySelector('[data-copy-label]');
        const idleLabel = label?.textContent ?? '';
        let timer = 0;
        button.addEventListener('click', async () => {
            const code = button.dataset.copyCode.trim();
            const ok = await copy(code);
            clearTimeout(timer);
            button.classList.toggle('is-copied', ok);
            if (icon) icon.className = ok ? 'fa-solid fa-check' : 'fa-regular fa-copy';
            if (label) label.textContent = ok ? 'Đã sao chép' : 'Chưa sao chép được';
            live.textContent = ok ? `Đã sao chép mã đặt bàn ${code}` : 'Chưa sao chép được, vui lòng chọn mã và sao chép thủ công';
            timer = setTimeout(() => {
                button.classList.remove('is-copied');
                if (icon) icon.className = 'fa-regular fa-copy';
                if (label) label.textContent = idleLabel;
            }, 2000);
        });
    });
})();
