// S3-09 Task 3: cảnh báo số điện thoại có ít nhất 3 lần khách không tới trong 90 ngày trên form tạo đặt bàn (chỉ nhân viên).
// - Tra cứu khi nhân viên gõ (chờ 350 ms sau phím cuối) và khi rời ô / đổi số; chỉ kết quả của lần tra cứu mới nhất được hiển thị.
// - Khi rời ô, số hợp lệ được điền lại theo dạng chuẩn do máy chủ trả về ("+84 901 234 567" → "0901234567").
// - Số lần thay đổi (đổi số khác, lịch sử tăng) → bỏ đánh dấu xác nhận, ghi số lần mới vào NoShowCountAcknowledged.
// - Không tra cứu được (mất mạng…) thì giữ nguyên; máy chủ vẫn kiểm tra lại khi gửi form.
(() => {
    const root = document.getElementById('phone-no-show');
    if (!root) return;
    const input = document.getElementById(root.dataset.phoneInput || 'Phone');
    if (!input) return;
    const box = document.getElementById('phone-no-show-warning');
    const message = document.getElementById('phone-no-show-message');
    const confirm = document.getElementById('NoShowWarningConfirmed');
    const acknowledged = document.getElementById('NoShowCountAcknowledged');
    const error = document.getElementById('phone-no-show-error');
    let timer = 0;
    let request = null;

    const token = () => (input.form || document).querySelector('input[name="__RequestVerificationToken"]')?.value
        || document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';

    function show(data) {
        if (data?.isWarning) {
            if (Number(acknowledged.value) !== data.noShowCount) {
                confirm.checked = false;
                acknowledged.value = String(data.noShowCount);
                error.textContent = '';
            }
            message.textContent = data.message;
            box.hidden = false;
            return;
        }
        box.hidden = true;
        confirm.checked = false;
        acknowledged.value = '0';
        error.textContent = '';
    }

    async function check(rewrite) {
        clearTimeout(timer);
        request?.abort();
        const value = input.value.trim();
        if (!value) { request = null; show(null); return; }
        const current = new AbortController();
        request = current;
        try {
            const response = await fetch(root.dataset.phoneHistoryApi, {
                method: 'POST',
                credentials: 'same-origin',
                signal: current.signal,
                headers: {
                    RequestVerificationToken: token(), 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json',
                    'Content-Type': 'application/x-www-form-urlencoded;charset=UTF-8'
                },
                body: new URLSearchParams({ phone: value }).toString()
            });
            if (request !== current || !response.ok) return;
            const data = await response.json();
            if (request !== current) return;
            if (!data.valid) { show(null); return; }
            if (rewrite && data.normalizedPhone && input.value.trim() !== data.normalizedPhone) {
                input.value = data.normalizedPhone;
                // Cập nhật lại kiểm tra định dạng phía trình duyệt (jQuery Validation) cho giá trị mới.
                if (window.jQuery?.fn?.valid) window.jQuery(input).valid();
            }
            show(data);
        } catch (failure) {
            if (failure?.name !== 'AbortError') { /* máy chủ kiểm tra lại khi gửi form */ }
        }
    }

    input.addEventListener('input', () => {
        clearTimeout(timer);
        timer = setTimeout(() => check(false), 350);
    });
    input.addEventListener('change', () => check(true));
    if (input.value.trim()) check(false);

    window.PhoneNoShowWarning = { check, show };
})();
