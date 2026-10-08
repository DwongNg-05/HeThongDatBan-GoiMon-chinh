(() => {
    let root = document.querySelector('[data-screen="kitchen-orders"]');
    if (!root) return;
    let loading = false, submitting = false;
    document.addEventListener('submit', event => {
        if (event.target.closest('[data-screen="kitchen-orders"]')) submitting = true;
    });
    const message = document.createElement('p');
    message.setAttribute('role', 'status'); root.before(message);
    async function refresh() {
        if (loading || submitting) return;
        loading = true;
        try {
            const response = await fetch('/Kitchen', { cache: 'no-store', signal: AbortSignal.timeout(2000) });
            if (!response.ok || response.redirected) throw new Error();
            const page = new DOMParser().parseFromString(await response.text(), 'text/html');
            const latest = page.querySelector('[data-screen="kitchen-orders"]');
            if (!latest) throw new Error();
            root.replaceWith(latest); root = latest; message.textContent = '';
        } catch { message.textContent = 'Chưa cập nhật được hàng đợi bếp. Kiểm tra kết nối hoặc tải lại trang.'; }
        finally { loading = false; }
    }
    setInterval(refresh, 2000);
})();
