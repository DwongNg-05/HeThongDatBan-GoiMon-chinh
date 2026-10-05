using System.ComponentModel.DataAnnotations;
using RestaurantManagement.Web.Models.Reservations;

internal static class ReservationInputValidationTests
{
    internal static void Run(Action<bool, string> check)
    {
        bool IsValid(ReservationCreateViewModel model) =>
            Validator.TryValidateObject(model, new ValidationContext(model), [], true);

        ReservationCreateViewModel Form(string phone = "0912345678", int? guests = 2) => new()
        {
            CustomerName = "Nguyễn Văn A",
            Phone = phone,
            GuestCount = guests,
            ReservationDate = new DateOnly(2026, 10, 5),
            ReservationTime = new TimeOnly(18, 30)
        };

        check(IsValid(Form()), "Reservation validation accepts a 10-digit phone and guest count in range");
        check(!IsValid(Form("091234567")), "Reservation validation rejects a 9-digit phone");
        check(!IsValid(Form("09123456789")), "Reservation validation rejects an 11-digit phone");
        check(!IsValid(Form("09123abc78")), "Reservation validation rejects phone letters");
        check(!IsValid(Form("0912 345678")), "Reservation validation rejects phone spaces");
        check(!IsValid(Form("+84912345678")), "Reservation validation rejects the +84 form");
        check(!IsValid(Form(guests: 0)), "Reservation validation rejects zero guests");
        check(IsValid(Form(guests: 1)) && IsValid(Form(guests: 20)), "Reservation validation accepts guest boundaries");
        check(!IsValid(Form(guests: 21)) && !IsValid(Form(guests: -1)) && !IsValid(Form(guests: null)), "Reservation validation rejects out-of-range or missing guests");
        var missingName = Form();
        missingName.CustomerName = "";
        check(!IsValid(missingName), "Reservation validation requires customer name");
        var longNotes = Form();
        longNotes.Notes = new string('a', 501);
        check(!IsValid(longNotes), "Reservation validation limits notes to 500 characters");
        var missingDateAndTime = Form();
        missingDateAndTime.ReservationDate = null;
        missingDateAndTime.ReservationTime = null;
        check(!IsValid(missingDateAndTime), "Reservation validation requires date and time");
    }
}
