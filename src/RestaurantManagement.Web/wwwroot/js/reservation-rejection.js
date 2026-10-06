// Keep the submit button disabled until a fixed rejection reason is selected.
(() => {
    const select = document.getElementById('rejection-reason');
    const button = document.getElementById('submit-rejection');
    if (!select || !button) return;
    const update = () => { button.disabled = !['NoTable', 'OutsideHours', 'Unreachable'].includes(select.value); };
    select.addEventListener('change', update);
    window.addEventListener('pageshow', update);
    update();
})();
