using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Web.Models.Areas;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Services;

var count = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label); count++;
}
bool Valid(string name, int? order)
{
    var model = new AreaFormViewModel { Name = name, SortOrder = order };
    return Validator.TryValidateObject(model, new ValidationContext(model), new List<ValidationResult>(), true);
}
Check(Valid("Sân vườn", 0), "Valid area, zero order");
Check(Valid(new string('a',80), int.MaxValue), "Maximum field values");
Check(!Valid("", 1), "Empty name");
Check(!Valid(" \t ", 1), "Whitespace name");
Check(!Valid(new string('a',81), 1), "Name too long");
Check(!Valid("Sân vườn", -1), "Negative order");
Check(!Valid("Sân vườn", null), "Missing order");
Check(Valid("  Sân  vườn ", 2), "Whitespace preserved");
var expectedStatuses = new Dictionary<string, (string Label, string CssClass)>
{
    ["Available"] = ("Trống", "available"),
    ["Reserved"] = ("Đã đặt trước", "reserved"),
    ["Serving"] = ("Đang phục vụ", "serving"),
    ["Cleaning"] = ("Đang dọn", "cleaning")
};
foreach (var (status, expected) in expectedStatuses)
{
    var display = TableStatusDisplay.From(status);
    Check(display.Label == expected.Label && display.CssClass == expected.CssClass, $"{status} maps to its text label and color class");
}
Check(TableStatusDisplay.From("reserved").Label == "Đã đặt trước", "Status mapping ignores case");
Check(TableStatusDisplay.From("Unexpected").Label == "Không xác định" && TableStatusDisplay.From("Unexpected").CssClass == "unknown", "Invalid status has a readable fallback");
var demoTables = new DemoTableCatalog().Get(null).Tables;
Check(demoTables.Count == 60 && expectedStatuses.Keys.All(status => demoTables.Any(table => table.Status == status)), "Demo map contains all four statuses across 60 tables");
Check(demoTables.All(table => TableStatusDisplay.From(table.Status).Label == table.StatusLabel), "Every demo table displays the label resolved from its current status");
var detailCatalog = new DemoTableCatalog();
var detailService = new TableDetailsService(new ConfigurationBuilder().Build(), detailCatalog,
    new TestHostEnvironment { EnvironmentName = "Development" }, NullLogger<TableDetailsService>.Instance);
var detailCodes = detailCatalog.GetAll().GroupBy(table => table.Status).ToDictionary(group => group.Key, group => group.First().Code);
var emptyDetails = await detailService.GetAsync(detailCodes["Available"], CancellationToken.None);
Check(emptyDetails is { Status: "Available", HasActiveSession: false, CurrentGuestName: null, UpcomingReservation: null, CurrentSubtotal: null },
    "Available table details show no current guest, reservation, session, or subtotal");
var reservedDetails = await detailService.GetAsync(detailCodes["Reserved"], CancellationToken.None);
Check(reservedDetails is { Status: "Reserved", UpcomingReservation: not null } && reservedDetails.UpcomingReservation.GuestCount > 0,
    "Reserved table details include the upcoming guest and booking time");
var servingDetails = await detailService.GetAsync(detailCodes["Serving"], CancellationToken.None);
Check(servingDetails is { Status: "Serving", HasActiveSession: true, CurrentGuestName: not null, CurrentGuestPhone: not null, ServiceStartedAtUtc: not null, ServiceElapsedMinutes: not null, CurrentSubtotal: > 0 },
    "Serving table details include current guest, service start, elapsed time, and subtotal");
var cleaningDetails = await detailService.GetAsync(detailCodes["Cleaning"], CancellationToken.None);
Check(cleaningDetails is { Status: "Cleaning", HasActiveSession: false, ServiceStartedAtUtc: null, CurrentSubtotal: null },
    "Cleaning table details do not show a finished service as active");
Check(await detailService.GetAsync("X99", CancellationToken.None) is null, "Unknown table has no details");
detailCatalog.TryUpdateStatus(detailCodes["Available"], "Serving", out _, out _);
var updatedDetails = await detailService.GetAsync(detailCodes["Available"], CancellationToken.None);
Check(updatedDetails is { Status: "Serving", CurrentGuestName: not null, CurrentSubtotal: > 0 },
    "Reopening details after a status update reflects the current table state");
