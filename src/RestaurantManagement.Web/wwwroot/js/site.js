// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// Thu gọn / mở thanh điều hướng "BẾP NHÀ" trên máy tính. Lựa chọn lưu trong localStorage (rm.sidebar-collapsed)
// và được áp dụng ngay đầu <body> (Views/Shared/_Layout.cshtml) để trang không bị nháy khi tải.
(function () {
    'use strict';
    var collapse = document.querySelector('[data-sidebar-collapse]');
    var expand = document.querySelector('[data-sidebar-expand]');
    if (!collapse || !expand) return;
    var KEY = 'rm.sidebar-collapsed';

    function sync() {
        var collapsed = document.body.classList.contains('sidebar-collapsed');
        collapse.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
        expand.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
    }

    function setCollapsed(collapsed) {
        document.body.classList.toggle('sidebar-collapsed', collapsed);
        try { localStorage.setItem(KEY, collapsed ? '1' : '0'); } catch (e) { /* trình duyệt chặn lưu */ }
        sync();
        (collapsed ? expand : collapse).focus();
    }

    collapse.addEventListener('click', function () { setCollapsed(true); });
    expand.addEventListener('click', function () { setCollapsed(false); });
    sync();
})();

// Các thao tác làm thay đổi trạng thái thật (phục vụ món, đóng ca, hủy...) phải
// được hỏi lại.  Gắn data-confirm-action vào form, nội dung trong data-confirm.
// Đặt tại đây để mọi màn hình dùng cùng một cơ chế và không vô tình gửi form.
(function () {
    'use strict';
    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.matches('[data-confirm-action]')) return;
        var message = form.dataset.confirm || 'Bạn có chắc muốn thực hiện thao tác này không?';
        if (!window.confirm(message)) event.preventDefault();
    }, true);
})();
