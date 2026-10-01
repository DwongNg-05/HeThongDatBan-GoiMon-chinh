// Xem trước ảnh món trước khi lưu và báo lỗi sớm cho tệp không phải JPG/PNG hoặc lớn hơn 5 MB.
// Máy chủ vẫn kiểm tra lại đuôi tệp, chữ ký nội dung và dung lượng.
(function () {
    const maxBytes = 5 * 1024 * 1024;
    const allowed = /\.(jpe?g|png)$/i;
    document.querySelectorAll('input[type="file"][data-anh-preview]').forEach(input => {
        const preview = document.getElementById(input.dataset.anhPreview);
        const originalSrc = preview?.getAttribute('src');
        let objectUrl = null;
        input.addEventListener('change', () => {
            if (objectUrl) { URL.revokeObjectURL(objectUrl); objectUrl = null; }
            const file = input.files && input.files[0];
            let message = '';
            if (file && !allowed.test(file.name)) message = 'Chỉ chấp nhận ảnh định dạng JPG hoặc PNG.';
            else if (file && file.size > maxBytes) message = 'Ảnh không được lớn hơn 5 MB.';
            input.setCustomValidity(message);
            if (message) { input.reportValidity(); }
            if (!preview) return;
            if (file && !message) {
                objectUrl = URL.createObjectURL(file);
                preview.src = objectUrl;
                preview.hidden = false;
            } else if (originalSrc) {
                preview.src = originalSrc;
            } else {
                preview.removeAttribute('src');
                preview.hidden = true;
            }
        });
    });
})();
