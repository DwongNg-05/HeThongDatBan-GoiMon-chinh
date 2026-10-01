# Removes the old Vietnamese-named files left behind by the English rename.
# Run from the repository root:  powershell -ExecutionPolicy Bypass -File tools/finish-english-rename.ps1
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
$old = @(
    "src\RestaurantManagement.Data\Entities\KhuVuc.cs",
    "src\RestaurantManagement.Data\Entities\TaiKhoan.cs",
    "src\RestaurantManagement.Data\Models\DonHang.cs",
    "src\RestaurantManagement.Data\Models\GhiNhanThayDoiGia.cs",
    "src\RestaurantManagement.Data\Models\MonAn.cs",
    "src\RestaurantManagement.Data\Models\NhomMon.cs",
    "src\RestaurantManagement.Web\Controllers\ThucDonApiController.cs",
    "src\RestaurantManagement.Web\Controllers\ThucDonController.cs",
    "src\RestaurantManagement.Web\Pages\DonHang\Details.cshtml",
    "src\RestaurantManagement.Web\Pages\DonHang\Details.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\GoiMon\Checkout.cshtml",
    "src\RestaurantManagement.Web\Pages\GoiMon\Checkout.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\GoiMon\Index.cshtml",
    "src\RestaurantManagement.Web\Pages\GoiMon\Index.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\GoiMon\ThemDong.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\QuanLyMon\Index.cshtml",
    "src\RestaurantManagement.Web\Pages\QuanLyMon\Index.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\QuanLyMon\NhatKyGia.cshtml",
    "src\RestaurantManagement.Web\Pages\QuanLyMon\NhatKyGia.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\QuanLyMon\Sua.cshtml",
    "src\RestaurantManagement.Web\Pages\QuanLyMon\Sua.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\QuanLyMon\Tao.cshtml",
    "src\RestaurantManagement.Web\Pages\QuanLyMon\Tao.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\QuanLyNhomMon\Index.cshtml",
    "src\RestaurantManagement.Web\Pages\QuanLyNhomMon\Index.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\QuanLyNhomMon\SapXep.cshtml",
    "src\RestaurantManagement.Web\Pages\QuanLyNhomMon\SapXep.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\QuanLyNhomMon\Sua.cshtml",
    "src\RestaurantManagement.Web\Pages\QuanLyNhomMon\Sua.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\QuanLyNhomMon\Tao.cshtml",
    "src\RestaurantManagement.Web\Pages\QuanLyNhomMon\Tao.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\QuanLyNhomMon\TrangThai.cshtml",
    "src\RestaurantManagement.Web\Pages\QuanLyNhomMon\TrangThai.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\QuanLyNhomMon\Xoa.cshtml",
    "src\RestaurantManagement.Web\Pages\QuanLyNhomMon\Xoa.cshtml.cs",
    "src\RestaurantManagement.Web\Pages\Shared\_ThucDonTheoNhom.cshtml",
    "src\RestaurantManagement.Web\Pages\ThucDon\Index.cshtml",
    "src\RestaurantManagement.Web\Pages\ThucDon\Index.cshtml.cs",
    "src\RestaurantManagement.Web\Services\AnhMonAnUpload.cs",
    "src\RestaurantManagement.Web\Services\IQuanLyMonStore.cs",
    "src\RestaurantManagement.Web\Services\InMemoryQuanLyMonStore.cs",
    "src\RestaurantManagement.Web\Services\NhomMonThucDon.cs",
    "src\RestaurantManagement.Web\Services\QuyTacTenNhomMon.cs",
    "src\RestaurantManagement.Web\Services\SqlQuanLyMonStore.cs",
    "src\RestaurantManagement.Web\Services\ThucDonCongKhai.cs",
    "src\RestaurantManagement.Web\Views\Account\DoiMatKhau.cshtml",
    "src\RestaurantManagement.Web\Views\Account\XacMinhEmail.cshtml",
    "src\RestaurantManagement.Web\Views\ThucDon\Index.cshtml",
    "src\RestaurantManagement.Web\wwwroot\js\anh-mon-preview.js"
)
foreach ($f in $old) {
    if (Test-Path $f) {
        git rm -q -f -- $f 2>$null
        if (Test-Path $f) { Remove-Item -Force $f }
        Write-Host "removed $f"
    }
}
foreach ($d in @("src\RestaurantManagement.Web\Pages\QuanLyMon","src\RestaurantManagement.Web\Pages\QuanLyNhomMon","src\RestaurantManagement.Web\Pages\GoiMon","src\RestaurantManagement.Web\Pages\DonHang","src\RestaurantManagement.Web\Pages\ThucDon","src\RestaurantManagement.Web\Views\ThucDon")) {
    if ((Test-Path $d) -and -not (Get-ChildItem $d -Recurse -File)) { Remove-Item -Recurse -Force $d; Write-Host "removed folder $d" }
}
git rm -q -f -- tools/finish-english-rename.ps1 2>$null
if (Test-Path tools/finish-english-rename.ps1) { Remove-Item -Force tools/finish-english-rename.ps1 }
Write-Host "Done. Now run: dotnet build RestaurantManagement.sln -m:1"
