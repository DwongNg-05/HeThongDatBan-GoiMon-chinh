(() => {
    const root = document.getElementById('daily-reservations');
    if (!root) return;
    const input = document.getElementById('reservation-date');
    const statusInput = document.getElementById('reservation-status');
    const rows = document.getElementById('reservation-rows');
    const loading = document.getElementById('reservation-loading');
    const error = document.getElementById('reservation-error');
    const empty = document.getElementById('reservation-empty');
    const results = document.getElementById('reservation-results');
    let pending;
    let selectedDate = root.dataset.date;
    let selectedStatus = '';
    let followToday = root.dataset.followToday === 'true';
    let refreshTimer;
    let busy = false;
    async function load(date, status = selectedStatus, background = false) {
        clearTimeout(refreshTimer);
        busy = true;
        pending?.abort();
        const request = new AbortController();
        pending = request;
        selectedDate = date;
        selectedStatus = status;
        const [year, month, day] = date.split('-');
        document.getElementById('viewing-date').textContent = `${day}/${month}/${year}`;
        if (!background) {
            rows.replaceChildren();
            loading.hidden = false;
            empty.hidden = results.hidden = true;
        }
        error.hidden = true;
        root.setAttribute('aria-busy', 'true');
        try {
            const statusQuery = status ? `&status=${encodeURIComponent(status)}` : '';
            const dateQuery = followToday ? '' : `date=${encodeURIComponent(date)}`;
            const response = await fetch(`${root.dataset.url}?${dateQuery}${statusQuery}`, {
                signal: request.signal, headers: { Accept: 'application/json' }, cache: 'no-store'
            });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('Load failed');
            const data = await response.json();
            if (pending !== request) return;
            if (data.date) {
                selectedDate = data.date;
                input.value = data.date;
                const [year, month, day] = data.date.split('-');
                document.getElementById('viewing-date').textContent = `${day}/${month}/${year}`;
            }
            rows.replaceChildren();
            const hasUpcoming = data.reservations.some(item => item.isUpcoming);
            let lastGroup;
            for (const item of data.reservations) {
                if (hasUpcoming && lastGroup !== !!item.isUpcoming) {
                    const heading = document.createElement('tr');
                    const cell = document.createElement('th');
                    cell.colSpan = 7;
                    cell.setAttribute('scope', 'rowgroup');
                    cell.textContent = item.isUpcoming ? 'Sắp đến trong 30 phút tới' : 'Các lượt đặt còn lại';
                    heading.className = item.isUpcoming ? 'table-warning' : 'table-light';
                    heading.append(cell);
                    rows.append(heading);
                    lastGroup = !!item.isUpcoming;
                }
                const row = document.createElement('tr');
                if (item.isUpcoming) row.className = 'table-warning';
                for (const field of ['code', 'customerName', 'phone', 'guestCount', 'timeSlot', 'tableName', 'status']) {
                    const cell = document.createElement('td');
                    cell.textContent = item[field];
                    row.append(cell);
                }
                rows.append(row);
            }
            empty.hidden = data.reservations.length !== 0;
            empty.textContent = status ? 'Không có lượt đặt bàn phù hợp với ngày và trạng thái đã chọn.' : 'Chưa có lượt đặt bàn trong ngày này.';
            results.hidden = data.reservations.length === 0;
            if (data.evaluatedAtUtc) {
                const evaluatedAt = new Date(data.evaluatedAtUtc);
                root.dataset.date = new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit' }).format(evaluatedAt);
                document.getElementById('reservation-updated').textContent = 'Cập nhật lúc ' +
                    new Intl.DateTimeFormat('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(evaluatedAt);
            }
        } catch (failure) {
            if (failure.name !== 'AbortError' && pending === request) error.hidden = false;
        } finally {
            if (pending === request) {
                loading.hidden = true;
                busy = false;
                root.setAttribute('aria-busy', 'false');
                refreshTimer = setTimeout(() => load(selectedDate, selectedStatus, true), 30000);
            }
        }
    }
    document.getElementById('reservation-date-form').addEventListener('submit', event => {
        event.preventDefault();
        followToday = input.value === root.dataset.date;
        load(input.value, statusInput.value);
    });
    statusInput.addEventListener('change', () => load(selectedDate, statusInput.value));
    document.getElementById('reservation-clear-filter').addEventListener('click', () => {
        statusInput.value = '';
        load(selectedDate, '');
    });
    document.getElementById('reservation-retry').addEventListener('click', () => load(selectedDate));
    document.getElementById('reservation-today').addEventListener('click', () => {
        followToday = true;
        load(selectedDate);
    });
    document.addEventListener('visibilitychange', () => {
        if (!document.hidden && !busy) load(selectedDate, selectedStatus, true);
    });
    load(selectedDate);
})();
