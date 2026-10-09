// S3-01 Task 1: giỏ món của khách trên trang gọi món tại bàn (/TableOrder).
// Giỏ lưu tạm trong sessionStorage theo phiên bàn (tải lại trang không mất), xoá sau khi đặt món thành công.
// Máy chủ (usp_SubmitOrder) kiểm tra lại món, giá, số lượng; giá ở đây chỉ để khách xem tạm tính.
(function () {
    'use strict';
    var cartSection = document.getElementById('gio-mon');
    if (!cartSection) return;

    var MAX_QTY = 99, MAX_NOTES = 200, MAX_LINES = 100;
    var key = cartSection.getAttribute('data-cart-key');
    var list = cartSection.querySelector('[data-cart-list]');
    var empty = cartSection.querySelector('[data-cart-empty]');
    var totalEl = cartSection.querySelector('[data-cart-total]');
    var form = cartSection.querySelector('[data-cart-form]');
    var jsonInput = cartSection.querySelector('[data-cart-json]');
    var submit = cartSection.querySelector('[data-cart-submit]');
    var counters = document.querySelectorAll('[data-cart-count]');
    var cart = [];

    function money(n) { return Number(n).toLocaleString('vi-VN') + ' ₫'; }
    function save() { try { sessionStorage.setItem(key, JSON.stringify(cart)); } catch (e) { /* trình duyệt chặn lưu tạm */ } }
    function load() {
        try {
            if (cartSection.getAttribute('data-clear-cart') === 'true') { sessionStorage.removeItem(key); return []; }
            var raw = sessionStorage.getItem(key);
            var parsed = raw ? JSON.parse(raw) : [];
            return Array.isArray(parsed) ? parsed : [];
        } catch (e) { return []; }
    }

    function render() {
        list.textContent = '';
        var total = 0, count = 0;
        cart.forEach(function (line, index) {
            total += line.price * line.quantity;
            count += line.quantity;
            var li = document.createElement('li');
            li.className = 'table-order-cart-item';

            var head = document.createElement('div');
            head.className = 'table-order-cart-head';
            var name = document.createElement('strong');
            name.textContent = line.name;
            var price = document.createElement('span');
            price.textContent = money(line.price * line.quantity);
            head.appendChild(name); head.appendChild(price);

            var controls = document.createElement('div');
            controls.className = 'table-order-qty';
            var minus = document.createElement('button');
            minus.type = 'button'; minus.textContent = '−'; minus.setAttribute('aria-label', 'Bớt một ' + line.name);
            minus.addEventListener('click', function () { change(index, -1); });
            var qty = document.createElement('span');
            qty.textContent = line.quantity; qty.setAttribute('aria-live', 'polite');
            var plus = document.createElement('button');
            plus.type = 'button'; plus.textContent = '+'; plus.setAttribute('aria-label', 'Thêm một ' + line.name);
            plus.disabled = line.quantity >= MAX_QTY;
            plus.addEventListener('click', function () { change(index, 1); });
            var remove = document.createElement('button');
            remove.type = 'button'; remove.className = 'table-order-remove'; remove.textContent = 'Bỏ';
            remove.addEventListener('click', function () { cart.splice(index, 1); update(); });
            controls.appendChild(minus); controls.appendChild(qty); controls.appendChild(plus); controls.appendChild(remove);

            var note = document.createElement('input');
            note.type = 'text'; note.maxLength = MAX_NOTES; note.className = 'table-order-note-input';
            note.placeholder = 'Ghi chú (ví dụ: ít cay, không hành)';
            note.value = line.notes || '';
            note.setAttribute('aria-label', 'Ghi chú cho ' + line.name);
            note.addEventListener('input', function () { line.notes = note.value.slice(0, MAX_NOTES); save(); });

            li.appendChild(head); li.appendChild(controls); li.appendChild(note);
            list.appendChild(li);
        });
        empty.hidden = cart.length > 0;
        totalEl.textContent = money(total);
        submit.disabled = cart.length === 0;
        counters.forEach(function (c) { c.textContent = count; });
    }

    function update() { save(); render(); }

    function change(index, delta) {
        var line = cart[index];
        if (!line) return;
        line.quantity = Math.min(MAX_QTY, line.quantity + delta);
        if (line.quantity <= 0) cart.splice(index, 1);
        update();
    }

    document.querySelectorAll('[data-add-dish]').forEach(function (button) {
        button.addEventListener('click', function () {
            if (button.disabled) return;
            var id = parseInt(button.getAttribute('data-add-dish'), 10);
            var existing = cart.find(function (l) { return l.dishId === id; });
            if (existing) existing.quantity = Math.min(MAX_QTY, existing.quantity + 1);
            else if (cart.length < MAX_LINES) cart.push({
                dishId: id,
                name: button.getAttribute('data-name'),
                price: parseInt(button.getAttribute('data-price'), 10) || 0,
                quantity: 1,
                notes: ''
            });
            update();
            var original = button.textContent;
            button.textContent = '✓ Đã thêm';
            setTimeout(function () { button.textContent = original; }, 900);
        });
    });

    // Món vừa chuyển "Tạm hết" (menu-availability.js): bỏ khỏi giỏ để không gửi order bị từ chối.
    document.addEventListener('menu-availability:changed', function (event) {
        var out = event.detail && event.detail.unavailable;
        if (!out || typeof out.has !== 'function') return;
        var before = cart.length;
        cart = cart.filter(function (l) { return !out.has(l.dishId); });
        if (cart.length !== before) update();
    });

    var sending = false;
    form.addEventListener('submit', function (event) {
        if (sending || cart.length === 0) { event.preventDefault(); return; }
        sending = true;
        jsonInput.value = JSON.stringify(cart.map(function (l) {
            return { dishId: l.dishId, quantity: l.quantity, notes: (l.notes || '').trim() || null };
        }));
        setTimeout(function () { submit.disabled = true; submit.textContent = 'Đang gửi…'; }, 0);
    });

    cart = load().filter(function (l) { return l && l.dishId > 0 && l.quantity > 0; });
    render();
})();
