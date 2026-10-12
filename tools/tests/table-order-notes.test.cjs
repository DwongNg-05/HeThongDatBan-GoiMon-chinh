// S3-03 Task 1 + Task 2: ghi chú riêng và lựa chọn ghi chú nhanh cho từng dòng món trong giỏ của khách
// (wwwroot/js/table-order.js → TableOrderCart).
// Chạy: node --test tools/tests/table-order-notes.test.cjs (từ thư mục gốc của repo, không cần cài gói nào).
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const source = fs.readFileSync('src/RestaurantManagement.Web/wwwroot/js/table-order.js', 'utf8');
const context = {};
context.globalThis = context;
vm.runInNewContext(source, context); // không có document → chỉ nạp phần logic
const Cart = context.TableOrderCart;
const plain = value => JSON.parse(JSON.stringify(value)); // đối tượng từ vm khác realm

const goi = { dishId: 3, name: 'Gỏi cuốn tôm thịt', price: 45000 };
const bo = { dishId: 5, name: 'Bò lúc lắc', price: 145000 };

test('hai dòng cùng món, ghi chú "ít cay" và "không hành" → hai dòng riêng, không gộp số lượng', () => {
    const cart = [];
    Cart.addDish(cart, goi);
    cart[0].notes = 'ít cay';
    Cart.addDish(cart, goi); // dòng cũ đã có ghi chú → dòng mới
    cart[1].notes = 'không hành';
    assert.equal(cart.length, 2);
    assert.deepEqual(plain(Cart.payload(cart)), [
        { dishId: 3, quantity: 1, notes: 'ít cay', quickNotes: [] },
        { dishId: 3, quantity: 1, notes: 'không hành', quickNotes: [] }
    ]);
    assert.equal(Cart.count(cart), 2);
    assert.equal(Cart.total(cart), 90000);
});

test('"+ Thêm vào giỏ" cộng vào dòng cùng món chưa có ghi chú, không cộng vào dòng đã có ghi chú', () => {
    const cart = [];
    Cart.addDish(cart, goi);
    Cart.addDish(cart, goi);
    assert.equal(cart.length, 1);
    assert.equal(cart[0].quantity, 2);
    cart[0].notes = 'ít cay';
    Cart.addDish(cart, goi);
    Cart.addDish(cart, goi);
    assert.equal(cart.length, 2);
    assert.deepEqual(plain(cart.map(l => [l.quantity, l.notes])), [[2, 'ít cay'], [2, '']]);
    Cart.addDish(cart, bo);
    assert.equal(cart.length, 3, 'món khác luôn là dòng khác');
});

test('"+ Dòng ghi chú khác" thêm dòng cùng món ngay dưới, 1 phần, ghi chú trống', () => {
    const cart = [];
    Cart.addDish(cart, goi);
    Cart.addDish(cart, bo);
    cart[0].quantity = 3;
    const added = Cart.addSeparateLine(cart, 0);
    assert.equal(added, 1);
    assert.deepEqual(plain(cart.map(l => [l.dishId, l.quantity, l.notes])), [[3, 3, ''], [3, 1, ''], [5, 1, '']]);
});

test('ghi chú rỗng hoặc chỉ khoảng trắng được lưu là "không có ghi chú" (null)', () => {
    assert.equal(Cart.noteForSubmit(''), null);
    assert.equal(Cart.noteForSubmit('   '), null);
    assert.equal(Cart.noteForSubmit(undefined), null);
    assert.equal(Cart.noteForSubmit('  ít cay  '), 'ít cay');
});

test('ghi chú có dấu tiếng Việt giữ nguyên; dạng tổ hợp (NFD) được đổi về dạng dựng sẵn (NFC)', () => {
    const note = 'Không hành, ít đường, thêm ớt – cảm ơn!';
    assert.equal(Cart.cleanNoteInput(note).value, note);
    const decomposed = note.normalize('NFD');
    assert.ok(decomposed.length > note.length);
    assert.equal(Cart.cleanNoteInput(decomposed).value, note);
});

