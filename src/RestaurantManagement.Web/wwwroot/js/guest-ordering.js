(() => {
    const root = document.querySelector('.guest-ordering');
    if (!root) return;

    const maximum = Number(root.dataset.maximumQuantity || 20);
    const initial = document.querySelector('#initial-cart')?.textContent || '[]';
    let cart;
    try { cart = JSON.parse(initial); } catch { cart = []; }
    if (!Array.isArray(cart)) cart = [];
    cart = cart.filter(item => Number.isInteger(item.dishId) && Number.isInteger(item.quantity) && item.quantity > 0 && item.quantity <= maximum);
    const formatMoney = amount => `${new Intl.NumberFormat('vi-VN').format(amount)} ₫`;
    const lines = root.querySelector('[data-cart-lines]');
    const empty = root.querySelector('[data-cart-empty]');
    const total = root.querySelector('[data-cart-total]');
    const json = root.querySelector('[data-cart-json]');
    const submit = root.querySelector('[data-submit-order]');
    const notice = root.querySelector('[data-cart-notice]');

    const showNotice = message => { notice.textContent = message; notice.hidden = !message; };
    const findDish = id => document.querySelector(`[data-add-dish][data-dish-id="${id}"]`);
    const dishData = id => {
        const button = findDish(id);
        return button ? { name: button.dataset.dishName, price: Number(button.dataset.dishPrice), unit: button.dataset.dishUnit } : null;
    };

    function render() {
        let amount = 0;
        lines.innerHTML = '';
        cart.forEach(item => {
            const dish = dishData(item.dishId);
            if (!dish) return;
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
        json.value = JSON.stringify(cart.map(item => ({ dishId: item.dishId, quantity: item.quantity })));
        submit.disabled = cart.length === 0 || submit.dataset.disabledBySession === 'true';
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
        else { cart.push({ dishId, quantity: 1 }); showNotice(''); render(); }
    }));

    root.querySelector('[data-order-form]')?.addEventListener('submit', event => {
        if (cart.length === 0) { event.preventDefault(); showNotice('Giỏ món đang trống.'); }
    });

    if (submit.disabled) submit.dataset.disabledBySession = 'true';
    render();
})();
