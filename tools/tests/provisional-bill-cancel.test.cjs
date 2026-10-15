// S3-05 Task 1: hộp xác nhận huỷ món trên hoá đơn tạm tính (wwwroot/js/provisional-bill-cancel.js).
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('src/RestaurantManagement.Web/wwwroot/js/provisional-bill-cancel.js', 'utf8');

function element(extra = {}) {
    return { hidden: false, disabled: false, value: '', textContent: '', checked: false, dataset: {}, attributes: {}, listeners: {},
        focused: 0,
        addEventListener(name, callback) { (this.listeners[name] ??= []).push(callback); },
        setAttribute(name, value) { this.attributes[name] = String(value); },
        removeAttribute(name) { delete this.attributes[name]; },
        focus() { this.focused++; },
        closest(selector) { return selector === '[data-cancel-item]' && this.dataset.cancelItem ? this : null; },
        ...extra };
}
function fire(target, name, event = {}) {
    let prevented = false;
    const payload = { target, preventDefault() { prevented = true; }, ...event };
    (target.listeners[name] || []).forEach(callback => callback(payload));
    return { prevented };
}

function setup(reasonCodes = ['ChangedMind', 'Mistake', 'SoldOut']) {
    const reasons = reasonCodes.map(code => element({ name: 'reason', value: code }));
    const confirm = element({ textContent: 'Xác nhận hủy', disabled: true });
    const dismiss = element({ textContent: 'Không hủy' });
    const dialog = element({ shown: 0, closed: 0, showModal() { this.shown++; this.open = true; }, close() { this.closed++; this.open = false; } });
    const form = element({
        querySelectorAll: selector => (selector === 'input[name="reason"]' ? reasons : []),
        querySelector: selector => (selector === '[data-cancel-confirm]' ? confirm : selector === '[data-cancel-dismiss]' ? dismiss : null)
    });
    const byId = {
        'cancel-item-dialog': dialog, 'cancel-item-form': form,
        'cancel-expected-status': element(), 'cancel-dialog-item': element(), 'cancel-dialog-free': element({ hidden: true }),
        'cancel-dialog-charge': element({ hidden: true }), 'cancel-dialog-note': element(), 'cancel-dialog-error': element({ hidden: true })
    };
    const documentListeners = {};
    const document = { getElementById: id => byId[id] || null, addEventListener(name, callback) { (documentListeners[name] ??= []).push(callback); } };
    const window = element();
    vm.runInNewContext(source, { document, window, Array });
    const clickDocument = target => {
        let prevented = false;
        (documentListeners.click || []).forEach(callback => callback({ target, preventDefault() { prevented = true; } }));
        return { prevented };
    };
    const button = (status = 'Pending', id = '41') => element({
        dataset: { cancelItem: id, cancelUrl: `/ProvisionalBill/7/CancelItem/${id}`, cancelName: 'Phở bò', cancelQuantity: '2 tô',
            cancelAmount: '120.000 ₫', cancelStatus: status, cancelCharged: status === 'Pending' ? 'false' : 'true' }
    });
    return { reasons, confirm, dismiss, dialog, form, byId, window, clickDocument, button, api: window.ProvisionalBillCancel };
}

test('clicking "Hủy món" opens the dialog for that line with no reason selected and the confirm button off', () => {
    const t = setup();
    t.reasons[1].checked = true;   // còn sót từ lần mở trước
    t.byId['cancel-dialog-note'].value = 'cũ';
    const line = t.button();
    assert.equal(t.clickDocument(line).prevented, true);
    assert.equal(t.dialog.shown, 1);
    assert.equal(t.form.attributes.action, '/ProvisionalBill/7/CancelItem/41');
    assert.equal(t.byId['cancel-expected-status'].value, 'Pending');
    assert.equal(t.byId['cancel-dialog-item'].textContent, '2 tô · Phở bò · 120.000 ₫');
    assert.equal(t.byId['cancel-dialog-free'].hidden, false);
    assert.equal(t.byId['cancel-dialog-charge'].hidden, true);
    assert.ok(t.reasons.every(reason => !reason.checked), 'no reason is pre-selected');
    assert.equal(t.byId['cancel-dialog-note'].value, '');
    assert.equal(t.confirm.disabled, true);
    assert.equal(t.reasons[0].focused, 1);
});

