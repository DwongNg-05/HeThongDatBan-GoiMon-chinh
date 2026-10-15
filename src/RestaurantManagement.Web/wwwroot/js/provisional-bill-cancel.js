// S3-05 Task 1: hộp xác nhận huỷ món trên hoá đơn tạm tính.
// - Bấm "Hủy món" ở một dòng → mở hộp: tên món, số lượng, số tiền, món có còn tính tiền không; lý do KHÔNG chọn sẵn.
// - "Xác nhận hủy" chỉ bật khi đã chọn một lý do (khách đổi ý, gọi nhầm, hết nguyên liệu; Quản lý có thêm "Quản lý yêu cầu").
//   Gửi khi chưa chọn lý do (ví dụ bấm Enter) bị chặn kèm thông báo; máy chủ vẫn tự kiểm tra lại.
// - expectedStatus = trạng thái dòng món lúc mở hộp: bếp bắt đầu làm trong lúc nhân viên chọn lý do thì máy chủ từ chối.
// - S3-05 Task 3: Quản lý huỷ món bếp đã bắt đầu (đang nấu / đã xong) = huỷ có tính tiền: chỉ ba lý do ("Quản lý yêu cầu" bị ẩn)
//   và phải đánh dấu ô "xác nhận hủy có tính tiền" thì nút xác nhận mới bật. Máy chủ kiểm tra lại cả hai.
// - Đang gửi: khoá cả hai nút và bỏ qua mọi lần gửi tiếp theo, nên bấm nhanh hai lần chỉ gửi một yêu cầu.
(() => {
    const dialog = document.getElementById('cancel-item-dialog');
    const form = document.getElementById('cancel-item-form');
    if (!dialog || !form) return;
    const expected = document.getElementById('cancel-expected-status');
    const itemText = document.getElementById('cancel-dialog-item');
    const free = document.getElementById('cancel-dialog-free');
    const charge = document.getElementById('cancel-dialog-charge');
    const note = document.getElementById('cancel-dialog-note');
    const error = document.getElementById('cancel-dialog-error');
    const chargedConfirm = document.getElementById('cancel-dialog-charged-confirm');
    const chargedCheck = document.getElementById('cancel-confirm-charged');
    const chargedAmount = document.getElementById('cancel-dialog-charged-amount');
    const confirm = form.querySelector('[data-cancel-confirm]');
    const dismiss = form.querySelector('[data-cancel-dismiss]');
    const confirmLabel = confirm.textContent;
    let submitting = false;
    let opener = null;
    let chargedMode = false;

    const reasons = () => Array.from(form.querySelectorAll('input[name="reason"]')).filter(input => !input.disabled);
    const allReasons = () => Array.from(form.querySelectorAll('input[name="reason"]'));
    const selectedReason = () => reasons().find(input => input.checked)?.value || '';
    const chargedConfirmed = () => !chargedMode || !chargedCheck || chargedCheck.checked;
    const ready = () => !!selectedReason() && chargedConfirmed();
    const missingText = () => !selectedReason()
        ? (error.dataset?.missingReason || error.textContent)
        : (error.dataset?.missingConfirm || error.textContent);

    // Huỷ có tính tiền: ẩn và vô hiệu lý do không dành cho huỷ có tính tiền (data-reason-charged="false").
    function applyMode(charged) {
        chargedMode = charged;
        allReasons().forEach(input => {
            const label = input.closest?.('[data-reason-charged]');
            const excluded = charged && label?.dataset?.reasonCharged === 'false';
            input.disabled = excluded;
            input.checked = false;
            if (label) label.hidden = excluded;
        });
        if (chargedConfirm) chargedConfirm.hidden = !charged;
        if (chargedCheck) {
            chargedCheck.checked = false;
            chargedCheck.disabled = !charged;
            chargedCheck.required = charged;
        }
    }

    function show() {
        if (typeof dialog.showModal === 'function') dialog.showModal();
        else dialog.setAttribute('open', '');
    }

    function hide() {
        if (typeof dialog.close === 'function') dialog.close();
        else dialog.removeAttribute('open');
    }

    function open(button) {
        if (submitting || !button || button.disabled) return false;
        opener = button;
        form.setAttribute('action', button.dataset.cancelUrl);
        expected.value = button.dataset.cancelStatus || '';
        itemText.textContent = `${button.dataset.cancelQuantity} · ${button.dataset.cancelName} · ${button.dataset.cancelAmount}`;
        const charged = button.dataset.cancelCharged === 'true';
        charge.hidden = !charged;
        free.hidden = charged;
        applyMode(charged);
        if (chargedAmount) chargedAmount.textContent = button.dataset.cancelAmount || '';
        note.value = '';
        error.hidden = true;
        confirm.disabled = true;
        confirm.textContent = charged ? 'Xác nhận hủy có tính tiền' : confirmLabel;
        dismiss.disabled = false;
        show();
        reasons()[0]?.focus();
        return true;
    }

    function close() {
        if (submitting) return false;
        hide();
        opener?.focus?.();
        return true;
    }

    document.addEventListener('click', event => {
        const button = event.target?.closest?.('[data-cancel-item]');
        if (!button) return;
        event.preventDefault();
        open(button);
    });

    form.addEventListener('change', event => {
        if (event.target?.name !== 'reason' && event.target?.name !== 'confirmCharged') return;
        confirm.disabled = submitting || !ready();
        if (ready()) error.hidden = true;
    });

    dismiss.addEventListener('click', () => close());

    // Esc: không đóng hộp khi đang gửi yêu cầu.
    dialog.addEventListener('cancel', event => { if (submitting) event.preventDefault(); });

    form.addEventListener('submit', event => {
        if (submitting) { event.preventDefault(); return; }
        if (!ready()) {
            event.preventDefault();
            error.textContent = missingText();
            error.hidden = false;
            confirm.disabled = true;
            return;
        }
        submitting = true;
        confirm.disabled = true;
        dismiss.disabled = true;
        confirm.textContent = 'Đang hủy…';
    });

    // Quay lại trang bằng nút Back (trình duyệt giữ trang cũ trong bộ nhớ): mở khoá và đóng hộp.
    window.addEventListener('pageshow', event => {
        if (!event.persisted) return;
        submitting = false;
        hide();
    });

    window.ProvisionalBillCancel = { open, close, isSubmitting: () => submitting, isCharged: () => chargedMode };
})();
