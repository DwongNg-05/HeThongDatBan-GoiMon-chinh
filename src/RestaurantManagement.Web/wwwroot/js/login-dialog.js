// Hộp đăng nhập ngay trên trang (avatar ở đầu trang) cho người chưa đăng nhập.
// - Gửi tới đúng POST /Account/Login bằng fetch + X-Requested-With: máy chủ trả JSON (thành công: redirectUrl; sai: 401 + thông báo
//   + số giây còn khoá), nên khoá tạm thời, nhật ký, xác minh email, đổi mật khẩu giữ nguyên như trang Đăng nhập.
// - Mã chống giả mạo chỉ lấy khi mở hộp (từ trang /Account/Login) để trang công khai không sinh token/cookie, giữ header cache như cũ.
// - Không hỗ trợ <dialog> hoặc tắt JS: avatar vẫn là liên kết sang trang Đăng nhập.
(() => {
    const dialog = document.querySelector('[data-login-dialog]');
    if (!dialog || typeof dialog.showModal !== 'function') return;

    const form = dialog.querySelector('[data-login-form]');
    const { Identifier: identifier, Password: password, returnUrl } = form.elements;
    const errorBox = dialog.querySelector('[data-login-error]');
    const errorText = dialog.querySelector('[data-login-error-text]');
    const submit = dialog.querySelector('[data-login-submit]');
    const reveal = dialog.querySelector('[data-login-reveal]');
    const loginUrl = form.getAttribute('action');
    let opener = null;
    let countdown = 0;

    function showError(message = '', remainingSeconds = 0) {
        clearInterval(countdown);
        errorBox.hidden = !message;
        errorText.textContent = message;
        if (!(remainingSeconds > 0)) return;
        // Như trang Đăng nhập: đếm ngược thời gian khoá tạm thời.
        const deadline = Date.now() + remainingSeconds * 1000;
        const tick = () => {
            const left = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
            const mmss = `${String(Math.floor(left / 60)).padStart(2, '0')}:${String(left % 60).padStart(2, '0')}`;
            errorText.textContent = left > 0
                ? `Đã có nhiều lần đăng nhập không thành công. Vui lòng thử lại sau ${mmss}.`
                : 'Đã hết thời gian chờ. Bạn có thể đăng nhập lại.';
            if (left === 0) clearInterval(countdown);
        };
        tick();
        countdown = setInterval(tick, 1000);
    }

    function setRevealed(show) {
        password.type = show ? 'text' : 'password';
        reveal.setAttribute('aria-pressed', String(show));
        reveal.setAttribute('aria-label', show ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
        reveal.firstElementChild.className = `fa-regular ${show ? 'fa-eye-slash' : 'fa-eye'}`;
    }

    // Lấy mã chống giả mạo (cùng cookie phiên) từ trang Đăng nhập; fresh = bỏ mã cũ để lấy mã mới.
    async function ensureToken(fresh = false) {
        const current = form.elements.__RequestVerificationToken;
        if (current && !fresh) return true;
        current?.remove();
        const response = await fetch(loginUrl, { credentials: 'same-origin', headers: { Accept: 'text/html' } });
        if (response.redirected) { window.location.assign(response.url); return false; } // đã đăng nhập ở thẻ khác
        if (!response.ok) return false;
        const token = new DOMParser().parseFromString(await response.text(), 'text/html')
            .querySelector('input[name="__RequestVerificationToken"]');
        if (token) form.append(Object.assign(document.createElement('input'), { type: 'hidden', name: token.name, value: token.value }));
        return Boolean(token);
    }

    const post = () => fetch(loginUrl, {
        method: 'POST',
        body: new FormData(form),
        credentials: 'same-origin',
        headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json' }
    });

    function open(event) {
        if (event.button > 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return; // mở thẻ mới như liên kết thường
        event.preventDefault();
        opener = event.currentTarget;
        showError();
        // Chỉ chuyển tiếp returnUrl nội bộ đã có trên địa chỉ (bị đưa về Đăng nhập khi mở trang cần quyền).
        const target = new URLSearchParams(window.location.search).get('returnUrl') || '';
        returnUrl.value = /^\/(?![\/\\])/.test(target) ? target : '';
        dialog.showModal();
        document.documentElement.classList.add('login-dialog-open');
        identifier.focus();
        ensureToken().catch(() => { /* thử lại khi bấm Đăng nhập */ });
    }

    dialog.addEventListener('close', () => {
        document.documentElement.classList.remove('login-dialog-open');
        clearInterval(countdown);
        password.value = '';
        setRevealed(false);
        opener?.focus();
    });
    // Bấm vào nền mờ (ngoài khung) để đóng; bấm vào phần đệm trong khung cũng có target là dialog nên phải so toạ độ.
    dialog.addEventListener('click', event => {
        const box = dialog.getBoundingClientRect();
        const outside = event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom;
        if (event.target === dialog && outside) dialog.close();
    });
    dialog.querySelector('[data-login-close]').addEventListener('click', () => dialog.close());
    reveal.addEventListener('click', () => { setRevealed(password.type === 'password'); password.focus(); });

    form.addEventListener('submit', async event => {
        event.preventDefault();
        submit.disabled = true;
        submit.textContent = 'Đang đăng nhập…';
        showError();
        try {
            if (!await ensureToken()) { window.location.assign(loginUrl); return; }
            let response = await post();
            if (response.status === 400) { // mã chống giả mạo cũ (ví dụ vừa đăng xuất): lấy mã mới, gửi lại một lần
                if (!await ensureToken(true)) { window.location.assign(loginUrl); return; }
                response = await post();
            }
            const data = await response.json().catch(() => null);
            if (response.ok && data?.succeeded) { window.location.assign(data.redirectUrl || '/'); return; }
            showError(data?.message || 'Không đăng nhập được lúc này. Vui lòng thử lại.', data?.remainingSeconds);
            password.value = '';
            password.focus();
        } catch {
            showError('Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.');
        } finally {
            submit.disabled = false;
            submit.textContent = 'Đăng nhập';
        }
    });

    document.querySelectorAll('[data-login-open]').forEach(link => link.addEventListener('click', open));
})();
