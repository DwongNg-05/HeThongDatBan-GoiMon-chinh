document.querySelectorAll('.closed-day').forEach(checkbox => {
    const row = checkbox.closest('tr');
    const inputs = row.querySelectorAll('input[type="time"]');
    const preview = row.querySelector('.booking-slots');
    const minutes = value => {
        if (!/^\d{2}:\d{2}$/.test(value)) return NaN;
        const [hour, minute] = value.split(':').map(Number);
        return hour < 24 && minute < 60 ? hour * 60 + minute : NaN;
    };
    const update = () => {
        inputs.forEach(input => {
            input.disabled = checkbox.checked;
            input.required = !checkbox.checked;
            input.setCustomValidity('');
        });
        const closeError = row.querySelector('[data-valmsg-for$=".ClosesAt"]');
        if (closeError) closeError.textContent = '';
        if (!preview) return;
        preview.replaceChildren();
        if (checkbox.checked) {
            preview.textContent = 'Ngày nghỉ — không nhận đặt bàn.';
            return;
        }
        const open = minutes(inputs[0].value), close = minutes(inputs[1].value);
        if (Number.isFinite(open) && Number.isFinite(close) && close <= open) {
            const message = 'Giờ đóng cửa phải lớn hơn giờ mở cửa.';
            inputs[1].setCustomValidity(message);
            if (closeError) closeError.textContent = message;
        }
        if (!Number.isFinite(open) || !Number.isFinite(close) || close <= open) {
            preview.textContent = 'Nhập giờ mở và đóng hợp lệ để xem khung giờ.';
            return;
        }
        for (let minute = open; minute < close; minute += 30) {
            const slot = document.createElement('span');
            slot.className = 'badge bg-light text-dark border me-1 mb-1';
            slot.textContent = `${String(Math.floor(minute / 60)).padStart(2, '0')}:${String(minute % 60).padStart(2, '0')}`;
            preview.append(slot);
        }
    };
    inputs.forEach(input => input.addEventListener('input', update));
    checkbox.addEventListener('change', update);
    update();
});

const duration = document.getElementById('DefaultBookingMinutes');
duration?.addEventListener('input', () => {
    const valid = duration.value !== '' && Number.isInteger(Number(duration.value)) && Number(duration.value) >= 30 && Number(duration.value) <= 360;
    const message = valid ? '' : 'Thời lượng giữ bàn phải là số nguyên từ 30 đến 360 phút.';
    duration.setCustomValidity(message);
    const error = document.querySelector('[data-valmsg-for="DefaultBookingMinutes"]');
    if (error) error.textContent = message;
});
