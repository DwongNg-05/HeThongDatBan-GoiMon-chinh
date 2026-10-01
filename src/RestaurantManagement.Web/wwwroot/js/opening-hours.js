// Giờ hoạt động: xem trước khung giờ đặt bàn ngay khi nhập, tắt ô giờ khi nghỉ cả ngày,
// chọn nhanh thời lượng và chép giờ của ngày đầu tiên cho cả tuần.
(() => {
    const pad = n => String(n).padStart(2, '0');
    const minutes = value => {
        if (!/^\d{2}:\d{2}$/.test(value)) return NaN;
        const [hour, minute] = value.split(':').map(Number);
        return hour < 24 && minute < 60 ? hour * 60 + minute : NaN;
    };
    const rows = [];

    document.querySelectorAll('.closed-day').forEach(checkbox => {
        const row = checkbox.closest('tr');
        const inputs = row.querySelectorAll('input[type="time"]');
        const preview = row.querySelector('.booking-slots');
        const note = (cls, text) => { const s = document.createElement('span'); s.className = cls; s.textContent = text; return s; };
        const update = () => {
            row.classList.toggle('is-closed', checkbox.checked);
            inputs.forEach(input => {
                input.disabled = checkbox.checked;
                input.required = !checkbox.checked;
                input.setCustomValidity('');
            });
            const closeError = row.querySelector('[data-valmsg-for$=".ClosesAt"]');
            if (closeError) closeError.textContent = '';
            if (!preview) return;
            const wasOpen = preview.querySelector('details')?.open ?? false;
            preview.replaceChildren();
            if (checkbox.checked) { preview.append(note('oh-off', 'Ngày nghỉ — không nhận đặt bàn.')); return; }
            const open = minutes(inputs[0].value), close = minutes(inputs[1].value);
            if (Number.isFinite(open) && Number.isFinite(close) && close <= open) {
                const message = 'Giờ đóng cửa phải lớn hơn giờ mở cửa.';
                inputs[1].setCustomValidity(message);
                if (closeError) closeError.textContent = message;
            }
            if (!Number.isFinite(open) || !Number.isFinite(close) || close <= open) {
                preview.append(note('oh-hint', 'Nhập giờ mở và đóng hợp lệ để xem khung giờ.'));
                return;
            }
            const slots = [];
            for (let m = open; m < close; m += 30) slots.push(`${pad(Math.floor(m / 60))}:${pad(m % 60)}`);
            const details = document.createElement('details');
            details.className = 'oh-slots';
            details.open = wasOpen;
            const summary = document.createElement('summary');
            const first = document.createElement('strong'); first.textContent = slots[0];
            const last = document.createElement('strong'); last.textContent = slots[slots.length - 1];
            summary.append('Nhận khách ', first, ' – ', last, ` · ${slots.length} lượt`);
            const list = document.createElement('div');
            list.className = 'oh-slot-list';
            slots.forEach(text => list.append(note('badge bg-light text-dark border', text)));
            details.append(summary, list);
            preview.append(details);
        };
        inputs.forEach(input => input.addEventListener('input', update));
        checkbox.addEventListener('change', update);
        rows.push({ checkbox, inputs, update });
        update();
    });

    document.getElementById('copy-first-day')?.addEventListener('click', () => {
        const [first, ...others] = rows;
        if (!first) return;
        others.forEach(r => {
            r.checkbox.checked = first.checkbox.checked;
            r.inputs[0].value = first.inputs[0].value;
            r.inputs[1].value = first.inputs[1].value;
            r.update();
        });
        document.getElementById('weekly-settings')?.dispatchEvent(new Event('input', { bubbles: true }));
    });

    const duration = document.getElementById('DefaultBookingMinutes');
    const chips = document.querySelectorAll('.oh-chip[data-minutes]');
    const validateDuration = () => {
        const valid = duration.value !== '' && Number.isInteger(Number(duration.value)) && Number(duration.value) >= 30 && Number(duration.value) <= 360;
        const message = valid ? '' : 'Thời lượng giữ bàn phải là số nguyên từ 30 đến 360 phút.';
        duration.setCustomValidity(message);
        const error = document.querySelector('[data-valmsg-for="DefaultBookingMinutes"]');
        if (error) error.textContent = message;
        chips.forEach(c => c.classList.toggle('is-on', c.dataset.minutes === duration.value));
    };
    duration?.addEventListener('input', validateDuration);
    chips.forEach(chip => chip.addEventListener('click', () => {
        duration.value = chip.dataset.minutes;
        duration.dispatchEvent(new Event('input', { bubbles: true }));
    }));
})();
