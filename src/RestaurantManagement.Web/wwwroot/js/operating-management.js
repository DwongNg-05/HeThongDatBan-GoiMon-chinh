(() => {
    const weekly = document.getElementById('weekly-settings');
    let dirty = false;
    weekly?.addEventListener('input', () => {
        dirty = true;
        document.getElementById('weekly-save-state').textContent = 'Có thay đổi chưa lưu. Nhấn Lưu cấu hình hoạt động trước khi kiểm tra lịch hoặc thử đặt bàn.';
    });
    weekly?.addEventListener('submit', () => { dirty = false; });
    window.addEventListener('beforeunload', event => {
        if (!dirty) return;
        event.preventDefault(); event.returnValue = '';
    });

    const calendar = document.getElementById('calendar-panel');
    const loaders = new Map();
    document.querySelectorAll('[data-management-panel]').forEach(panel => {
        let busy = false;
        async function load(url, options = {}) {
            if (busy) return;
            busy = true;
            panel.setAttribute('aria-busy', 'true');
            try {
                const response = await fetch(url, { ...options, cache: 'no-store' });
                if (!response.ok) throw new Error('Request failed');
                const page = new DOMParser().parseFromString(await response.text(), 'text/html');
                const main = page.querySelector('main');
                if (!main) throw new Error('Missing content');
                panel.replaceChildren(...Array.from(main.childNodes));
                panel.querySelector('h1')?.setAttribute('tabindex', '-1');
                panel.querySelector('h1')?.focus();
                if (panel.id === 'holiday-panel' && options.method === 'POST') {
                    calendar.querySelector('.calendar-stale')?.remove();
                    const note = document.createElement('p');
                    note.className = 'calendar-stale text-warning';
                    note.textContent = 'Danh sách ngày nghỉ có thể đã thay đổi. Nhấn Xem khung giờ để cập nhật lịch bên dưới.';
                    calendar.prepend(note);
                }
            } catch {
                panel.querySelector('.panel-error')?.remove();
                const error = document.createElement('p');
                error.className = 'panel-error text-danger';
                error.setAttribute('role', 'alert');
                error.textContent = 'Không tải được kết quả. Dữ liệu đang nhập được giữ lại; hãy kiểm tra kết nối và tải lại danh sách trước khi gửi lại.';
                panel.prepend(error);
            } finally {
                busy = false; panel.removeAttribute('aria-busy');
            }
        }
        loaders.set(panel.id, load);
        panel.addEventListener('click', event => {
            const link = event.target.closest('a');
            if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
            const url = new URL(link.href, location.href);
            if (url.origin !== location.origin) return;
            if (url.pathname.startsWith('/SpecialHolidays')) {
                event.preventDefault(); loaders.get('holiday-panel')(url);
                document.getElementById('holiday-panel').scrollIntoView({ behavior: 'smooth' });
            } else if (url.pathname === '/OpeningHours/Calendar') {
                event.preventDefault();
                loaders.get('calendar-panel')(url);
                calendar.scrollIntoView({ behavior: 'smooth' });
            } else if (url.pathname === '/OpeningHours' || url.pathname === '/OpeningHours/Index') {
                event.preventDefault(); weekly.scrollIntoView({ behavior: 'smooth' });
            }
        });
        panel.addEventListener('submit', event => {
            const form = event.target;
            if (!(form instanceof HTMLFormElement)) return;
            event.preventDefault();
            const url = new URL(form.action, location.href);
            const data = new FormData(form);
            if (form.method.toLowerCase() === 'get') {
                url.search = new URLSearchParams(data).toString(); load(url);
            } else load(url, { method: 'POST', body: data });
        });
    });
})();
