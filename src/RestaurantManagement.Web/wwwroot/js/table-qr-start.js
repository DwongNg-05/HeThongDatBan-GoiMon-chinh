// S3-01 Task 1: khách quét QR bàn được đưa thẳng vào trang gọi món.
// Trang /q/{mã} (GET) chỉ hiển thị bàn; script gửi biểu mẫu POST đúng MỘT lần để mở phiên gọi món.
// Bấm thêm vào nút trong lúc đang gửi cũng không gửi lần hai (máy chủ vẫn tự chống tạo trùng phiên).
(function () {
    'use strict';
    var form = document.getElementById('qr-start-form');
    if (!form) return;
    var sent = false;
    form.addEventListener('submit', function (event) {
        if (sent) { event.preventDefault(); return; }
        sent = true;
        var button = form.querySelector('button[type="submit"]');
        if (button) {
            // Khoá sau khi trình duyệt đã nhận lệnh gửi, để không chặn chính lần gửi này.
            setTimeout(function () { button.disabled = true; button.textContent = 'Đang mở…'; }, 0);
        }
    });
    if (form.getAttribute('data-auto-start') !== 'true') return;
    if (typeof form.requestSubmit === 'function') form.requestSubmit();
    else { sent = true; form.submit(); }
})();
