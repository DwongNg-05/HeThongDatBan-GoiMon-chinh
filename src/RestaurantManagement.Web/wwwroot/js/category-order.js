(() => {
    const list = document.getElementById('category-order');
    if (!list) return;
    const save = document.getElementById('save-order');
    const status = document.getElementById('order-status');
    function refresh() {
        const rows = [...list.children];
        rows.forEach((row, index) => {
            row.querySelector('[data-move="up"]').disabled = index === 0;
            row.querySelector('[data-move="down"]').disabled = index === rows.length - 1;
        });
    }
    list.addEventListener('click', event => {
        const button = event.target.closest('button[data-move]');
        if (!button || button.disabled) return;
        const row = button.closest('li');
        const up = button.dataset.move === 'up';
        const neighbor = up ? row.previousElementSibling : row.nextElementSibling;
        if (!neighbor) return;
        if (up) list.insertBefore(row, neighbor);
        else list.insertBefore(neighbor, row);
        refresh();
        save.disabled = false;
        status.textContent = `${row.dataset.categoryName}: vị trí ${[...list.children].indexOf(row) + 1}. Chưa lưu thay đổi.`;
        (button.disabled ? row.querySelector(up ? '[data-move="down"]' : '[data-move="up"]') : button).focus();
    });
    refresh();
})();
