param(
    [Parameter(Mandatory=$true)][string]$Sender,
    [Parameter(Mandatory=$true)][string]$Recipient,
    [string]$SmtpHost = 'smtp.gmail.com',
    [int]$Port = 587
)
$ErrorActionPreference = 'Stop'
# Run interactively: the password never appears in command arguments or repository files.
$null = [System.Net.Mail.MailAddress]::new($Sender)
$null = [System.Net.Mail.MailAddress]::new($Recipient)
$secret = Read-Host 'Nhap mat khau ung dung SMTP (khong phai mat khau dang nhap Gmail)' -AsSecureString
$credential = [System.Net.NetworkCredential]::new($Sender, $secret)
if ($SmtpHost -eq 'smtp.gmail.com') {
    $credential.Password = $credential.Password.Replace(' ', '')
}
$client = [System.Net.Mail.SmtpClient]::new($SmtpHost, $Port)
$client.EnableSsl = $true
$client.UseDefaultCredentials = $false
$client.Credentials = $credential
$client.Timeout = 30000
$message = [System.Net.Mail.MailMessage]::new($Sender, $Recipient)
$message.Subject = 'Bep Nha - Kiem tra gui email'
$message.Body = 'Email thu cau hinh SMTP cua du an dat ban. Day khong phai thong bao xac nhan mot luot dat that.'
try {
    $client.Send($message)
    Write-Host 'SMTP da nhan email thu. Hay kiem tra hop thu va Spam cua dia chi nhan.'
    $config = @{
        'ConfirmationEmail:Host' = $SmtpHost
        'ConfirmationEmail:Port' = $Port.ToString()
        'ConfirmationEmail:EnableSsl' = 'true'
        'ConfirmationEmail:From' = $Sender
        'ConfirmationEmail:Username' = $Sender
        'ConfirmationEmail:Password' = $credential.Password
        'ConfirmationEmail:Enabled' = 'false'
    }
    # dotnet user-secrets accepts JSON through stdin; preserves unrelated secrets.
    $config | ConvertTo-Json -Compress | & dotnet user-secrets set --id RestaurantManagement-Web-Team | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Khong luu duoc User Secrets.' }
    Write-Host 'Da luu SMTP vao User Secrets ngoai repository. Worker van tat de kiem tra hang doi cu truoc khi bat gui.'
}
catch [System.Net.Mail.SmtpException] {
    Write-Error ('Gui thu that bai. Kiem tra mat khau ung dung, SMTP va mang. Ma SMTP: ' + $_.Exception.StatusCode)
}
finally {
    $message.Dispose()
    $client.Dispose()
    $credential.Password = ''
    $secret.Dispose()
    if ($config) { $config['ConfirmationEmail:Password'] = '' }
}
