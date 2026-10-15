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
    let nextDelay = 30000;
    // S3-09 Task 1 (reservation-no-show.js): đánh dấu khách không tới và hẹn giờ tải lại đúng lúc một lượt đạt mốc trễ.
    const noShow = globalThis.ReservationNoShow;
    // S3-09 Task 2: lượt đang gia hạn giữ bàn (không cảnh báo) đứng ngay sau nhóm đang trễ, kèm thời hạn mới.
    const groupOf = item => item.isLate ? 'late' : item.isExtended ? 'extended' : item.isUpcoming ? 'upcoming' : 'rest';
    const groups = {
        late: { title: 'Đang trễ quá 15 phút – khách chưa tới', className: 'table-danger' },
        extended: { title: 'Đang gia hạn giữ bàn – chờ khách tới', className: 'table-info' },
        upcoming: { title: 'Sắp đến trong 30 phút tới', className: 'table-warning' },
        rest: { title: 'Các lượt đặt còn lại', className: 'table-light' }
    };
    // Màu nhãn trạng thái (staff-theme.css): chờ xác nhận = vàng, đã xác nhận / đã tới = xanh, kết thúc = xám, không tới = đỏ.
    const tones = { 'Chờ xác nhận': 'pending', 'Đã xác nhận': 'confirmed', 'Khách đã tới': 'confirmed', 'Khách không tới': 'no-show' };
    function statusCell(item) {
        const cell = document.createElement('td');
        cell.className = 'reservation-status';
        cell.dataset.tone = tones[item.status] || 'closed';
        if (item.isExtended) {
            const status = document.createElement('span');
            status.textContent = item.status;
            const hold = document.createElement('span');
            hold.className = 'reservation-hold-badge';
            hold.textContent = item.holdLabel || 'Đang gia hạn giữ bàn';
            cell.append(status);
            cell.append(hold);
            return cell;
        }
        if (!item.isLate) { cell.textContent = item.status; return cell; }
        const status = document.createElement('span');
        status.textContent = item.status;
        const badge = document.createElement('span');
        badge.className = 'reservation-late-badge';
        badge.textContent = item.lateLabel || 'Trễ quá 15 phút';
        cell.append(status);
        cell.append(badge);
        if (item.extensionLabel) {
            // Đã gia hạn một lần và đã hết hạn: không còn nút gia hạn, chỉ còn đánh dấu khách không tới.
            const used = document.createElement('span');
            used.className = 'reservation-extension-used';
            used.textContent = item.extensionLabel;
            cell.append(used);
        }
        if (noShow?.extend && item.id && item.canExtend) {
            const extend = document.createElement('button');
            extend.type = 'button';
            extend.className = 'btn btn-sm btn-outline-primary reservation-extend';
            extend.textContent = 'Gia hạn 15 phút';
            extend.dataset.extendId = String(item.id);
            extend.setAttribute('aria-label', `Gia hạn giữ bàn 15 phút: lượt ${item.code}, ${item.customerName}`);
            extend.addEventListener('click', async () => {
                const result = await noShow.extend(item.id, `lượt ${item.code} – ${item.customerName}`);
                if (result) load(selectedDate, selectedStatus, true);
            });
            cell.append(extend);
        }
        if (noShow && item.id) {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'btn btn-sm btn-outline-danger reservation-no-show';
            button.textContent = 'Khách không tới';
            button.dataset.noShowId = String(item.id);
            button.setAttribute('aria-label', `Đánh dấu khách không tới: lượt ${item.code}, ${item.customerName}`);
            button.addEventListener('click', async () => {
                const result = await noShow.mark(item.id, `lượt ${item.code} – ${item.customerName}`);
                if (result) load(selectedDate, selectedStatus, true);
            });
            cell.append(button);
        }
        return cell;
    }
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
            // Nhóm: đang trễ (S3-09) → đang gia hạn giữ bàn → sắp đến trong 30 phút → còn lại;
            // chỉ có tiêu đề nhóm khi có lượt trễ, đang gia hạn hoặc sắp đến.
            const grouped = data.reservations.some(item => item.isLate || item.isExtended || item.isUpcoming);
            let lastGroup;
            for (const item of data.reservations) {
                const group = groupOf(item);
                if (grouped && lastGroup !== group) {
                    const heading = document.createElement('tr');
                    const cell = document.createElement('th');
                    cell.colSpan = 7;
                    cell.setAttribute('scope', 'rowgroup');
                    cell.textContent = groups[group].title;
                    heading.className = groups[group].className;
                    heading.append(cell);
                    rows.append(heading);
                    lastGroup = group;
                }
                const row = document.createElement('tr');
                if (group !== 'rest') row.className = groups[group].className;
                if (item.isLate) row.dataset.lateReservation = String(item.id);
                if (item.isExtended) row.dataset.extendedReservation = String(item.id);
                for (const field of ['code', 'customerName', 'phone', 'guestCount', 'timeSlot', 'tableName']) {
                    const cell = document.createElement('td');
                    cell.textContent = item[field];
                    row.append(cell);
                }
                row.append(statusCell(item));
                rows.append(row);
            }
            nextDelay = noShow ? noShow.nextDelay(data, Date.now()) : 30000;
            // Cảnh báo trễ không đọc được: báo ngay đầu danh sách, không im lặng.
            if (data.alertsError) {
                const heading = document.createElement('tr');
                const cell = document.createElement('th');
                cell.colSpan = 7;
                cell.textContent = data.alertsError;
                heading.className = 'table-danger';
                heading.dataset.alertsError = 'true';
                heading.append(cell);
                rows.prepend ? rows.prepend(heading) : rows.append(heading);
            }
            empty.hidden = data.reservations.length !== 0;
            empty.textContent = status ? 'Không có lượt đặt bàn phù hợp với ngày và trạng thái đã chọn.' : 'Chưa có lượt đặt bàn trong ngày này.';
            results.hidden = data.reservations.length === 0 && !data.alertsError;
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
                refreshTimer = setTimeout(() => load(selectedDate, selectedStatus, true), nextDelay);
                nextDelay = 30000;
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
