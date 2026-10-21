// Món chờ mang ra (/Serving).
// - Đồng hồ "Đã chờ" chạy mỗi giây, tính từ lúc món được ghi nhận hoàn thành (máy chủ gửi waitMs đo bằng đồng hồ database,
//   trình duyệt cộng thêm thời gian trôi qua kể từ lúc nhận — không phụ thuộc giờ của máy điện thoại).
// - Món chờ vượt quá 5 phút được đánh dấu nổi bật NGAY trên dòng đang hiển thị, không sắp xếp lại danh sách.
// - S3-08 Task 1: mỗi 3 giây tải lại danh sách: thêm món vừa xong kèm thông báo "Món mới xong: Bàn … · tên món" (trong tối đa 5 giây
//   kể từ khi Bếp bấm "Đã xong"), gỡ món đã được mang ra; thứ tự luôn theo thời điểm hoàn thành, sớm trước muộn sau.
// - Bấm "Đã mang ra": gỡ dòng ngay, món đã phục vụ không còn trên danh sách và không bị cảnh báo. Nếu nhân viên khác đã mang ra trước,
//   máy chủ trả 409 kèm tên người đó: dòng cũng được gỡ và hiện thông báo để không mang trùng.
(() => {
    const logic = {
        /** Thời gian đã chờ (ms) tại thời điểm nowMs, biết lúc nhận dữ liệu món đã chờ item.waitMs. */
        waitMs(item, nowMs) {
            return Math.max(0, item.waitMs + Math.max(0, nowMs - item.receivedAt));
        },
        /** Cảnh báo khi chờ VƯỢT QUÁ ngưỡng (đúng 5:00 chưa cảnh báo) — khớp ServingQueue.IsOverdue. */
        isOverdue(waitMs, thresholdMs) {
            return waitMs > thresholdMs;
        },
        formatWait(ms) {
            const total = Math.floor(Math.max(0, ms) / 1000);
            const hours = Math.floor(total / 3600);
            const minutes = Math.floor((total % 3600) / 60);
            const seconds = String(total % 60).padStart(2, '0');
            return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${seconds}` : `${minutes}:${seconds}`;
        },
        /** Thứ tự theo thời điểm hoàn thành, trùng thì theo id — không phụ thuộc trạng thái cảnh báo. */
        order(items) {
            return [...items].sort((a, b) => (Date.parse(a.readyAtUtc) - Date.parse(b.readyAtUtc)) || (a.id - b.id));
        },
        /** Danh sách từ máy chủ → trạng thái trên trang (bỏ món đã phục vụ nếu có). */
        snapshot(items, receivedAt) {
            return logic.order(items.filter(item => !item.served))
                .map(item => ({ ...item, waitMs: Number(item.waitMs) || 0, receivedAt }));
        },
        /** S3-08 Task 1: món có trong lần tải mới nhưng chưa có trên trang (món vừa xong), theo thứ tự hoàn thành. */
        newlyReady(shownIds, items) {
            return logic.order(items.filter(item => !shownIds.has(item.id)));
        },
        /** Nội dung thông báo món mới xong: luôn có mã bàn và tên món. */
        notice(item) {
            const quantity = Number(item.quantity) > 1 ? ` × ${item.quantity}` : '';
            return `Món mới xong: Bàn ${item.tableCode} · ${item.itemName}${quantity}`;
        },
        /** Thời gian chờ, nhãn và cảnh báo của từng món tại nowMs, giữ nguyên thứ tự của state. */
        view(state, nowMs, thresholdMs) {
            return state.map(item => {
                const wait = logic.waitMs(item, nowMs);
                return { id: item.id, waitMs: wait, waitText: logic.formatWait(wait), overdue: logic.isOverdue(wait, thresholdMs) };
            });
        }
    };
    globalThis.ServingQueueLogic = logic;

    if (typeof document === 'undefined') return;
    const root = document.querySelector('[data-screen="serving-queue"]');
    if (!root) return;

    const clock = () => performance.now();
    const thresholdMs = Number(root.dataset.overdueMs) || 5 * 60 * 1000;
    const refreshMs = Number(root.dataset.refreshMs) || 3000;
    const noticeMs = 15000;
    const queueUrl = root.dataset.queueUrl;
    const serveUrl = root.dataset.serveUrl;
    const tbody = root.querySelector('[data-serving-rows]');
    const table = root.querySelector('[data-serving-table]');
    const empty = root.querySelector('[data-serving-empty]');
    const template = document.getElementById('serving-row-template');
    const status = document.getElementById('serving-status');
    const waitingCount = root.querySelector('[data-waiting-count]');
    const overdueCount = root.querySelector('[data-overdue-count]');
    const notices = document.getElementById('serving-notices');

    const loadedAt = clock();
    const rows = new Map();
    let state = [];
    for (const row of tbody.querySelectorAll('tr[data-serving-item]')) {
        const id = Number(row.dataset.servingItem);
        rows.set(id, row);
        state.push({ id, readyAtUtc: row.dataset.readyAtUtc, waitMs: Number(row.dataset.waitMs) || 0, receivedAt: loadedAt });
    }
    state = logic.order(state);

    /** kind: 'success' | 'warning' (đã có người khác mang ra) | 'danger'. */
    function showStatus(message, kind) {
        if (!status) return;
        status.textContent = message;
        for (const k of ['success', 'warning', 'danger']) status.classList.toggle('alert-' + k, k === kind);
        status.hidden = !message;
    }

    /** S3-08 Task 1: thông báo món mới xong (mã bàn, tên món), tự ẩn sau 15 giây hoặc khi món đã được mang ra. */
    function notify(item) {
        if (!notices) return;
        const notice = document.createElement('div');
        notice.className = 'alert alert-info py-2 mb-2 serving-notice';
        notice.setAttribute('role', 'status');
        notice.dataset.noticeFor = String(item.id);
        notice.textContent = logic.notice(item);
        notices.insertBefore(notice, notices.firstElementChild);
        while (notices.children.length > 5) notices.children[notices.children.length - 1].remove();
        setTimeout(() => notice.remove(), noticeMs);
    }

    function dropNotices(keep) {
        if (!notices) return;
        for (const notice of [...notices.children])
            if (!keep.has(Number(notice.dataset.noticeFor))) notice.remove();
    }

    function cell(row, name) {
        return row.querySelector(`[data-cell="${name}"]`);
    }

    function createRow(item) {
        const row = template.content.firstElementChild.cloneNode(true);
        row.dataset.servingItem = String(item.id);
        row.dataset.readyAtUtc = item.readyAtUtc;
        cell(row, 'table').textContent = item.tableCode ?? '';
        cell(row, 'name').textContent = item.itemName ?? '';
        cell(row, 'quantity').textContent = `${item.quantity ?? ''} ${item.unit ?? ''}`.trim();
        cell(row, 'notes').textContent = item.notes ?? '';
        const time = cell(row, 'ready-at').querySelector('time');
        time.textContent = item.readyAt ?? '';
        time.setAttribute('datetime', item.readyAtUtc);
        row.querySelector('[data-serve-form]').setAttribute('action', serveUrl + item.id);
        return row;
    }

    /** Cập nhật đồng hồ và cảnh báo trên các dòng hiện có — không thêm, bớt hay đổi chỗ dòng nào. */
    function render() {
        const view = logic.view(state, clock(), thresholdMs);
        let overdue = 0;
        for (const entry of view) {
            const row = rows.get(entry.id);
            if (!row) continue;
            const wasOverdue = row.dataset.overdue === 'true';
            row.querySelector('[data-wait]').textContent = entry.waitText;
            row.dataset.waitMs = String(Math.round(entry.waitMs));
            row.dataset.overdue = entry.overdue ? 'true' : 'false';
            row.classList.toggle('table-danger', entry.overdue);
            row.classList.toggle('serving-overdue', entry.overdue);
            row.classList.toggle('serving-overdue-new', entry.overdue && !wasOverdue);
            row.querySelector('[data-overdue-badge]').hidden = !entry.overdue;
            if (entry.overdue) overdue++;
        }
        if (waitingCount) waitingCount.textContent = String(view.length);
        if (overdueCount) overdueCount.textContent = String(overdue);
        empty.hidden = view.length > 0;
        table.hidden = view.length === 0;
    }

    /** Đồng bộ các dòng với state: gỡ món đã mang ra, thêm món mới, xếp đúng thứ tự hoàn thành, đánh số lại. */
    function syncRows() {
        const keep = new Set(state.map(item => item.id));
        dropNotices(keep);
        for (const [id, row] of rows) {
            if (!keep.has(id)) {
                row.remove();
                rows.delete(id);
            }
        }
        state.forEach((item, index) => {
            let row = rows.get(item.id);
            if (!row) {
                row = createRow(item);
                rows.set(item.id, row);
            }
            if (tbody.children[index] !== row) tbody.insertBefore(row, tbody.children[index] ?? null);
            cell(row, 'position').textContent = String(index + 1);
        });
        render();
    }

    // Món đã mang ra không bao giờ quay lại trạng thái chờ: lọc luôn khỏi các lần tải lại đang chạy dở.
    const served = new Set();
    function removeItem(id) {
        served.add(id);
        state = state.filter(item => item.id !== id);
        syncRows();
    }

    let refreshing = false;
    async function refresh() {
        if (refreshing) return;
        refreshing = true;
        try {
            const response = await fetch(queueUrl, {
                headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json' },
                cache: 'no-store', credentials: 'same-origin'
            });
            if (response.status === 401) { location.reload(); return; }
            if (!response.ok) return;
            const data = await response.json();
            const items = (data.items ?? []).filter(item => !served.has(item.id));
            const fresh = logic.newlyReady(new Set(rows.keys()), items);
            state = logic.snapshot(items, clock());
            syncRows();
            for (const item of fresh) {
                const row = rows.get(item.id);
                row?.classList.toggle('serving-new', true);
                setTimeout(() => row?.classList.toggle('serving-new', false), noticeMs);
                notify(item);
            }
        } catch {
            // Mất mạng tạm thời: giữ danh sách hiện có, đồng hồ vẫn chạy, lần sau thử lại.
        } finally {
            refreshing = false;
        }
    }

    tbody.addEventListener('submit', async event => {
        const form = event.target.closest('[data-serve-form]');
        if (!form) return;
        event.preventDefault();
        const row = form.closest('tr[data-serving-item]');
        const dish = row.querySelector('[data-cell="name"]')?.textContent?.trim() || 'món này';
        const table = row.querySelector('[data-cell="table"]')?.textContent?.trim() || '';
        if (!window.confirm(`Xác nhận đã mang ${dish}${table ? ` ra bàn ${table}` : ''}?`)) return;
        const id = Number(row.dataset.servingItem);
        const button = form.querySelector('button');
        button.disabled = true;
        try {
            const response = await fetch(form.getAttribute('action'), {
                method: 'POST', body: new FormData(form), credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json' }
            });
            if (response.status === 401) { location.reload(); return; }
            const data = await response.json().catch(() => ({}));
            // Đã mang ra, hoặc món không còn chờ (nhân viên khác đã mang ra / bàn đã thanh toán / món đã huỷ) → gỡ khỏi danh sách.
            if (response.ok || response.status === 404 || response.status === 409) removeItem(id);
            else button.disabled = false;
            showStatus(data.message ?? (response.ok ? 'Đã mang món ra.' : 'Không xác nhận được, vui lòng thử lại.'),
                response.ok ? 'success' : data.alreadyServed ? 'warning' : 'danger');
        } catch {
            button.disabled = false;
            showStatus('Mất kết nối, vui lòng thử lại.', 'danger');
        }
    });

    document.addEventListener('visibilitychange', () => {
        if (!document.hidden) refresh();
    });
    syncRows();
    setInterval(render, 1000);
    setInterval(refresh, refreshMs);
})();
