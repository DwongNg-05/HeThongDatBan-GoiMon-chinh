(() => {
    const date = document.getElementById("ReservationDate");
    const time = document.getElementById("ReservationTime");
    const guestCount = document.getElementById("GuestCount");
    const preferredArea = document.getElementById("PreferredAreaId");
    const message = document.querySelector("[data-reservation-slots-message]");
    if (!date || !time || !message) return;

    const render = (slots, selectedTime) => {
        time.replaceChildren(new Option("-- Chọn khung giờ --", ""));
        for (const slot of slots) time.add(new Option(slot, slot, false, slot === selectedTime));
        time.disabled = slots.length === 0;
    };

    const loadSlots = async (keepSelected) => {
        const selectedTime = keepSelected ? (time.dataset.selectedTime || time.value) : "";
        message.textContent = "";
        if (!date.value) {
            render([], "");
            message.textContent = "Vui lòng chọn ngày để xem khung giờ.";
            return;
        }
        time.disabled = true;
        try {
            const parameters = new URLSearchParams({ date: date.value });
            if (guestCount?.value) parameters.set("guestCount", guestCount.value);
            if (preferredArea?.value) parameters.set("preferredAreaId", preferredArea.value);
            const response = await fetch(`/api/reservation-slots?${parameters}`, { headers: { Accept: "application/json" } });
            const result = await response.json();
            if (!response.ok) throw new Error(result.message || "Không thể tải khung giờ.");
            render(result.slots || [], selectedTime);
            message.textContent = result.message || "";
        } catch (error) {
            render([], "");
            message.textContent = error.message || "Không thể tải khung giờ. Vui lòng thử lại.";
        }
    };

    date.addEventListener("change", () => loadSlots(false));
    guestCount?.addEventListener("change", () => loadSlots(false));
    preferredArea?.addEventListener("change", () => loadSlots(false));
    loadSlots(true);
})();
