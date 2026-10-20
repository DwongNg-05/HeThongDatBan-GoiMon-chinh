// Ô ngày / giờ theo kiểu Việt Nam cho toàn hệ thống: ngày hiện "dd/mm/yyyy" (ngày trước, tháng sau), giờ hiện 24 giờ "HH:mm".
// Ô <input type="date"> / <input type="time"> gốc của trình duyệt hiển thị theo ngôn ngữ máy (ví dụ máy tiếng Anh: 10/20/2026, 08:30 AM),
// nên mỗi ô gốc được giữ lại (ẩn, vẫn gửi về máy chủ đúng dạng yyyy-MM-dd / HH:mm và vẫn phát sự kiện input/change như cũ),
// và đặt cạnh nó một ô chữ để khách gõ hoặc bấm biểu tượng lịch / đồng hồ để chọn.
// Ô nào không muốn đổi: thêm thuộc tính data-native-date.
(function () {
    'use strict';

    var CALENDAR = '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><rect x="3.5" y="5" width="17" height="15.5" rx="1.5"/><path d="M3.5 9.5h17M8 3v4M16 3v4"/></svg>';
    var CLOCK = '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/></svg>';

    function pad(n) { return String(n).padStart(2, '0'); }

    // Chia chữ đang gõ thành các phần (ngày/tháng/năm hoặc giờ:phút): đủ số chữ số thì tự sang phần sau,
    // gõ dấu phân cách thì sang phần sau ngay (cho phép "5/11/2026", "9:30").
    function segmentMask(text, sizes, sep, sepPattern) {
        var parts = [''], i = 0;
        for (var k = 0; k < text.length; k++) {
            var ch = text.charAt(k);
            if (/\d/.test(ch)) {
                if (parts[i].length >= sizes[i]) {
                    if (i === sizes.length - 1) continue;
                    parts[++i] = '';
                }
                parts[i] += ch;
            } else if (sepPattern.test(ch) && parts[i].length > 0 && i < sizes.length - 1) {
                parts[++i] = '';
            }
        }
        return parts.join(sep);
    }

    var kinds = {
        date: {
            placeholder: 'dd/mm/yyyy',
            maxLength: 10,
            button: 'Chọn ngày',
            icon: CALENDAR,
            invalid: 'Nhập ngày theo dạng ngày/tháng/năm, ví dụ 20/10/2026.',
            // yyyy-MM-dd → dd/MM/yyyy
            toText: function (value) {
                var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value || '');
                return m ? m[3] + '/' + m[2] + '/' + m[1] : '';
            },
            // dd/MM/yyyy (chấp nhận 1 chữ số cho ngày, tháng) → yyyy-MM-dd, sai ngày thì null
            toValue: function (text) {
                var m = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec((text || '').trim());
                if (!m) return null;
                var d = +m[1], mo = +m[2], y = +m[3];
                var check = new Date(Date.UTC(y, mo - 1, d));
                if (check.getUTCFullYear() !== y || check.getUTCMonth() !== mo - 1 || check.getUTCDate() !== d) return null;
                return y + '-' + pad(mo) + '-' + pad(d);
            },
            // Tự thêm dấu "/" khi gõ: 20102026 → 20/10/2026; gõ tay "5/11/2026" vẫn được.
            mask: function (text) { return segmentMask(text, [2, 2, 4], '/', /\//); },
            complete: function (text) { return /^\d{1,2}\/\d{1,2}\/\d{4}$/.test(text); }
        },
        time: {
            placeholder: 'hh:mm',
            maxLength: 5,
            button: 'Chọn giờ',
            icon: CLOCK,
            invalid: 'Nhập giờ theo dạng 24 giờ, ví dụ 19:30.',
            // HH:mm hoặc HH:mm:ss → HH:mm
            toText: function (value) {
                var m = /^(\d{2}):(\d{2})/.exec(value || '');
                return m ? m[1] + ':' + m[2] : '';
            },
            toValue: function (text) {
                var m = /^(\d{1,2})[:hH.](\d{2})$/.exec((text || '').trim());
                if (!m || +m[1] > 23 || +m[2] > 59) return null;
                return pad(+m[1]) + ':' + m[2];
            },
            mask: function (text) { return segmentMask(text, [2, 2], ':', /[:hH.]/); },
            complete: function (text) { return /^\d{1,2}:\d{2}$/.test(text); }
        }
    };

    function fire(input, type) {
        input.dispatchEvent(new Event(type, { bubbles: true }));
    }

    function enhance(input) {
        var kind = kinds[input.type];
        if (!kind || input.hasAttribute('data-native-date') || input.dataset.vnDate) return;
        input.dataset.vnDate = '1';

        var display = getComputedStyle(input).display;
        var wrap = document.createElement('span');
        wrap.className = 'vn-date';
        if (display === 'inline' || display === 'inline-block') wrap.classList.add('vn-date--inline');
        input.parentNode.insertBefore(wrap, input);
        wrap.appendChild(input);

        var text = document.createElement('input');
        text.type = 'text';
        text.className = input.className + ' vn-date__text';
        text.placeholder = kind.placeholder;
        text.maxLength = kind.maxLength;
        text.inputMode = 'numeric';
        text.autocomplete = 'off';
        text.spellcheck = false;
        text.disabled = input.disabled;
        text.readOnly = input.readOnly;
        if (input.required) { text.required = true; input.required = false; input.dataset.vnRequired = '1'; }
        if (input.getAttribute('aria-describedby')) text.setAttribute('aria-describedby', input.getAttribute('aria-describedby'));
        if (input.id) {
            text.id = input.id + '__vn';
            document.querySelectorAll('label[for="' + CSS.escape(input.id) + '"]').forEach(function (label) { label.htmlFor = text.id; });
        }
        if (input.getAttribute('aria-label')) text.setAttribute('aria-label', input.getAttribute('aria-label'));
        if (input.autofocus) text.autofocus = true;

        var button = document.createElement('button');
        button.type = 'button';
        button.className = 'vn-date__btn';
        button.setAttribute('aria-label', kind.button);
        button.title = kind.button;
        button.tabIndex = -1;
        button.innerHTML = kind.icon;
        button.disabled = input.disabled;

        input.classList.add('vn-date__native');
        input.tabIndex = -1;
        input.setAttribute('aria-hidden', 'true');
        wrap.appendChild(text);
        wrap.appendChild(button);

        // Kiểm tra định dạng + giới hạn min/max (thông báo bằng tiếng Việt, ngày theo dd/mm/yyyy).
        function validate() {
            var message = '';
            var raw = text.value.trim();
            if (raw) {
                var value = kind.toValue(raw);
                if (!value) message = kind.invalid;
                else if (input.min && value < input.min) message = (input.type === 'date' ? 'Ngày' : 'Giờ') + ' phải từ ' + kind.toText(input.min) + ' trở đi.';
                else if (input.max && value > input.max) message = (input.type === 'date' ? 'Ngày' : 'Giờ') + ' không được sau ' + kind.toText(input.max) + '.';
            }
            text.setCustomValidity(message);
            return message === '';
        }

        var syncing = false;
        function fromNative() {
            if (syncing) return;
            text.value = kind.toText(input.value);
            validate();
        }
        function toNative(eventType) {
            var raw = text.value.trim();
            var value = raw ? kind.toValue(raw) : '';
            if (value === null) { validate(); return; }
            if (input.value !== value) {
                syncing = true;
                input.value = value;
                syncing = false;
                fire(input, 'input');
                if (eventType === 'change') fire(input, 'change');
            }
            validate();
        }

        // Các script khác gán thẳng input.value (ví dụ đổi ngày bằng nút Hôm nay / Ngày mai): cập nhật ô chữ theo.
        var descriptor = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
        Object.defineProperty(input, 'value', {
            configurable: true,
            get: function () { return descriptor.get.call(this); },
            set: function (v) { descriptor.set.call(this, v); fromNative(); }
        });

        text.addEventListener('input', function () {
            var masked = kind.mask(text.value);
            if (masked !== text.value) text.value = masked;
            if (kind.complete(text.value)) toNative('input');
            else text.setCustomValidity('');
        });
        text.addEventListener('change', function () { toNative('change'); });
        text.addEventListener('blur', function () {
            var value = kind.toValue(text.value);
            if (value) text.value = kind.toText(value);
            validate();
        });
        input.addEventListener('input', fromNative);
        input.addEventListener('change', fromNative);

        button.addEventListener('click', function () {
            if (typeof input.showPicker === 'function') {
                try { input.showPicker(); return; } catch (e) { /* trình duyệt không cho mở: chuyển sang gõ tay */ }
            }
            text.focus();
        });

        // Form bị reset: ô chữ theo giá trị gốc.
        if (input.form) input.form.addEventListener('reset', function () { setTimeout(fromNative, 0); });

        fromNative();
        text.setCustomValidity('');
    }

    function run(root) {
        (root || document).querySelectorAll('input[type="date"], input[type="time"]').forEach(enhance);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { run(); });
    else run();

    // Ô ngày / giờ thêm vào trang sau (hộp thoại, nội dung tải bằng fetch).
    if ('MutationObserver' in window) {
        new MutationObserver(function (mutations) {
            mutations.forEach(function (m) {
                m.addedNodes.forEach(function (node) {
                    if (node.nodeType !== 1) return;
                    if (node.matches && node.matches('input[type="date"], input[type="time"]')) enhance(node);
                    else if (node.querySelectorAll) run(node);
                });
            });
        }).observe(document.documentElement, { childList: true, subtree: true });
    }

    window.vnDate = { enhance: run };
})();
