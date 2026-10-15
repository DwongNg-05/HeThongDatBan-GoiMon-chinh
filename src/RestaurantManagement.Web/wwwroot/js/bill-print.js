// S4-01 Task 3: in hoá đơn tạm tính ra máy in nhiệt khổ 80mm qua hộp thoại in của trình duyệt (docs/S4-01-Task3.md).
// - Đưa bản in ([data-print-receipt]) ra thẳng dưới <body> để khi in chỉ còn nó (bill-print.css ẩn mọi thứ khác).
// - Đo chiều dài thật của bản in ở bề rộng 80mm rồi đặt @page { size: 80mm <dài>mm; margin: 0 }: cả hoá đơn là MỘT trang,
//   in liền một dải, không ngắt giữa chừng dù nhiều món. Đo lại ngay trước khi in (nút "In" hoặc Ctrl+P).
// - Chỉ gọi window.print(): không gửi yêu cầu nào lên máy chủ, nên in hay huỷ hộp thoại in đều không đổi bàn / đơn gọi món.
(function (root) {
    'use strict';
    const PX_PER_MM = 96 / 25.4;
    /** Chiều dài trang (mm) cho bản in cao `px` điểm ảnh CSS: làm tròn lên + 4mm dư để máy in cắt giấy không mất dòng cuối. */
    function pageHeightMm(px) {
        const height = Number(px);
        return Math.max(40, Math.ceil((Number.isFinite(height) && height > 0 ? height : 0) / PX_PER_MM) + 4);
    }
    function pageRule(px) {
        return `@page { size: 80mm ${pageHeightMm(px)}mm; margin: 0; }`;
    }
    const api = { PX_PER_MM, pageHeightMm, pageRule };
    if (typeof module === 'object' && module.exports) { module.exports = api; return; }

    const doc = root.document;
    const receipt = doc.querySelector('[data-print-receipt]');
    if (!receipt) return;
    if (receipt.parentElement !== doc.body) doc.body.appendChild(receipt);
    const style = doc.createElement('style');
    style.id = 'bill-print-page-size';
    doc.head.appendChild(style);

    function fit() {
        receipt.classList.add('bill-print-measuring');
        const height = receipt.getBoundingClientRect().height;
        receipt.classList.remove('bill-print-measuring');
        style.textContent = pageRule(height);
        receipt.dataset.printHeightMm = String(pageHeightMm(height));
        return height;
    }

    fit();
    root.addEventListener('load', fit);
    if (doc.fonts && doc.fonts.ready) doc.fonts.ready.then(fit, () => {});
    root.addEventListener('beforeprint', fit);
    doc.querySelectorAll('[data-preview-print]').forEach(button => button.addEventListener('click', () => {
        fit();
        root.print();
    }));
    root.billPrint = { ...api, fit };
})(typeof window !== 'undefined' ? window : globalThis);
