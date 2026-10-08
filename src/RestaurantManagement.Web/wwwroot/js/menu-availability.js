// S2-08 Task 1: trạng thái "Tạm hết" của món cập nhật tự động (tối đa 5 giây) trên thực đơn công khai,
// màn hình gọi món và danh sách món trong ngày; bật/tắt "Tạm hết" một chạm trên danh sách món trong ngày.
//
// Trang khai báo:  <div data-availability-url="/api/menu/availability" [data-availability-interval="3000"]>
// Mỗi món:          <… data-availability-id="12">, bên trong có thể có
//   [data-availability-label]       nhãn "Tạm hết" (ẩn khi món còn phục vụ)
//   [data-availability-disable]     nút/ô bị khoá khi món không nhận order
//   [data-sold-out-today-note]      ghi chú "Quản lý đã báo hết trong ngày"
//   form[data-temporarily-out-toggle] nút một chạm bật/tắt (danh sách món trong ngày)
(function () {
    'use strict';
    var root = document.querySelector('[data-availability-url]');
    if (!root) return;
    var url = root.getAttribute('data-availability-url');
    var intervalMs = Number(root.getAttribute('data-availability-interval')) || 3000;
    var busy = false;

    function setDish(el, state) {
        var out = !!state.unavailable;
        el.setAttribute('data-unavailable', out ? 'true' : 'false');
        if (el.hasAttribute('data-sold-out')) el.setAttribute('data-sold-out', out ? 'true' : 'false');
        el.querySelectorAll('[data-availability-label]').forEach(function (label) { label.hidden = !out; });
        if (el.classList.contains('public-dish')) setPublicLabel(el, out);
        el.querySelectorAll('[data-availability-disable]').forEach(function (control) { control.disabled = out; });
        el.querySelectorAll('[data-sold-out-today-note]').forEach(function (note) { note.hidden = !state.soldOutToday; });
        el.querySelectorAll('form[data-temporarily-out-toggle]').forEach(function (form) {
            var on = !!state.temporarilyOut;
            el.setAttribute('data-temporarily-out', on ? 'true' : 'false');
            var input = form.querySelector('input[name="isTemporarilyOut"]');
            if (input) input.value = on ? 'false' : 'true';
            var button = form.querySelector('button');
            if (!button) return;
            button.textContent = on ? button.getAttribute('data-text-on') : button.getAttribute('data-text-off');
            button.setAttribute('aria-pressed', on ? 'true' : 'false');
            button.classList.toggle('btn-danger', !on);
            button.classList.toggle('btn-success', on);
        });
    }

    // Thực đơn công khai: máy chủ chỉ in nhãn "Tạm hết" cho món đang hết, nên thêm/bỏ nhãn ngay trên trang.
    function setPublicLabel(el, out) {
        var body = el.querySelector('.public-dish-body');
        if (!body) return;
        var label = body.querySelector('.public-dish-status');
        if (out && !label) {
            label = document.createElement('span');
            label.className = 'public-dish-status';
            label.textContent = root.getAttribute('data-availability-text') || 'Tạm hết';
            var note = document.createElement('span');
            note.className = 'visually-hidden';
            note.textContent = ' – ' + (root.getAttribute('data-availability-note') || '');
            label.appendChild(note);
            body.insertBefore(label, body.firstChild);
        } else if (!out && label) {
            label.remove();
        }
    }

    function updateCount() {
        var counter = document.querySelector('[data-unavailable-count]');
        if (counter) counter.textContent = String(document.querySelectorAll('[data-availability-id][data-unavailable="true"]').length);
    }

    function apply(data) {
        var unavailable = new Set(data.unavailable || []);
        var temporarilyOut = new Set(data.temporarilyOut || []);
        var soldOutToday = new Set(data.soldOutToday || []);
        document.querySelectorAll('[data-availability-id]').forEach(function (el) {
            var id = Number(el.getAttribute('data-availability-id'));
            setDish(el, { unavailable: unavailable.has(id), temporarilyOut: temporarilyOut.has(id), soldOutToday: soldOutToday.has(id) });
        });
        updateCount();
        document.dispatchEvent(new CustomEvent('menu-availability:changed', { detail: { unavailable: unavailable } }));
    }

    function refresh() {
        if (busy) return;
        busy = true;
        fetch(url, { cache: 'no-store', credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (response) { return response.ok ? response.json() : null; })
            .then(function (data) { if (data) apply(data); })
            .catch(function () { /* mất mạng tạm thời: lần gọi sau sẽ cập nhật */ })
            .then(function () { busy = false; });
    }

    function showStatus(message, ok) {
        var status = document.getElementById('daily-dishes-status');
        if (!status || !message) return;
        status.textContent = message;
        status.className = 'alert ' + (ok ? 'alert-success' : 'alert-danger');
        status.hidden = false;
    }

    // Một chạm bật/tắt "Tạm hết": gửi bằng fetch, cập nhật ngay dòng món, không tải lại trang.
    document.addEventListener('submit', function (event) {
        var form = event.target.closest ? event.target.closest('form[data-temporarily-out-toggle]') : null;
        if (!form || !window.fetch) return;
        event.preventDefault();
        var row = form.closest('[data-availability-id]');
        var button = form.querySelector('button');
        if (button) button.disabled = true;
        fetch(form.action, {
            method: 'POST', body: new FormData(form), credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' }
        }).then(function (response) {
            if (response.status === 401) { window.location.reload(); return null; }
            return response.json().catch(function () { return {}; }).then(function (data) { return { ok: response.ok, data: data }; });
        }).then(function (result) {
            if (!result) return;
            if (result.ok && row) {
                var soldOutToday = !!row.querySelector('[data-sold-out-today-note]:not([hidden])');
                setDish(row, { temporarilyOut: result.data.isTemporarilyOut, soldOutToday: soldOutToday, unavailable: result.data.isTemporarilyOut || soldOutToday });
                updateCount();
            }
            showStatus(result.data.message || (result.ok ? '' : 'Không thể cập nhật trạng thái món lúc này.'), result.ok);
        }).catch(function () {
            showStatus('Mất kết nối tới máy chủ. Vui lòng thử lại.', false);
        }).then(function () {
            if (button) button.disabled = false;
        });
    });

    window.menuAvailability = { refresh: refresh };
    setInterval(function () { if (!document.hidden) refresh(); }, intervalMs);
    document.addEventListener('visibilitychange', function () { if (!document.hidden) refresh(); });
    window.addEventListener('pageshow', function (event) { if (event.persisted) refresh(); });
    refresh();
})();
