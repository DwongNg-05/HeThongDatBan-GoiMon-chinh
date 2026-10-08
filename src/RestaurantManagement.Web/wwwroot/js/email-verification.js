// Xác minh email: ô mã tự đổi sang chữ in hoa, nút "Gửi lại mã" đếm ngược.
(() => {
    const code = document.getElementById('ev-code');
    code?.addEventListener('input', () => {
        const start = code.selectionStart;
        code.value = code.value.toUpperCase().replace(/[^A-Z0-9 -]/g, '');
        code.setSelectionRange(start, start);
    });

    const button = document.getElementById('ev-resend');
    const label = document.getElementById('ev-resend-wait');
    let wait = Number(button?.dataset.wait || 0);
    if (!button || wait <= 0) return;
    const timer = setInterval(() => {
        wait -= 1;
        if (wait <= 0) {
            clearInterval(timer);
            button.disabled = false;
            label.textContent = '';
        } else {
            label.textContent = ` (${wait} giây)`;
        }
    }, 1000);
})();
