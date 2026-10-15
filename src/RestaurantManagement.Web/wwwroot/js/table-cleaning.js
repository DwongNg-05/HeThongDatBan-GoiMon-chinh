(() => {
    const config = document.querySelector('[data-cleaning-config]');
    if (!config) return;
    const message = config.querySelector('[data-cleaning-message]');
    document.querySelectorAll('.table-card[data-table-code]').forEach(card => {
        const button = document.createElement('button');
        button.type = 'button'; button.className = 'btn btn-outline-success btn-sm mt-2';
        button.dataset.completeCleaning = ''; button.textContent = 'Báo đã dọn xong';
        const update = () => { button.hidden = card.dataset.status !== 'Cleaning'; };
        const feedback = document.createElement('small');
        feedback.setAttribute('role', 'status'); feedback.className = 'd-block text-danger';
        update(); card.append(button, feedback);
        new MutationObserver(update).observe(card, { attributes: true, attributeFilter: ['data-status'] });
        button.addEventListener('click', async event => {
            event.stopPropagation(); button.disabled = true; message.textContent = ''; feedback.textContent = '';
            try {
                const response = await fetch(`${config.dataset.url}/${encodeURIComponent(card.dataset.tableCode)}/complete`, {
                    method: 'POST', body: new URLSearchParams({ __RequestVerificationToken: config.querySelector('[name=__RequestVerificationToken]').value })
                });
                if (response.redirected) throw new Error('Phiên đăng nhập đã hết. Vui lòng đăng nhập lại.');
                const data = await response.json();
                if (!response.ok) throw new Error(data.message || 'Không thể báo đã dọn xong.');
                message.textContent = data.message;
            } catch (error) { feedback.textContent = error.message || 'Không kết nối được máy chủ. Vui lòng thử lại.'; }
            finally {
                window.dispatchEvent(new Event('cleaning-completed'));
                button.disabled = false;
            }
        });
    });
})();
