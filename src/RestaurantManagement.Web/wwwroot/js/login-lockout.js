(() => {
    const counter = document.getElementById('lockout-countdown');
    if (!counter) return;
    const deadline = Date.now() + Number(counter.dataset.seconds) * 1000;
    const refresh = () => {
        const seconds = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
        counter.textContent = `${String(Math.floor(seconds / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
        if (seconds === 0) {
            document.getElementById('lockout-message').textContent = 'Đã hết thời gian chờ. Bạn có thể đăng nhập lại';
            counter.hidden = true;
            clearInterval(timer);
        }
    };
    const timer = setInterval(refresh, 1000);
    refresh();
})();
