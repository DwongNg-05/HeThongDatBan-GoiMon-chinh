// Trang Giới thiệu Bếp Nhà (Views/About/Index.cshtml): bật các hiệu ứng khi cuộn tới từng phần.
// Chỉ thêm/bớt lớp CSS (about.css làm chuyển động); không cần thư viện ngoài.
// Không có JS hoặc người dùng chọn "giảm chuyển động" (html không có .ab-motion) thì nội dung hiện ngay, không ẩn gì.
(function () {
    'use strict';
    var root = document.documentElement;
    var page = document.querySelector('.about-page');
    if (!page || !root.classList.contains('ab-motion')) return;

    if (!('IntersectionObserver' in window)) {
        root.classList.remove('ab-motion');
        return;
    }

    // Gán độ trễ cho từng phần tử con để chúng xuất hiện lần lượt (stagger).
    page.querySelectorAll('[data-anim]').forEach(function (block) {
        var textIndex = 0;
        block.querySelectorAll('[data-anim-part]').forEach(function (part) {
            var kind = part.getAttribute('data-anim-part');
            if (kind === 'text') part.style.setProperty('--d', (0.2 + textIndex++ * 0.15).toFixed(2) + 's');
            else if (kind === 'side') part.style.setProperty('--d', (0.55 + textIndex++ * 0.2).toFixed(2) + 's');
        });
    });

    // Lời giới thiệu: tách theo dòng hiển thị để từng dòng nhô lên.
    function splitLines(el) {
        var original = el.getAttribute('data-original') || el.textContent.replace(/\s+/g, ' ').trim();
        el.setAttribute('data-original', original);
        el.setAttribute('aria-label', original);
        var words = original.split(' ');
        el.innerHTML = '';
        var spans = words.map(function (word) {
            var span = document.createElement('span');
            span.textContent = word;
            el.appendChild(span);
            el.appendChild(document.createTextNode(' '));
            return span;
        });
        var lines = [], top = null;
        spans.forEach(function (span) {
            if (span.offsetTop !== top) { lines.push([]); top = span.offsetTop; }
            lines[lines.length - 1].push(span.textContent);
        });
        el.innerHTML = '';
        lines.forEach(function (lineWords, i) {
            var line = document.createElement('span');
            line.className = 'ab-line';
            line.setAttribute('aria-hidden', 'true');
            var inner = document.createElement('span');
            inner.className = 'ab-line__inner';
            inner.style.setProperty('--i', i);
            inner.textContent = lineWords.join(' ');
            line.appendChild(inner);
            el.appendChild(line);
        });
        el.setAttribute('data-split', '');
        return lines.length;
    }

    var quotes = Array.prototype.slice.call(page.querySelectorAll('[data-anim="quote"]'));
    function splitPending() {
        quotes.forEach(function (q) { if (!q.classList.contains('is-in')) q._lines = splitLines(q); });
    }
    // Đợi font chữ có chân tải xong mới đo dòng, tránh tách sai.
    (document.fonts && document.fonts.ready ? document.fonts.ready : Promise.resolve()).then(function () {
        splitPending();
        quotes.forEach(function (q) { observer.observe(q); });
    });

    var resizeTimer;
    window.addEventListener('resize', function () {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(splitPending, 200);
    });

    function reveal(el) {
        el.classList.add('is-in');
        if (el.hasAttribute('data-split')) {
            // Khi các dòng đã hiện xong, trả lại đoạn văn thường để tự xuống dòng khi đổi cỡ màn hình.
            var duration = 1400 + (el._lines || 1) * 200 + 200;
            setTimeout(function () {
                el.textContent = el.getAttribute('data-original');
                el.removeAttribute('data-split');
                el.removeAttribute('aria-label');
                el.classList.add('is-done');
            }, duration);
        }
    }

    var observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
            if (!entry.isIntersecting) return;
            reveal(entry.target);
            observer.unobserve(entry.target);
        });
    }, { rootMargin: '0px 0px -12% 0px', threshold: 0.12 });

    page.querySelectorAll('[data-anim]:not([data-anim="quote"]):not([data-anim="hero"])').forEach(function (el) {
        observer.observe(el);
    });

    // Hai góc dưới của khung trang hiện khi cuộn tới cuối trang.
    var bottomMark = document.createElement('span');
    bottomMark.setAttribute('aria-hidden', 'true');
    bottomMark.style.cssText = 'position:absolute;left:0;bottom:0;width:1px;height:1px;';
    page.appendChild(bottomMark);
    var bottomObserver = new IntersectionObserver(function (entries) {
        if (entries.some(function (e) { return e.isIntersecting; })) {
            page.classList.add('is-bottom');
            bottomObserver.disconnect();
        }
    });
    bottomObserver.observe(bottomMark);

    // Mở trang: kẻ khung và phần đầu chạy ngay.
    requestAnimationFrame(function () {
        requestAnimationFrame(function () {
            page.classList.add('is-ready');
            var hero = page.querySelector('[data-anim="hero"]');
            if (hero) reveal(hero);
        });
    });
})();