test('a line the kitchen already started (manager) warns that it stays charged', () => {
    const t = setup(['ChangedMind', 'Mistake', 'SoldOut', 'ManagerOverride']);
    t.clickDocument(t.button('Preparing', '42'));
    assert.equal(t.byId['cancel-expected-status'].value, 'Preparing');
    assert.equal(t.byId['cancel-dialog-charge'].hidden, false);
    assert.equal(t.byId['cancel-dialog-free'].hidden, true);
});

test('clicks outside a cancel button do nothing', () => {
    const t = setup();
    assert.equal(t.clickDocument(element()).prevented, false);
    assert.equal(t.dialog.shown, 0);
});

test('each of the three reasons enables the confirm button', () => {
    for (const index of [0, 1, 2]) {
        const t = setup();
        t.clickDocument(t.button());
        t.reasons[index].checked = true;
        fire(t.form, 'change', { target: t.reasons[index] });
        assert.equal(t.confirm.disabled, false, t.reasons[index].value);
    }
});

test('sending without a reason is blocked and explained', () => {
    const t = setup();
    t.clickDocument(t.button());
    const result = fire(t.form, 'submit');
    assert.equal(result.prevented, true);
    assert.equal(t.byId['cancel-dialog-error'].hidden, false);
    assert.equal(t.confirm.disabled, true);
    assert.equal(t.api.isSubmitting(), false);
    t.reasons[2].checked = true;
    fire(t.form, 'change', { target: t.reasons[2] });
    assert.equal(t.byId['cancel-dialog-error'].hidden, true);
});

test('confirming sends once: buttons lock, a second submit and Esc are ignored', () => {
    const t = setup();
    t.clickDocument(t.button());
    t.reasons[0].checked = true;
    fire(t.form, 'change', { target: t.reasons[0] });
    assert.equal(fire(t.form, 'submit').prevented, false, 'the first confirmation is sent');
    assert.equal(t.confirm.disabled, true);
    assert.equal(t.dismiss.disabled, true);
    assert.equal(t.confirm.textContent, 'Đang hủy…');
    assert.equal(fire(t.form, 'submit').prevented, true, 'a second submit is blocked');
    assert.equal(fire(t.dialog, 'cancel').prevented, true, 'Esc does not close the dialog while sending');
    assert.equal(t.api.close(), false);
    assert.equal(t.api.open(t.button('Pending', '43')), false, 'another line cannot be opened while sending');
    fire(t.window, 'pageshow', { persisted: true });
    assert.equal(t.api.isSubmitting(), false, 'coming back with the Back button unlocks the page');
});

test('"Không hủy" closes the dialog and returns the focus to the line button', () => {
    const t = setup();
    const line = t.button();
    t.clickDocument(line);
    fire(t.dismiss, 'click');
    assert.equal(t.dialog.closed, 1);
    assert.equal(line.focused, 1);
});

// S3-05 Task 3: huỷ có tính tiền của Quản lý — ba lý do, ô xác nhận bắt buộc.
function setupManager() {
    const t = setup(['ChangedMind', 'Mistake', 'SoldOut', 'ManagerOverride']);
    t.labels = t.reasons.map(reason => {
        const label = element({ dataset: { reasonCharged: reason.value === 'ManagerOverride' ? 'false' : 'true' } });
        reason.closest = selector => (selector === '[data-reason-charged]' ? label : null);
        return label;
    });
    return t;
}
function setupManagerWithConfirm() {
    // Dựng lại với ô xác nhận (getElementById đọc khi chạy script).
    const reasons = ['ChangedMind', 'Mistake', 'SoldOut', 'ManagerOverride'].map(code => element({ name: 'reason', value: code }));
    const labels = reasons.map(reason => {
        const label = element({ dataset: { reasonCharged: reason.value === 'ManagerOverride' ? 'false' : 'true' } });
        reason.closest = selector => (selector === '[data-reason-charged]' ? label : null);
        return label;
    });
    const confirm = element({ textContent: 'Xác nhận hủy', disabled: true });
    const dismiss = element({ textContent: 'Không hủy' });
    const dialog = element({ showModal() { this.open = true; }, close() { this.open = false; } });
    const form = element({
        querySelectorAll: selector => (selector === 'input[name="reason"]' ? reasons : []),
        querySelector: selector => (selector === '[data-cancel-confirm]' ? confirm : selector === '[data-cancel-dismiss]' ? dismiss : null)
    });
    const byId = {
        'cancel-item-dialog': dialog, 'cancel-item-form': form,
        'cancel-expected-status': element(), 'cancel-dialog-item': element(), 'cancel-dialog-free': element({ hidden: true }),
        'cancel-dialog-charge': element({ hidden: true }), 'cancel-dialog-note': element(),
        'cancel-dialog-error': element({ hidden: true, dataset: { missingReason: 'Hãy chọn lý do hủy món', missingConfirm: 'hãy đánh dấu xác nhận huỷ có tính tiền' } }),
        'cancel-dialog-charged-confirm': element({ hidden: true }),
        'cancel-confirm-charged': element({ name: 'confirmCharged', disabled: true }),
        'cancel-dialog-charged-amount': element()
    };
    const documentListeners = {};
    const document = { getElementById: id => byId[id] || null, addEventListener(name, callback) { (documentListeners[name] ??= []).push(callback); } };
    const window = element();
    vm.runInNewContext(source, { document, window, Array });
    const click = target => (documentListeners.click || []).forEach(callback => callback({ target, preventDefault() {} }));
    const button = (status, id = '50') => element({
        dataset: { cancelItem: id, cancelUrl: `/ProvisionalBill/7/CancelItem/${id}`, cancelName: 'Lẩu thái', cancelQuantity: '1 nồi',
            cancelAmount: '250.000 ₫', cancelStatus: status, cancelCharged: status === 'Pending' ? 'false' : 'true' }
    });
    return { reasons, labels, confirm, form, byId, window, click, button, api: window.ProvisionalBillCancel };
}

