(() => {
    const root = document.querySelector('[data-no-show-warning]');
    if (!root) return;
    const form = root.closest('form'), phone = form.querySelector('[name="Phone"]');
    const panel = root.querySelector('[data-warning-panel]'), message = root.querySelector('[data-warning-text]');
    const acknowledged = root.querySelector('[name="NoShowAcknowledged"]'), token = root.querySelector('[name="NoShowWarningToken"]');
    const error = root.querySelector('[data-warning-error]');
    phone.closest('.mb-3')?.after(root);
    const normalize = value => value.replace(/[\s.()\-]/g, '').replace(/^(\+84|0084)/, '0');
    let timer, version = 0;
    async function check(current) {
        const value = normalize(phone.value);
        if (!/^0[0-9]{9}$/.test(value)) return;
        const body = new URLSearchParams({ phone: value, __RequestVerificationToken: form.querySelector('[name="__RequestVerificationToken"]').value });
        try {
            const response = await fetch(root.dataset.url, { method: 'POST', body });
            if (!response.ok) throw new Error();
            const data = await response.json();
            if (current !== version) return;
            acknowledged.checked = false;
            panel.hidden = data.count < 3;
            token.value = data.token || '';
            message.textContent = `Số điện thoại này có ${data.count} lần khách không tới trong 90 ngày gần đây. Bạn vẫn có thể tiếp tục đặt bàn.`;
        } catch {
            if (current === version) error.textContent = 'Chưa tra cứu được lịch sử. Hệ thống sẽ kiểm tra lại khi bạn gửi đặt bàn.';
        }
    }
    phone.addEventListener('input', () => {
        clearTimeout(timer); ++version;
        acknowledged.checked = false; token.value = ''; panel.hidden = true; error.textContent = '';
        timer = setTimeout(() => check(version), 350);
    });
    phone.addEventListener('blur', () => { phone.value = normalize(phone.value); });
    form.addEventListener('submit', event => {
        phone.value = normalize(phone.value);
        if (!panel.hidden && !acknowledged.checked) {
            event.preventDefault(); error.textContent = 'Vui lòng xác nhận đã đọc cảnh báo để tiếp tục.'; acknowledged.focus();
        }
    });
    if (phone.value && panel.hidden) check(version);
})();
