(() => {
    const input = document.querySelector('[data-code-check]');
    const message = document.getElementById('code-availability');
    if (!input || !message) return;
    const id = document.getElementById('Id')?.value ?? '';
    let timer;
    input.addEventListener('input', () => {
        window.clearTimeout(timer);
        const code = input.value.trim();
        message.textContent = '';
        message.className = 'form-text';
        if (!code) return;
        timer = window.setTimeout(async () => {
            try {
                const url = new URL('/Tables/CheckCode', window.location.origin);
                url.searchParams.set('code', code);
                if (id) url.searchParams.set('id', id);
                // S1-04: gọi kèm X-Requested-With để máy chủ trả mã lỗi 401/403 dạng JSON thay vì trang HTML.
                const response = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                if (!response.ok) throw new Error('Code check failed');
                const result = await response.json();
                message.textContent = result.available ? 'Mã bàn có thể sử dụng.' : 'Mã bàn đã tồn tại.';
                message.className = result.available ? 'form-text text-success' : 'form-text text-danger';
            } catch { message.textContent = 'Không thể kiểm tra mã bàn lúc này.'; message.className = 'form-text text-warning'; }
        }, 300);
    });
})();
