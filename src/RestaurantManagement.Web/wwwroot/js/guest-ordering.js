(() => {
    const root = document.querySelector('.guest-ordering');
    if (!root) return;

    const maximum = Number(root.dataset.maximumQuantity || 20);
    const cartStorageKey = 'GuestOrdering.Cart';
    const requestStorageKey = 'GuestOrdering.RequestId';
    const initial = document.querySelector('#initial-cart')?.textContent || '[]';
    let cart;
    try { cart = JSON.parse(initial); } catch { cart = []; }
    if ((!Array.isArray(cart) || cart.length === 0) && window.sessionStorage) {
        try { cart = JSON.parse(sessionStorage.getItem(cartStorageKey) || '[]'); } catch { cart = []; }
    }
    if (!Array.isArray(cart)) cart = [];
    cart = cart.filter(item => Number.isInteger(item.dishId) && Number.isInteger(item.quantity) && item.quantity > 0 && item.quantity <= maximum);
    const formatMoney = amount => `${new Intl.NumberFormat('vi-VN').format(amount)} ₫`;
    const lines = root.querySelector('[data-cart-lines]');
    const empty = root.querySelector('[data-cart-empty]');
    const total = root.querySelector('[data-cart-total]');
    const json = root.querySelector('[data-cart-json]');
    const submit = root.querySelector('[data-submit-order]');
    const notice = root.querySelector('[data-cart-notice]');
    const form = root.querySelector('[data-order-form]');
    const requestIdInput = root.querySelector('[data-request-id]');
    let sending = false;

    const showNotice = message => { notice.textContent = message; notice.hidden = !message; };
    const findDish = id => document.querySelector(`[data-add-dish][data-dish-id="${id}"]`);
    const dishData = id => {
        const button = findDish(id);
        return button ? { name: button.dataset.dishName, price: Number(button.dataset.dishPrice), unit: button.dataset.dishUnit } : null;
    };
    const newRequestId = () => window.crypto?.randomUUID?.() ||
        `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}-4000-8000-${Math.random().toString(16).slice(2)}`;
    const getStored = key => { try { return sessionStorage.getItem(key); } catch { return null; } };
    const setStored = (key, value) => { try { sessionStorage.setItem(key, value); } catch { /* storage is optional */ } };
    const removeStored = key => { try { sessionStorage.removeItem(key); } catch { /* storage is optional */ } };

    function ensureRequestId() {
        const requestId = requestIdInput.value || getStored(requestStorageKey) || newRequestId();
        requestIdInput.value = requestId;
        setStored(requestStorageKey, requestId);
        return requestId;
    }

    function updateVisibleDishPrice(dishId, price) {
        const button = findDish(dishId);
        if (!button) return;
        button.dataset.dishPrice = String(price);
        const priceText = button.closest('.guest-dish')?.querySelector('.guest-dish-price');
        if (priceText) priceText.firstChild.textContent = formatMoney(price);
    }

    function render() {
        let amount = 0;
        lines.innerHTML = '';
        cart.forEach(item => {
            const dish = dishData(item.dishId);
            if (!dish) return;
            if (!Number.isInteger(item.observedPriceVnd) || item.observedPriceVnd <= 0) item.observedPriceVnd = dish.price;
            amount += dish.price * item.quantity;
            const line = document.createElement('article');
            line.className = 'guest-cart-line';
            line.innerHTML = `<div><strong></strong><span></span></div><div class="guest-quantity"><button type="button" aria-label="Giảm số lượng">−</button><output></output><button type="button" aria-label="Tăng số lượng">+</button></div>`;
            line.querySelector('strong').textContent = dish.name;
            line.querySelector('span').textContent = `${formatMoney(dish.price)} / ${dish.unit} · ${formatMoney(dish.price * item.quantity)}`;
            line.querySelector('output').textContent = String(item.quantity);
            const [decrease, increase] = line.querySelectorAll('button');
            decrease.addEventListener('click', () => change(item.dishId, -1));
            increase.addEventListener('click', () => change(item.dishId, 1));
            increase.disabled = item.quantity >= maximum;
            lines.appendChild(line);
        });
        empty.hidden = cart.length > 0;
        total.textContent = formatMoney(amount);
        json.value = JSON.stringify(cart.map(item => ({ dishId: item.dishId, quantity: item.quantity, observedPriceVnd: item.observedPriceVnd })));
        setStored(cartStorageKey, json.value);
        submit.disabled = sending || cart.length === 0 || submit.dataset.disabledBySession === 'true';
    }

    function change(dishId, difference) {
        const item = cart.find(value => value.dishId === dishId);
        if (!item) return;
        const next = item.quantity + difference;
        if (next > maximum) { showNotice(`Mỗi món chỉ gọi tối đa ${maximum} phần.`); return; }
        if (next <= 0) {
            cart = cart.filter(value => value.dishId !== dishId);
            showNotice('');
        } else {
            item.quantity = next;
            showNotice(next === maximum ? `Bạn đã chọn tối đa ${maximum} phần cho món này.` : '');
        }
        render();
    }

    root.querySelectorAll('[data-add-dish]').forEach(button => button.addEventListener('click', () => {
        const dishId = Number(button.dataset.dishId);
        const existing = cart.find(item => item.dishId === dishId);
        if (existing) change(dishId, 1);
        else { cart.push({ dishId, quantity: 1, observedPriceVnd: Number(button.dataset.dishPrice) }); showNotice(''); render(); }
    }));

    form?.addEventListener('submit', async event => {
        if (cart.length === 0) { event.preventDefault(); showNotice('Giỏ món đang trống.'); return; }
        if (!window.fetch || !window.AbortController) return;

        event.preventDefault();
        if (sending) return;
        sending = true;
        showNotice('Đang gửi order...');
        render();
        ensureRequestId();

        const controller = new AbortController();
        const timer = window.setTimeout(() => controller.abort(), 15000);
        try {
            const data = new FormData(form);
            data.set('CartJson', json.value);
            data.set('RequestId', requestIdInput.value);
            const response = await fetch(form.dataset.submitEndpoint, {
                method: 'POST', body: data, credentials: 'same-origin', signal: controller.signal,
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            const payload = await response.json().catch(() => ({}));
            if (response.ok && payload.success && payload.order) {
                removeStored(cartStorageKey);
                removeStored(requestStorageKey);
                window.location.assign(`/GuestOrdering/Success?order=${encodeURIComponent(payload.order)}`);
                return;
            }
            if (response.status === 409 && Array.isArray(payload.priceChanges)) {
                payload.priceChanges.forEach(change => {
                    const item = cart.find(value => value.dishId === change.dishId);
                    if (item) item.observedPriceVnd = change.currentPriceVnd;
                    updateVisibleDishPrice(change.dishId, change.currentPriceVnd);
                });
            }
            showNotice(payload.message || 'Chưa thể gửi order. Vui lòng thử lại.');
        } catch {
            showNotice('Không thể xác nhận kết quả gửi order. Giỏ món vẫn được giữ nguyên; vui lòng thử lại.');
        } finally {
            window.clearTimeout(timer);
            sending = false;
            render();
        }
    });

    if (submit.disabled) submit.dataset.disabledBySession = 'true';
    ensureRequestId();
    render();
})();