var sharedCatalog = new DemoTableCatalog();
var eventBroker = new TableMapEventBroker();
using (var sourceSubscription = eventBroker.Subscribe())
using (var receivingSubscription = eventBroker.Subscribe())
{
    foreach (var (status, reason) in new[]
    {
        ("Reserved", "ReservationConfirmed"),
        ("Serving", "ServiceStarted"),
        ("Cleaning", "ServiceClosed"),
        ("Available", "CleaningCompleted")
    })
    {
        Check(sharedCatalog.TryUpdateStatus("A01", status, out var changedTable, out var transition) && changedTable is not null && transition is not null,
            $"A01 transition to {status} accepted");
        eventBroker.Publish(transition!);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await receivingSubscription.Reader.ReadAsync(timeout.Token);
        Check(received.Code == "A01" && received.Status == status && received.ChangeReason == reason && received.ChangedAtUtc.Offset == TimeSpan.Zero,
            $"Connected map receives A01 {status} event ({reason}) with UTC timestamp");
        Check((await sourceSubscription.Reader.ReadAsync(timeout.Token)).Code == "A01",
            $"Source map also receives its A01 update");
    }

    Check(sharedCatalog.TryUpdateStatus("A01", "Available", out _, out var duplicate) && duplicate is null,
        "Repeated status does not create a change event");

    Check(sharedCatalog.TryUpdateStatus("A01", "Reserved", out _, out var reserved) && reserved?.ChangeReason == "ReservationConfirmed",
        "Available to reserved identifies reservation confirmation");
    Check(sharedCatalog.TryUpdateStatus("A01", "Available", out _, out var released) && released?.ChangeReason == "ReservationReleased",
        "Reserved to available identifies reservation release");
}
Check(!sharedCatalog.TryUpdateStatus("X99", "Available", out _, out _), "Unknown table code is rejected");
Check(!sharedCatalog.TryUpdateStatus("A01", "Offline", out _, out _), "Unknown status is rejected");
Check(sharedCatalog.GetAll().Single(table => table.Code == "A01").Status == "Available",
    "Snapshot retains state after stream clients reconnect");
var controller = new AreasController(new ConfigurationBuilder().Build());
Check(controller.Create() is ViewResult { Model: AreaFormViewModel }, "GET create form");
var form = new AreaFormViewModel { Name = "Sân vườn", SortOrder = -1 };
controller.ModelState.AddModelError("SortOrder", "Invalid");
Check(await controller.Create(form) is ViewResult result && ReferenceEquals(result.Model, form),
    "Invalid POST retains submitted form without accessing database");
Check(await controller.Edit(form) is ViewResult edit && ReferenceEquals(edit.Model, form),
    "Invalid edit retains submitted values without accessing database");
var longNotes = new AreaFormViewModel { Name = "Test", SortOrder = 0, Notes = new string('a',501) };
Check(!Validator.TryValidateObject(longNotes, new ValidationContext(longNotes), new List<ValidationResult>(), true), "Notes limited to 500 characters");
var day = new DateTime(2026,9,28);
Check(RestaurantManagement.Web.Models.Reservations.BookingTime.NextStart(day.AddHours(20).AddMinutes(30).AddSeconds(5)) == day.AddHours(21), "Default slot is future at half-hour boundary");
Check(RestaurantManagement.Web.Models.Reservations.BookingTime.NextStart(day.AddHours(21).AddMinutes(30)) == day.AddDays(1).AddHours(8), "Late default moves to next morning");
Check(RestaurantManagement.Web.Models.Reservations.BookingTime.NextStart(day.AddHours(23).AddMinutes(50)) == day.AddDays(1).AddHours(8), "Midnight rollover");
MenuTests.Run(Check);
PublicMenuTests.Run(Check);
await MenuImageTests.Run(Check);
SecurityAuditTests.Run(Check);
await AuditReadOnlyTests.Run(Check);
OpeningHoursTests.Run(Check);
CategoryTests.Run(Check);
Console.WriteLine($"{count} tests passed.");
if (args.Contains("--authentication-http")) await AuthenticationHttpTests.Run();
if (args.Contains("--confirmation")) await ReservationConfirmationTests.Run();
if (args.Contains("--integration")) await AreaHttpTests.Run();
if (args.Contains("--opening-hours-http")) await AreaHttpTests.Run(openingHoursOnly: true);
if (args.Contains("--menu-http")) await MenuHttpTests.Run();

sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "RestaurantManagement.AreaTests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
