(() => {
    const root = document.querySelector('.guest-ordering');
    if (!root) return;

    const initial = document.querySelector('#initial-cart')?.textContent || '[]';
    let cart;
    try { cart = JSON.parse(initial); } catch { cart = []; }
    if (!Array.isArray(cart)) cart = [];
    cart = cart.filter(item => Number.isInteger(item.dishId) && Number.isInteger(item.quantity) && item.quantity > 0);
    const lines = root.querySelector('[data-cart-lines]');
    const empty = root.querySelector('[data-cart-empty]');
    const json = root.querySelector('[data-cart-json]');
    const submit = root.querySelector('[data-submit-order]');
    const findDish = id => document.querySelector(`[data-add-dish][data-dish-id="${id}"]`);
    const dishData = id => {
        const button = findDish(id);
        return button ? { name: button.dataset.dishName, price: Number(button.dataset.dishPrice), unit: button.dataset.dishUnit } : null;
    };

    function render() {
        lines.innerHTML = '';
        cart.forEach(item => {
            const dish = dishData(item.dishId);
            if (!dish) return;
            const line = document.createElement('article');
            line.className = 'guest-cart-line';
            line.innerHTML = `<div><strong></strong><span></span></div>`;
            line.querySelector('strong').textContent = dish.name;
            line.querySelector('span').textContent = `Số lượng: ${item.quantity}`;
            lines.appendChild(line);
        });
        empty.hidden = cart.length > 0;
        json.value = JSON.stringify(cart.map(item => ({ dishId: item.dishId, quantity: item.quantity })));
        submit.disabled = cart.length === 0 || submit.dataset.disabledBySession === 'true';
    }

    root.querySelectorAll('[data-add-dish]').forEach(button => button.addEventListener('click', () => {
        const dishId = Number(button.dataset.dishId);
        const existing = cart.find(item => item.dishId === dishId);
        if (existing) existing.quantity += 1;
        else cart.push({ dishId, quantity: 1 });
        render();
    }));

    root.querySelector('[data-order-form]')?.addEventListener('submit', event => {
        if (cart.length === 0) event.preventDefault();
    });

    if (submit.disabled) submit.dataset.disabledBySession = 'true';
    render();
})();
