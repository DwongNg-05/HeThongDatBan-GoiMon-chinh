(() => {
    'use strict';
    const form = document.getElementById('merge-tables-form');
    if (!form) return;
    const select = form.querySelector('#merge-other');
    const version = form.querySelector('#merge-other-version');
    const summary = form.querySelector('#merge-summary');
    const confirm = form.querySelector('[name=confirmed]');
    function update() {
        const option = select.selectedOptions[0];
        version.value = option.dataset.version;
        const keep = form.querySelector('[name=keepCurrent]:checked').value === 'true';
        summary.textContent = `Phiên chính sau khi gộp: #${keep ? form.elements.currentSessionId.value : option.value}. Hai bàn thanh toán một lần.`;
        confirm.checked = false;
    }
    select.addEventListener('change', update);
    form.querySelectorAll('[name=keepCurrent]').forEach(input => input.addEventListener('change', update));
    form.addEventListener('submit', () => { form.querySelector('button[type=submit]').disabled = true; });
    update();
})();
