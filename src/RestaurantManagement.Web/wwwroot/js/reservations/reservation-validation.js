(() => {
    const phone = document.getElementById("Phone");
    const guestCount = document.getElementById("GuestCount");

    const setError = (input, message) => {
        const messageElement = document.querySelector(`[data-client-validation-for="${input.name}"]`);
        if (messageElement) messageElement.textContent = message;
        input.classList.toggle("is-invalid", Boolean(message));
    };

    const validatePhone = () => {
        const value = phone.value.trim();
        setError(phone, value && !/^0\d{9}$/.test(value)
            ? "Số điện thoại phải gồm đúng 10 chữ số và bắt đầu bằng 0."
            : "");
    };

    const validateGuestCount = () => {
        const value = guestCount.value.trim();
        const isValid = /^\d+$/.test(value) && Number(value) >= 1 && Number(value) <= 20;
        setError(guestCount, value && !isValid
            ? "Số khách phải là số nguyên từ 1 đến 20. Đoàn trên 20 khách, vui lòng liên hệ trực tiếp nhà hàng."
            : "");
    };

    if (phone) phone.addEventListener("blur", validatePhone);
    if (guestCount) guestCount.addEventListener("blur", validateGuestCount);
})();