test('manager charged cancel: only the three reasons, a charged confirmation is required', () => {
    for (const status of ['Preparing', 'Ready']) {
        const t = setupManagerWithConfirm();
        t.click(t.button(status));
        assert.equal(t.api.isCharged(), true);
        assert.equal(t.labels[3].hidden, true, '"Quản lý yêu cầu" is hidden for a charged cancellation');
        assert.equal(t.reasons[3].disabled, true);
        assert.ok(t.labels.slice(0, 3).every(label => !label.hidden));
        assert.equal(t.byId['cancel-dialog-charged-confirm'].hidden, false);
        assert.equal(t.byId['cancel-confirm-charged'].disabled, false);
        assert.equal(t.byId['cancel-confirm-charged'].checked, false, 'the charged confirmation is not pre-ticked');
        assert.equal(t.byId['cancel-dialog-charged-amount'].textContent, '250.000 ₫');
        assert.equal(t.confirm.textContent, 'Xác nhận hủy có tính tiền');
        t.reasons[0].checked = true;
        fire(t.form, 'change', { target: t.reasons[0] });
        assert.equal(t.confirm.disabled, true, 'a reason alone is not enough');
        const blocked = fire(t.form, 'submit');
        assert.equal(blocked.prevented, true);
        assert.equal(t.byId['cancel-dialog-error'].textContent, 'hãy đánh dấu xác nhận huỷ có tính tiền');
        t.byId['cancel-confirm-charged'].checked = true;
        fire(t.form, 'change', { target: t.byId['cancel-confirm-charged'] });
        assert.equal(t.confirm.disabled, false);
        assert.equal(fire(t.form, 'submit').prevented, false, 'reason + confirmation sends the request');
    }
});

test('manager charged cancel without a reason explains the missing reason first', () => {
    const t = setupManagerWithConfirm();
    t.click(t.button('Preparing'));
    t.byId['cancel-confirm-charged'].checked = true;
    fire(t.form, 'change', { target: t.byId['cancel-confirm-charged'] });
    assert.equal(t.confirm.disabled, true);
    assert.equal(fire(t.form, 'submit').prevented, true);
    assert.equal(t.byId['cancel-dialog-error'].textContent, 'Hãy chọn lý do hủy món');
});

test('reopening a pending line restores the four manager reasons and drops the charged confirmation', () => {
    const t = setupManagerWithConfirm();
    t.click(t.button('Preparing', '51'));
    t.api.close();
    t.click(t.button('Pending', '52'));
    assert.equal(t.api.isCharged(), false);
    assert.ok(t.labels.every(label => !label.hidden) && t.reasons.every(reason => !reason.disabled));
    assert.equal(t.byId['cancel-dialog-charged-confirm'].hidden, true);
    assert.equal(t.byId['cancel-confirm-charged'].disabled, true, 'the confirmation is not sent for a free cancellation');
    t.reasons[3].checked = true;
    fire(t.form, 'change', { target: t.reasons[3] });
    assert.equal(t.confirm.disabled, false);
    assert.equal(t.confirm.textContent, 'Xác nhận hủy');
});

test('helper setupManager keeps the old manager dialog working', () => {
    const t = setupManager();
    t.clickDocument(t.button('Pending'));
    assert.equal(t.reasons.length, 4);
});
