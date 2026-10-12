// S3-01 Task 1: giỏ món của khách trên trang gọi món tại bàn (/TableOrder).
// Giỏ lưu tạm trong sessionStorage theo phiên bàn (tải lại trang không mất), xoá sau khi đặt món thành công.
// Máy chủ (usp_SubmitOrder) kiểm tra lại món, giá, số lượng; giá ở đây chỉ để khách xem tạm tính.
// S3-03 Task 1: mỗi dòng món có ô ghi chú riêng (tối đa 200 ký tự). Hai dòng cùng món nhưng khác ghi chú là hai dòng
// riêng, không gộp số lượng. Bấm "Đặt món" mở bước xác nhận liệt kê món + ghi chú; khách bấm "Xác nhận gửi bếp" mới gửi.
// S3-03 Task 2: mỗi dòng có thêm các nút lựa chọn nhanh ("Ít cay", "Không hành", "Không mỡ"), chọn được nhiều nút; lựa chọn
// nhanh đi kèm ghi chú tự do của CHÍNH dòng đó và được gửi riêng (quickNotes) để không chiếm 200 ký tự của ghi chú tự do.
(function (global) {
    'use strict';

    var MAX_QTY = 99, MAX_NOTES = 200, MAX_LINES = 100;
    // Danh sách mặc định; trang thật lấy từ data-quick-notes (GuestOrderNotes.QuickOptions ở máy chủ).
    var quickOptions = [
        { code: 'it-cay', label: 'Ít cay' },
        { code: 'khong-hanh', label: 'Không hành' },
        { code: 'khong-mo', label: 'Không mỡ' }
    ];

    // ---- Logic giỏ món (không phụ thuộc DOM; tools/tests/table-order-notes.test.cjs kiểm thử trực tiếp) ----
    var Cart = {
        MAX_QTY: MAX_QTY, MAX_NOTES: MAX_NOTES, MAX_LINES: MAX_LINES,

        /** Ghi chú khi khách đang gõ: dạng dựng sẵn (NFC), xuống dòng/tab → khoảng trắng, cắt ở 200 ký tự. Không bỏ khoảng trắng cuối (khách đang gõ dở). */
        cleanNoteInput: function (text) {
            var value = String(text == null ? '' : text);
            if (typeof value.normalize === 'function') value = value.normalize('NFC');
            value = value.replace(/[\u0000-\u001f\u007f-\u009f]/g, ' ');
            var truncated = value.length > MAX_NOTES;
            if (truncated) {
                var end = MAX_NOTES;
                // Không cắt đôi một ký tự ngoài BMP (emoji): bỏ cả nửa đầu của cặp surrogate.
                var code = value.charCodeAt(end - 1);
                if (code >= 0xd800 && code <= 0xdbff) end -= 1;
                value = value.slice(0, end);
            }
            return { value: value, truncated: truncated };
        },

        /** Ghi chú gửi lên máy chủ: bỏ khoảng trắng hai đầu; rỗng → null (lưu "không có ghi chú"). */
        noteForSubmit: function (text) {
            var value = Cart.cleanNoteInput(text).value.trim();
            return value === '' ? null : value;
        },

        /** Danh sách lựa chọn nhanh [{code, label}] theo thứ tự hiển thị. */
        quickOptions: function () { return quickOptions.slice(); },

        /** Đặt danh sách lựa chọn nhanh (từ máy chủ). Bỏ qua dữ liệu hỏng, giữ danh sách mặc định. */
        setQuickOptions: function (options) {
            if (!Array.isArray(options)) return;
            var clean = options.map(function (o) {
                return o && { code: String(o.code || o.Code || ''), label: String(o.label || o.Label || '') };
            }).filter(function (o) { return o && o.code && o.label; });
            if (clean.length > 0) quickOptions = clean;
        },

        /** Lựa chọn nhanh hợp lệ của một dòng: chỉ mã có trong danh sách, không trùng, theo thứ tự hiển thị. */
        cleanQuick: function (codes) {
            var chosen = Array.isArray(codes) ? codes.map(String) : [];
            return quickOptions.filter(function (o) { return chosen.indexOf(o.code) >= 0; }).map(function (o) { return o.code; });
        },

        /** Bật/tắt một lựa chọn nhanh trên một dòng; trả về true nếu sau đó đang được chọn. */
        toggleQuick: function (line, code) {
            if (!line || !quickOptions.some(function (o) { return o.code === code; })) return false;
            var current = Cart.cleanQuick(line.quick);
            var on = current.indexOf(code) < 0;
            line.quick = Cart.cleanQuick(on ? current.concat([code]) : current.filter(function (c) { return c !== code; }));
            return on;
        },

        /** Nhãn các lựa chọn nhanh đã chọn của dòng, ví dụ ["Ít cay", "Không hành"]. */
        quickLabels: function (line) {
            var chosen = Cart.cleanQuick(line && line.quick);
            return quickOptions.filter(function (o) { return chosen.indexOf(o.code) >= 0; }).map(function (o) { return o.label; });
        },

        /** Dòng đã có yêu cầu riêng (ghi chú tự do hoặc lựa chọn nhanh) thì không được cộng dồn phần mới vào. */
        hasNote: function (line) {
            return Cart.noteForSubmit(line && line.notes) !== null || Cart.cleanQuick(line && line.quick).length > 0;
        },

        /**
         * "+ Thêm vào giỏ": cộng vào dòng cùng món CHƯA có ghi chú; nếu mọi dòng của món đã có ghi chú thì thêm dòng mới
         * (để ghi chú "ít cay" của dòng cũ không bị áp cho phần mới). Trả về vị trí dòng, -1 khi giỏ đã đủ 100 dòng.
         */
        addDish: function (cart, dish) {
            for (var i = 0; i < cart.length; i++) {
                if (cart[i].dishId === dish.dishId && !Cart.hasNote(cart[i])) {
                    cart[i].quantity = Math.min(MAX_QTY, cart[i].quantity + 1);
                    return i;
                }
            }
            if (cart.length >= MAX_LINES) return -1;
            cart.push({ dishId: dish.dishId, name: dish.name, price: dish.price, quantity: 1, notes: '', quick: [] });
            return cart.length - 1;
        },

        /** "+ Dòng ghi chú khác": thêm một dòng cùng món, 1 phần, ghi chú trống, ngay dưới dòng đang xem. */
        addSeparateLine: function (cart, index) {
            var line = cart[index];
            if (!line || cart.length >= MAX_LINES) return -1;
            cart.splice(index + 1, 0, { dishId: line.dishId, name: line.name, price: line.price, quantity: 1, notes: '', quick: [] });
            return index + 1;
        },

        changeQuantity: function (cart, index, delta) {
            var line = cart[index];
            if (!line) return;
            line.quantity = Math.min(MAX_QTY, line.quantity + delta);
            if (line.quantity <= 0) cart.splice(index, 1);
        },

        total: function (cart) { return cart.reduce(function (sum, l) { return sum + l.price * l.quantity; }, 0); },
        count: function (cart) { return cart.reduce(function (sum, l) { return sum + l.quantity; }, 0); },

        /** Giỏ gửi máy chủ: giữ nguyên từng dòng và thứ tự — không gộp hai dòng cùng món. */
        payload: function (cart) {
            return cart.map(function (l) {
                return { dishId: l.dishId, quantity: l.quantity, notes: Cart.noteForSubmit(l.notes), quickNotes: Cart.cleanQuick(l.quick) };
            });
        },

        /** Đọc lại giỏ đã lưu: bỏ dòng hỏng, ghi chú cũ quá dài được cắt về 200 ký tự. */
        sanitize: function (saved) {
            if (!Array.isArray(saved)) return [];
            return saved.filter(function (l) {
                return l && typeof l.dishId === 'number' && l.dishId > 0 && typeof l.quantity === 'number' && l.quantity > 0;
            }).slice(0, MAX_LINES).map(function (l) {
                return {
                    dishId: l.dishId, name: String(l.name || ''), price: Number(l.price) || 0,
                    quantity: Math.min(MAX_QTY, Math.floor(l.quantity)), notes: Cart.cleanNoteInput(l.notes).value,
                    quick: Cart.cleanQuick(l.quick)
                };
            });
        }
    };
    global.TableOrderCart = Cart;

    // ---- Giao diện ----
    var document = global.document;
    var cartSection = document && document.getElementById('gio-mon');
    if (!cartSection) return;

    var key = cartSection.getAttribute('data-cart-key');
    try { Cart.setQuickOptions(JSON.parse(cartSection.getAttribute('data-quick-notes') || 'null')); } catch (e) { /* giữ mặc định */ }
    var editor = cartSection.querySelector('[data-cart-editor]');
    var list = cartSection.querySelector('[data-cart-list]');
    var empty = cartSection.querySelector('[data-cart-empty]');
    var totalEl = cartSection.querySelector('[data-cart-total]');
    var review = cartSection.querySelector('[data-cart-review]');
    var reviewList = cartSection.querySelector('[data-review-list]');
    var reviewTitle = cartSection.querySelector('[data-review-title]');
    var form = cartSection.querySelector('[data-cart-form]');
    var jsonInput = cartSection.querySelector('[data-cart-json]');
    var openReview = cartSection.querySelector('[data-cart-review-open]');
    var backButton = cartSection.querySelector('[data-cart-review-back]');
    var submit = cartSection.querySelector('[data-cart-submit]');
    var counters = document.querySelectorAll('[data-cart-count]');
    var cart = [];
    var reviewing = false;
    var sending = false;

    function money(n) { return Number(n).toLocaleString('vi-VN') + ' ₫'; }
    function save() { try { global.sessionStorage.setItem(key, JSON.stringify(cart)); } catch (e) { /* trình duyệt chặn lưu tạm */ } }
    function load() {
        try {
            if (cartSection.getAttribute('data-clear-cart') === 'true') { global.sessionStorage.removeItem(key); return []; }
            var raw = global.sessionStorage.getItem(key);
            return Cart.sanitize(raw ? JSON.parse(raw) : []);
        } catch (e) { return []; }
    }

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        if (text != null) node.textContent = text;
        return node;
    }

    function counterText(length) {
        return length >= MAX_NOTES ? length + '/' + MAX_NOTES + ' – đã đủ ' + MAX_NOTES + ' ký tự' : length + '/' + MAX_NOTES;
    }

    function noteField(line, index) {
        var wrap = el('div', 'table-order-note-field');
        var id = 'ghi-chu-' + index;
        var label = el('label', 'visually-hidden', 'Ghi chú cho ' + line.name + ' (dòng ' + (index + 1) + ')');
        label.setAttribute('for', id);
        var input = el('input', 'table-order-note-input');
        input.type = 'text'; input.id = id; input.maxLength = MAX_NOTES; input.autocomplete = 'off';
        input.placeholder = 'Ghi chú thêm (ví dụ: thêm chanh, mang ra sau)';
        input.value = line.notes || '';
        input.setAttribute('data-cart-note', String(index));
        var counter = el('span', 'table-order-note-count', counterText(input.value.length));
        counter.id = id + '-dem';
        counter.setAttribute('data-note-count', '');
        counter.setAttribute('aria-live', 'polite');
        input.setAttribute('aria-describedby', counter.id);
        var warning = el('span', 'table-order-note-warning', 'Ghi chú tối đa ' + MAX_NOTES + ' ký tự – phần vượt quá không được nhận.');
        warning.setAttribute('data-note-warning', '');
        warning.setAttribute('role', 'alert');
        warning.hidden = true;

        function apply() {
            var cleaned = Cart.cleanNoteInput(input.value);
            // Ô có maxlength=200 đã chặn gõ/dán thêm; NFC hoặc bộ gõ tiếng Việt vẫn có thể làm chuỗi dài hơn → cắt lại.
            var full = cleaned.truncated || cleaned.value.length >= MAX_NOTES;
            if (cleaned.value !== input.value) input.value = cleaned.value;
            line.notes = cleaned.value;
            counter.textContent = counterText(cleaned.value.length);
            counter.classList.toggle('is-full', full);
            warning.hidden = !full;
            save();
        }
        input.addEventListener('input', function (event) { if (!event || !event.isComposing) apply(); });
        input.addEventListener('compositionend', apply);
        wrap.appendChild(quickGroup(line, index));
        wrap.appendChild(label); wrap.appendChild(input); wrap.appendChild(counter); wrap.appendChild(warning);
        return wrap;
    }

    // S3-03 Task 2: các nút lựa chọn nhanh của MỘT dòng (bật/tắt, chọn được nhiều nút). Chỉ đổi line.quick của dòng này.
    function quickGroup(line, index) {
        var group = el('div', 'table-order-quick');
        group.setAttribute('role', 'group');
        group.setAttribute('aria-label', 'Lựa chọn nhanh cho ' + line.name + ' (dòng ' + (index + 1) + ')');
        group.setAttribute('data-cart-quick', String(index));
        var chosen = Cart.cleanQuick(line.quick);
        Cart.quickOptions().forEach(function (option) {
            var button = el('button', 'table-order-quick-option', option.label);
            button.type = 'button';
            button.setAttribute('data-quick-code', option.code);
            var on = chosen.indexOf(option.code) >= 0;
            button.setAttribute('aria-pressed', on ? 'true' : 'false');
            button.classList.toggle('is-on', on);
            button.addEventListener('click', function () {
                var nowOn = Cart.toggleQuick(line, option.code);
                button.setAttribute('aria-pressed', nowOn ? 'true' : 'false');
                button.classList.toggle('is-on', nowOn);
                save();
            });
            group.appendChild(button);
        });
        return group;
    }

    function renderEditor() {
        list.textContent = '';
        cart.forEach(function (line, index) {
            var li = el('li', 'table-order-cart-item');
            li.setAttribute('data-cart-line', String(index));

            var head = el('div', 'table-order-cart-head');
            head.appendChild(el('strong', null, line.name));
            head.appendChild(el('span', null, money(line.price * line.quantity)));

            var controls = el('div', 'table-order-qty');
            var minus = el('button', null, '−');
            minus.type = 'button'; minus.setAttribute('aria-label', 'Bớt một ' + line.name);
            minus.addEventListener('click', function () { Cart.changeQuantity(cart, index, -1); update(); });
            var qty = el('span', null, String(line.quantity));
            qty.setAttribute('aria-live', 'polite');
            var plus = el('button', null, '+');
            plus.type = 'button'; plus.setAttribute('aria-label', 'Thêm một ' + line.name);
            plus.disabled = line.quantity >= MAX_QTY;
            plus.addEventListener('click', function () { Cart.changeQuantity(cart, index, 1); update(); });
            var split = el('button', 'table-order-split', '+ Dòng ghi chú khác');
            split.type = 'button';
            split.setAttribute('data-cart-split', String(index));
            split.setAttribute('aria-label', 'Thêm một dòng ' + line.name + ' với ghi chú khác');
            split.disabled = cart.length >= Cart.MAX_LINES;
            split.addEventListener('click', function () {
                var added = Cart.addSeparateLine(cart, index);
                update();
                focusNote(added);
            });
            var remove = el('button', 'table-order-remove', 'Bỏ');
            remove.type = 'button'; remove.setAttribute('aria-label', 'Bỏ ' + line.name + ' khỏi giỏ');
            remove.addEventListener('click', function () { cart.splice(index, 1); update(); });
            controls.appendChild(minus); controls.appendChild(qty); controls.appendChild(plus);
            controls.appendChild(split); controls.appendChild(remove);

            li.appendChild(head); li.appendChild(controls); li.appendChild(noteField(line, index));
            list.appendChild(li);
        });
    }

    function renderReview() {
        reviewList.textContent = '';
        cart.forEach(function (line) {
            var li = el('li', 'table-order-review-item');
            li.setAttribute('data-review-line', '');
            var head = el('div', 'table-order-cart-head');
            head.appendChild(el('strong', null, line.quantity + ' × ' + line.name));
            head.appendChild(el('span', null, money(line.price * line.quantity)));
            li.appendChild(head);
            var quick = Cart.quickLabels(line);
            if (quick.length > 0) {
                var quickEl = el('span', 'table-order-item-quick', quick.join(' · '));
                quickEl.setAttribute('data-review-quick', '');
                li.appendChild(quickEl);
            }
            var note = Cart.noteForSubmit(line.notes);
            if (note || quick.length === 0) {
                var noteEl = el('span', note ? 'table-order-item-note' : 'table-order-item-note is-empty', note ? 'Ghi chú: ' + note : 'Không có ghi chú');
                noteEl.setAttribute('data-review-note', '');
                li.appendChild(noteEl);
            }
            reviewList.appendChild(li);
        });
    }

    function render() {
        if (cart.length === 0) reviewing = false;
        renderEditor();
        if (reviewing) renderReview();
        empty.hidden = cart.length > 0;
        editor.hidden = reviewing;
        review.hidden = !reviewing;
        totalEl.textContent = money(Cart.total(cart));
        openReview.hidden = reviewing;
        openReview.disabled = cart.length === 0;
        backButton.hidden = !reviewing;
        submit.hidden = !reviewing;
        submit.disabled = !reviewing || cart.length === 0 || sending;
        var count = Cart.count(cart);
        counters.forEach(function (c) { c.textContent = count; });
    }

    function update() { save(); render(); }

    function focusNote(index) {
        var input = list.querySelector('[data-cart-note="' + index + '"]');
        if (input && typeof input.focus === 'function') input.focus();
    }

    document.querySelectorAll('[data-add-dish]').forEach(function (button) {
        button.addEventListener('click', function () {
            if (button.disabled) return;
            Cart.addDish(cart, {
                dishId: parseInt(button.getAttribute('data-add-dish'), 10),
                name: button.getAttribute('data-name'),
                price: parseInt(button.getAttribute('data-price'), 10) || 0
            });
            update();
            var original = button.textContent;
            button.textContent = '✓ Đã thêm';
            setTimeout(function () { button.textContent = original; }, 900);
        });
    });

    // Bước xác nhận: khách xem lại món, số lượng và ghi chú từng dòng trước khi gửi bếp.
    openReview.addEventListener('click', function () {
        if (cart.length === 0) return;
        reviewing = true;
        render();
        if (reviewTitle && typeof reviewTitle.focus === 'function') reviewTitle.focus();
    });
    backButton.addEventListener('click', function () {
        reviewing = false;
        render();
        if (typeof openReview.focus === 'function') openReview.focus();
    });

    // Món vừa chuyển "Tạm hết" (menu-availability.js): bỏ khỏi giỏ để không gửi order bị từ chối.
    document.addEventListener('menu-availability:changed', function (event) {
        var out = event.detail && event.detail.unavailable;
        if (!out || typeof out.has !== 'function') return;
        var before = cart.length;
        cart = cart.filter(function (l) { return !out.has(l.dishId); });
        if (cart.length !== before) update();
    });

    form.addEventListener('submit', function (event) {
        // Chỉ gửi từ bước xác nhận; Enter ở nơi khác chỉ mở bước xác nhận.
        if (sending || cart.length === 0 || !reviewing) {
            event.preventDefault();
            if (!sending && cart.length > 0 && !reviewing) openReview.click();
            return;
        }
        sending = true;
        jsonInput.value = JSON.stringify(Cart.payload(cart));
        setTimeout(function () { submit.disabled = true; submit.textContent = 'Đang gửi…'; }, 0);
    });

    cart = load();
    render();
})(typeof window !== 'undefined' ? window : globalThis);
