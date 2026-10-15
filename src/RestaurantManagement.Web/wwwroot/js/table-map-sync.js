(() => {
    'use strict';

    const root = document.querySelector('.snapshot-map');

    if (!root || root.dataset.loadFailed === 'true') {
        return;
    }

    const notice = root.querySelector('.map-sync-notice');
    const clock = root.querySelector('.map-sync-time time');

    const cards = new Map(
        [...root.querySelectorAll('[data-table-code]')]
            .map(card => [card.dataset.tableCode, card])
    );

    const statuses = {
        Available: ['Trống', 'available'],
        Reserved: ['Đã đặt trước', 'reserved'],
        Serving: ['Đang phục vụ', 'serving'],
        AwaitingPayment: ['Chờ thanh toán', 'serving'],
        Cleaning: ['Đang dọn', 'cleaning']
    };

    let cursor = root.dataset.cursor;
    let pending = false;
    let stopped = false;

    function warn(
        message = 'Mất kết nối. Dữ liệu có thể đã cũ; hệ thống sẽ tự thử lại.'
    ) {
        if (!notice) return;

        notice.textContent = message;
        notice.hidden = false;
    }

    function apply(table) {
        const card = cards.get(table.code);
        const display = statuses[table.status];

        if (!card || !display) return;

        // A merge changes the group while both cards remain Serving.
        const groupKey = JSON.stringify(table.group ?? null);
        const groupChanged = card.dataset.groupKey !== groupKey;
        card.dataset.groupKey = groupKey;
        card.dataset.mainSessionId = table.group ? String(table.group.mainSessionId) : '';
        const badge = card.querySelector('.table-group-label');
        if (badge) {
            badge.hidden = !table.group;
            badge.textContent = table.group ? `Nhóm ${table.group.label}` : '';
            badge.title = table.group
                ? `Các bàn: ${table.group.memberTableCodes.join(', ')} · Bàn chính: ${table.group.mainTableCode}` : '';
        }
        if (card.dataset.status === table.status) {
            if (groupChanged) root.dispatchEvent(new CustomEvent('tablemap:changed', {
                detail: { code: table.code, previousStatus: table.status, status: table.status }
            }));
            return;
        }

        const previousStatus = card.dataset.status;
        const wasAvailable = previousStatus === 'Available';
        const isAvailable = table.status === 'Available';
        const [label, css] = display;

        for (const value of Object.values(statuses)) {
            card.classList.remove('status-' + value[1]);
        }

        card.classList.add('status-' + css);
        card.dataset.status = table.status;

        const statusLabel = card.querySelector('.table-status-label');

        if (statusLabel) {
            statusLabel.textContent = label;
        }

        card.setAttribute(
            'aria-label',
            `Bàn ${table.code}, ${table.capacity} chỗ, ${label}`
        );

        card.classList.add('table-just-changed');

        window.setTimeout(() => {
            card.classList.remove('table-just-changed');
        }, 3000);

        const area = card.closest('.area-section');
        const counter = area?.querySelector('[data-area-available]');

        if (counter) {
            const currentCount = Number(counter.textContent);

            if (Number.isFinite(currentCount)) {
                counter.textContent = String(
                    currentCount
                    + (isAvailable ? 1 : 0)
                    - (wasAvailable ? 1 : 0)
                );
            }
        }

        root.dispatchEvent(
            new CustomEvent('tablemap:changed', {
                detail: {
                    code: table.code,
                    previousStatus,
                    status: table.status
                }
            })
        );
    }

    async function sync() {
        if (stopped || document.hidden || pending) {
            return;
        }

        pending = true;

        const controller = new AbortController();

        const timeout = window.setTimeout(() => {
            controller.abort();
        }, 1800);

        try {
            const response = await fetch(
                `${root.dataset.changesUrl}?after=${encodeURIComponent(cursor)}`,
                {
                    signal: controller.signal,
                    cache: 'no-store',
                    credentials: 'same-origin'
                }
            );

            if (
                response.status === 401
                || response.status === 403
                || response.redirected
            ) {
                stopped = true;

                warn(
                    'Phiên đăng nhập hoặc quyền truy cập đã thay đổi. '
                    + 'Vui lòng đăng nhập lại.'
                );

                return;
            }

            if (response.status === 409) {
                stopped = true;

                warn(
                    'Dữ liệu đã được đặt lại. Vui lòng tải lại sơ đồ.'
                );

                return;
            }

            if (!response.ok) {
                throw new Error('Không tải được trạng thái bàn.');
            }

            const changes = await response.json();

            if (
                !changes
                || !/^\d+$/.test(String(changes.cursor))
                || !Array.isArray(changes.tables)
                || changes.tables.some(
                    table => !table
                        || !Object.prototype.hasOwnProperty.call(
                            statuses,
                            table.status
                        )
                )
            ) {
                throw new Error('Dữ liệu đồng bộ không hợp lệ.');
            }

            changes.tables.forEach(apply);

            cursor = String(changes.cursor);
            root.dataset.cursor = cursor;

            if (clock && changes.syncedAtUtc) {
                const syncedAt = new Date(changes.syncedAtUtc);

                if (!Number.isNaN(syncedAt.getTime())) {
                    clock.dateTime = changes.syncedAtUtc;

                    clock.textContent = syncedAt.toLocaleString(
                        'vi-VN',
                        {
                            timeZone: 'Asia/Ho_Chi_Minh'
                        }
                    );
                }
            }

            if (notice) {
                notice.hidden = true;
            }
        } catch {
            if (!document.hidden && !stopped) {
                warn();
            }
        } finally {
            window.clearTimeout(timeout);
            pending = false;
        }
    }

    document.addEventListener('visibilitychange', () => {
        if (!document.hidden) {
            void sync();
        }
    });

    window.addEventListener('online', () => {
        void sync();
    });

    window.addEventListener('offline', () => {
        warn();
    });

    const interval = window.setInterval(() => {
        void sync();
    }, 2000);

    window.addEventListener('pagehide', () => {
        stopped = true;
        window.clearInterval(interval);
    });

    root.dataset.syncReady = 'true';

    void sync();
})();