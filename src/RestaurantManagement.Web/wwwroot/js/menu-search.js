// S2-01 Task 2: khi khách bấm nút × để xoá từ khoá thì tải lại toàn bộ thực đơn.
// Xoá tay rồi bấm "Tìm" với ô trống cũng hiển thị toàn bộ thực đơn (máy chủ coi từ khoá rỗng là không lọc).
// Việc lọc theo tên (bỏ dấu, không phân biệt hoa/thường) làm ở máy chủ: MenuSearch.Filter.
(() => {
    const form = document.querySelector('.public-menu-search');
    const input = form?.querySelector('input[name="q"]');
    if (!form || !input || (form.dataset.keyword || '') === '') return;

    const showFullMenu = () => {
        if (input.value.trim() === '') {
            window.location.href = form.getAttribute('action') || window.location.pathname;
        }
    };
    // Nút × của ô type="search" (Chrome, Edge, Safari) phát sự kiện "search";
    // thay đổi không do gõ phím (không có inputType) cũng được xử lý như bấm ×.
    input.addEventListener('search', showFullMenu);
    input.addEventListener('input', (event) => { if (!event.inputType) showFullMenu(); });
})();
