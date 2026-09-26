using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
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
Console.WriteLine($"{count} tests passed.");
if (args.Contains("--integration")) await AreaHttpTests.Run();

