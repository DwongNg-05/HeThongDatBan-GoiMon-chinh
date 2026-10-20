// Trang Tra cứu đặt bàn: các hình nền (con dấu sáp, dải sơn xoáy, trăng màu nước) bay trong khung cửa sổ như trong một chiếc hộp.
// Chạm mép cửa sổ thì bật lại, va vào nhau thì nảy ra (va chạm đàn hồi giữa các hình tròn), vừa bay vừa lăn tròn.
// Vị trí xuất phát lấy từ chỗ máy chủ đã xếp ngẫu nhiên (Views/ReservationLookup/Index.cshtml); hướng, tốc độ bay chọn ngẫu nhiên.
// Người dùng chọn "giảm chuyển động" thì giữ nguyên hình đứng yên.
(function () {
    'use strict';
    var layer = document.querySelector('.bk-scatter');
    if (!layer) return;
    if (window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches) return;

    var items = Array.prototype.slice.call(layer.querySelectorAll('.bk-scatter__item'));
    if (!items.length) return;

    var SPEED_MIN = 55, SPEED_MAX = 95;    // px mỗi giây
    var bodies = [];
    var width = layer.clientWidth, height = layer.clientHeight;

    function rand(a, b) { return a + Math.random() * (b - a); }

    function setup() {
        bodies = items.map(function (el) {
            var rect = el.getBoundingClientRect();
            var style = getComputedStyle(el);
            var scale = parseFloat(style.getPropertyValue('--s')) || 1;
            var angle = parseFloat(style.getPropertyValue('--r')) || 0;
            var w = el.offsetWidth * scale, h = el.offsetHeight * scale;
            var speed = rand(SPEED_MIN, SPEED_MAX), dir = rand(0, Math.PI * 2);
            return {
                el: el,
                x: rect.left + rect.width / 2,      // tâm hình
                y: rect.top + rect.height / 2,
                r: Math.min(w, h) * 0.46,            // bán kính va chạm (hình gần tròn)
                vx: Math.cos(dir) * speed,
                vy: Math.sin(dir) * speed,
                angle: angle,
                spin: rand(15, 35) * (Math.random() < 0.5 ? -1 : 1),   // độ mỗi giây
                scale: scale
            };
        });
        layer.classList.add('is-bouncing');
        bodies.forEach(place);
    }

    function place(b) {
        b.el.style.transform = 'translate3d(' + (b.x - b.el.offsetWidth / 2).toFixed(1) + 'px,' +
            (b.y - b.el.offsetHeight / 2).toFixed(1) + 'px,0) rotate(' + b.angle.toFixed(1) + 'deg) scale(' + b.scale + ')';
    }

    // Chạm mép hộp (cửa sổ): đổi chiều vận tốc, đảo chiều lăn cho giống bật tường.
    function walls(b) {
        if (b.x - b.r < 0) { b.x = b.r; b.vx = Math.abs(b.vx); b.spin = -b.spin; }
        else if (b.x + b.r > width) { b.x = width - b.r; b.vx = -Math.abs(b.vx); b.spin = -b.spin; }
        if (b.y - b.r < 0) { b.y = b.r; b.vy = Math.abs(b.vy); b.spin = -b.spin; }
        else if (b.y + b.r > height) { b.y = height - b.r; b.vy = -Math.abs(b.vy); b.spin = -b.spin; }
    }

    // Hai hình chạm nhau: tách ra cho khỏi dính, rồi trao đổi vận tốc theo phương nối hai tâm (va chạm đàn hồi, khối lượng theo diện tích).
    function collide(a, b) {
        var dx = b.x - a.x, dy = b.y - a.y;
        var dist = Math.sqrt(dx * dx + dy * dy) || 0.01;
        var min = a.r + b.r;
        if (dist >= min) return;
        var nx = dx / dist, ny = dy / dist;
        var overlap = (min - dist) / 2;
        a.x -= nx * overlap; a.y -= ny * overlap;
        b.x += nx * overlap; b.y += ny * overlap;
        var rel = (a.vx - b.vx) * nx + (a.vy - b.vy) * ny;
        if (rel <= 0) return;
        var ma = a.r * a.r, mb = b.r * b.r;
        var impulse = 2 * rel / (ma + mb);
        a.vx -= impulse * mb * nx; a.vy -= impulse * mb * ny;
        b.vx += impulse * ma * nx; b.vy += impulse * ma * ny;
        a.spin = -a.spin; b.spin = -b.spin;
    }

    // Giữ tốc độ trong khoảng đẹp mắt (va chạm có thể làm hình quá nhanh hoặc gần đứng yên).
    function clampSpeed(b) {
        var s = Math.sqrt(b.vx * b.vx + b.vy * b.vy) || 1;
        var target = Math.min(Math.max(s, SPEED_MIN), SPEED_MAX * 1.3);
        b.vx *= target / s; b.vy *= target / s;
    }

    var last = null;
    function frame(now) {
        if (last === null) last = now;
        var dt = Math.min((now - last) / 1000, 0.05);   // tab ẩn lâu rồi mở lại: không nhảy cóc
        last = now;
        bodies.forEach(function (b) {
            b.x += b.vx * dt;
            b.y += b.vy * dt;
            b.angle = (b.angle + b.spin * dt) % 360;
            walls(b);
        });
        for (var i = 0; i < bodies.length; i++)
            for (var j = i + 1; j < bodies.length; j++) collide(bodies[i], bodies[j]);
        bodies.forEach(function (b) { clampSpeed(b); walls(b); place(b); });
        requestAnimationFrame(frame);
    }

    window.addEventListener('resize', function () {
        width = layer.clientWidth; height = layer.clientHeight;
        bodies.forEach(walls);
    });

    function start() {
        setup();
        requestAnimationFrame(frame);
    }
    // Đợi ảnh tải xong mới đo kích thước.
    var pending = items.filter(function (img) { return !img.complete; });
    if (!pending.length) start();
    else {
        var left = pending.length;
        pending.forEach(function (img) {
            var done = function () { if (--left === 0) start(); };
            img.addEventListener('load', done, { once: true });
            img.addEventListener('error', done, { once: true });
        });
    }
})();
