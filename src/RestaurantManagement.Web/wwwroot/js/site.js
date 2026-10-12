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