test('ghi chú đúng 200 ký tự được giữ nguyên; ký tự thứ 201 trở đi bị từ chối', () => {
    const exact = 'ệ'.repeat(200);
    assert.deepEqual(plain(Cart.cleanNoteInput(exact)), { value: exact, truncated: false });
    const over = Cart.cleanNoteInput('ệ'.repeat(201));
    assert.equal(over.value.length, 200);
    assert.equal(over.truncated, true);
    // 200 ký tự tiếng Việt gõ dạng tổ hợp (400 mã) vẫn là 200 ký tự sau NFC, không bị cắt.
    assert.deepEqual(plain(Cart.cleanNoteInput('ệ'.repeat(200).normalize('NFD'))), { value: exact, truncated: false });
    assert.equal(Cart.cleanNoteInput('😀'.repeat(101)).value.length, 200, 'không cắt đôi emoji');
    assert.equal(Cart.cleanNoteInput('a'.repeat(199) + '😀').value.length, 199);
});

test('xuống dòng và ký tự điều khiển trong ghi chú đổi thành khoảng trắng', () => {
    assert.equal(Cart.cleanNoteInput('ít cay\nkhông hành\t!').value, 'ít cay không hành !');
});

test('giỏ đọc lại từ sessionStorage: bỏ dòng hỏng, cắt ghi chú cũ quá dài, giữ các dòng cùng món', () => {
    const restored = Cart.sanitize([
        { dishId: 3, name: 'Gỏi', price: 45000, quantity: 1, notes: 'ít cay' },
        { dishId: 3, name: 'Gỏi', price: 45000, quantity: 1, notes: 'x'.repeat(250) },
        null, { dishId: 0, quantity: 1 }, { dishId: 4, quantity: 0 }, { dishId: '7', quantity: 1 }
    ]);
    assert.equal(restored.length, 2);
    assert.equal(restored[0].notes, 'ít cay');
    assert.equal(restored[1].notes.length, 200);
    assert.deepEqual(plain(Cart.sanitize('không phải mảng')), []);
});

test('số lượng tối đa 99 mỗi dòng, giỏ tối đa 100 dòng', () => {
    const cart = [];
    Cart.addDish(cart, goi);
    Cart.changeQuantity(cart, 0, 200);
    assert.equal(cart[0].quantity, 99);
    for (let i = 1; i < 100; i++) Cart.addSeparateLine(cart, 0);
    assert.equal(cart.length, 100);
    assert.equal(Cart.addSeparateLine(cart, 0), -1);
    cart.forEach(l => { l.notes = 'có ghi chú'; });
    assert.equal(Cart.addDish(cart, goi), -1);
    Cart.changeQuantity(cart, 0, -99);
    assert.equal(cart.length, 99, 'bớt về 0 thì bỏ dòng');
});

// ---- S3-03 Task 2: lựa chọn ghi chú nhanh ----

test('có đủ 3 lựa chọn nhanh: Ít cay, Không hành, Không mỡ', () => {
    assert.deepEqual(plain(Cart.quickOptions().map(o => o.label)), ['Ít cay', 'Không hành', 'Không mỡ']);
});

test('chọn từng lựa chọn nhanh', () => {
    for (const [code, label] of [['it-cay', 'Ít cay'], ['khong-hanh', 'Không hành'], ['khong-mo', 'Không mỡ']]) {
        const cart = [];
        Cart.addDish(cart, goi);
        assert.equal(Cart.toggleQuick(cart[0], code), true);
        assert.deepEqual(plain(Cart.quickLabels(cart[0])), [label]);
        assert.deepEqual(plain(Cart.payload(cart)[0].quickNotes), [code]);
        assert.equal(Cart.payload(cart)[0].notes, null);
    }
});

test('chọn nhiều lựa chọn trên cùng một dòng, luôn theo thứ tự hiển thị', () => {
    const cart = [];
    Cart.addDish(cart, goi);
    Cart.toggleQuick(cart[0], 'khong-mo');
    Cart.toggleQuick(cart[0], 'it-cay');
    Cart.toggleQuick(cart[0], 'khong-hanh');
    assert.deepEqual(plain(Cart.quickLabels(cart[0])), ['Ít cay', 'Không hành', 'Không mỡ']);
    assert.deepEqual(plain(Cart.payload(cart)[0].quickNotes), ['it-cay', 'khong-hanh', 'khong-mo']);
});

test('kết hợp lựa chọn nhanh với ghi chú tự do của chính dòng đó', () => {
    const cart = [];
    Cart.addDish(cart, goi);
    Cart.toggleQuick(cart[0], 'it-cay');
    Cart.toggleQuick(cart[0], 'khong-hanh');
    cart[0].notes = '  thêm chanh  ';
    assert.deepEqual(plain(Cart.payload(cart)), [{ dishId: 3, quantity: 1, notes: 'thêm chanh', quickNotes: ['it-cay', 'khong-hanh'] }]);
    // Lựa chọn nhanh không tính vào 200 ký tự của ghi chú tự do.
    cart[0].notes = 'ệ'.repeat(200);
    assert.equal(Cart.payload(cart)[0].notes.length, 200);
    assert.equal(Cart.payload(cart)[0].quickNotes.length, 2);
});

test('bỏ chọn một lựa chọn nhanh', () => {
    const cart = [];
    Cart.addDish(cart, goi);
    Cart.toggleQuick(cart[0], 'it-cay');
    Cart.toggleQuick(cart[0], 'khong-hanh');
    assert.equal(Cart.toggleQuick(cart[0], 'it-cay'), false);
    assert.deepEqual(plain(Cart.quickLabels(cart[0])), ['Không hành']);
    Cart.toggleQuick(cart[0], 'khong-hanh');
    assert.deepEqual(plain(Cart.payload(cart)[0].quickNotes), []);
    assert.equal(Cart.hasNote(cart[0]), false, 'bỏ hết thì dòng lại như chưa có yêu cầu');
});

test('ghi chú và lựa chọn nhanh của hai dòng cùng món không bị lẫn nhau', () => {
    const cart = [];
    Cart.addDish(cart, goi);
    Cart.toggleQuick(cart[0], 'it-cay');
    cart[0].notes = 'dòng một';
    Cart.addDish(cart, goi); // dòng 1 đã có yêu cầu → dòng mới
    assert.equal(cart.length, 2);
    assert.deepEqual(plain(cart[1].quick), [], 'dòng mới không thừa hưởng lựa chọn của dòng cũ');
    Cart.toggleQuick(cart[1], 'khong-mo');
    cart[1].notes = 'dòng hai';
    Cart.toggleQuick(cart[0], 'khong-hanh');
    assert.deepEqual(plain(Cart.payload(cart)), [
        { dishId: 3, quantity: 1, notes: 'dòng một', quickNotes: ['it-cay', 'khong-hanh'] },
        { dishId: 3, quantity: 1, notes: 'dòng hai', quickNotes: ['khong-mo'] }
    ]);
    const split = Cart.addSeparateLine(cart, 0);
    assert.deepEqual(plain(cart[split].quick), [], '"+ Dòng ghi chú khác" tạo dòng không có lựa chọn nhanh');
});

test('dòng chỉ có lựa chọn nhanh cũng không bị cộng dồn khi thêm cùng món', () => {
    const cart = [];
    Cart.addDish(cart, goi);
    Cart.toggleQuick(cart[0], 'khong-hanh');
    Cart.addDish(cart, goi);
    assert.equal(cart.length, 2);
    assert.equal(cart[0].quantity, 1);
});

test('mã lựa chọn lạ bị bỏ qua; giỏ cũ chưa có lựa chọn nhanh vẫn đọc được', () => {
    const cart = [];
    Cart.addDish(cart, goi);
    assert.equal(Cart.toggleQuick(cart[0], 'them-ot'), false);
    assert.deepEqual(plain(cart[0].quick), []);
    const restored = Cart.sanitize([
        { dishId: 3, name: 'Gỏi', price: 45000, quantity: 1, notes: 'ít cay' },
        { dishId: 3, name: 'Gỏi', price: 45000, quantity: 1, notes: '', quick: ['khong-mo', 'la', 'it-cay', 'it-cay'] }
    ]);
    assert.deepEqual(plain(restored.map(l => l.quick)), [[], ['it-cay', 'khong-mo']]);
});

test('danh sách lựa chọn nhanh lấy từ máy chủ (data-quick-notes dạng {Code, Label})', () => {
    const vmContext = {};
    vmContext.globalThis = vmContext;
    vm.runInNewContext(source, vmContext);
    const C = vmContext.TableOrderCart;
    C.setQuickOptions([{ Code: 'it-cay', Label: 'Ít cay' }, { Code: 'khong-hanh', Label: 'Không hành' }]);
    assert.deepEqual(plain(C.quickOptions()), [{ code: 'it-cay', label: 'Ít cay' }, { code: 'khong-hanh', label: 'Không hành' }]);
    C.setQuickOptions('hỏng');
    C.setQuickOptions([]);
    assert.equal(C.quickOptions().length, 2, 'dữ liệu hỏng không xoá danh sách');
});
