using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace RestaurantManagement.DbTool;

/// <summary>Một email máy chủ SMTP giả đã nhận (đã giải mã tiêu đề và nội dung).</summary>
internal sealed record ReceivedMail(string To, DateTime ReceivedAtUtc, string Subject, string Text, string Html, string Raw);

/// <summary>
/// S2-09 Task 3: máy chủ SMTP giả chạy trên máy (127.0.0.1, cổng ngẫu nhiên) để kiểm chứng toàn bộ luồng gửi email
/// qua SMTP thật của web: đo thời gian từ lúc đặt bàn tới lúc thư tới máy chủ, đọc lại đúng nội dung khách nhận,
/// và cố tình từ chối thư (lỗi tạm thời 450) cho từng người nhận để kiểm tra việc gửi lại.
/// </summary>
internal sealed class FakeSmtpServer : IAsyncDisposable
{
    public const string RejectReply = "450 4.2.1 Mailbox busy, try again later (S2-09 test)";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _failuresBeforeSuccess = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<ReceivedMail> _received = new();

    public FakeSmtpServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoop();
    }

    public int Port { get; }

    /// <summary>Từ chối <paramref name="failures"/> lần gửi đầu tiên tới người nhận này (int.MaxValue = luôn từ chối).</summary>
    public void FailFirst(string recipient, int failures) => _failuresBeforeSuccess[recipient] = failures;

    /// <summary>Số lần web đã thử gửi tới người nhận này (mỗi lần = một lệnh RCPT TO).</summary>
    public int Attempts(string recipient) => _attempts.TryGetValue(recipient, out var n) ? n : 0;

    public IReadOnlyList<ReceivedMail> MailsTo(string recipient) =>
        _received.Where(m => string.Equals(m.To, recipient, StringComparison.OrdinalIgnoreCase)).ToArray();

    public IReadOnlyList<ReceivedMail> All => _received.ToArray();

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            _ = Task.Run(() => Serve(client));
        }
    }

    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
                await writer.WriteLineAsync("220 fake-smtp ESMTP ready");
                var recipients = new List<string>();
                while (await reader.ReadLineAsync() is string line)
                {
                    var command = (line.Length >= 4 ? line[..4] : line).ToUpperInvariant();
                    switch (command)
                    {
                        case "EHLO":
                            await writer.WriteLineAsync("250-fake-smtp");
                            await writer.WriteLineAsync("250 8BITMIME");
                            break;
                        case "HELO":
                        case "MAIL":
                        case "NOOP":
                            await writer.WriteLineAsync("250 OK");
                            break;
                        case "RSET":
                            recipients.Clear();
                            await writer.WriteLineAsync("250 OK");
                            break;
                        case "RCPT":
                        {
                            var address = Address(line);
                            var attempt = _attempts.AddOrUpdate(address, 1, (_, n) => n + 1);
                            var failures = _failuresBeforeSuccess.TryGetValue(address, out var f) ? f : 0;
                            if (attempt <= failures)
                            {
                                await writer.WriteLineAsync(RejectReply);
                            }
                            else
                            {
                                recipients.Add(address);
                                await writer.WriteLineAsync("250 OK");
                            }
                            break;
                        }
                        case "DATA":
                        {
                            if (recipients.Count == 0) { await writer.WriteLineAsync("554 No valid recipients"); break; }
                            await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                            var raw = new StringBuilder();
                            while (await reader.ReadLineAsync() is string dataLine && dataLine != ".")
                                raw.Append(dataLine.StartsWith("..") ? dataLine[1..] : dataLine).Append("\r\n");
                            var mail = raw.ToString();
                            foreach (var to in recipients) _received.Enqueue(Parse(to, mail));
                            recipients.Clear();
                            await writer.WriteLineAsync("250 OK queued");
                            break;
                        }
                        case "QUIT":
                            await writer.WriteLineAsync("221 Bye");
                            return;
                        default:
                            await writer.WriteLineAsync("502 Command not implemented");
                            break;
                    }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }
    }

    private static string Address(string line)
    {
        var match = Regex.Match(line, "<([^>]*)>");
        return (match.Success ? match.Groups[1].Value : line[(line.IndexOf(':') + 1)..]).Trim();
    }

    // ---------------- Giải mã MIME (đủ cho thư do System.Net.Mail tạo ra) ----------------

    internal static ReceivedMail Parse(string to, string raw)
    {
        var (headers, _) = SplitHeaders(raw);
        var subject = DecodeEncodedWords(headers.GetValueOrDefault("Subject") ?? "");
        string text = "", html = "";
        foreach (var (contentType, content) in Leaves(raw))
        {
            if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)) html += content;
            else if (contentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase)) text += content;
        }
        return new ReceivedMail(to, DateTime.UtcNow, subject, text, html, raw);
    }

    private static (Dictionary<string, string> Headers, string Body) SplitHeaders(string part)
    {
        var end = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var head = end < 0 ? part : part[..end];
        var body = end < 0 ? "" : part[(end + 4)..];
        var unfolded = Regex.Replace(head, "\r\n[ \t]+", " ");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in unfolded.Split("\r\n"))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return (headers, body);
    }

    private static IEnumerable<(string ContentType, string Content)> Leaves(string part)
    {
        var (headers, body) = SplitHeaders(part);
        var contentType = headers.GetValueOrDefault("Content-Type") ?? "text/plain";
        if (contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            var boundary = Regex.Match(contentType, "boundary=\"?([^\";]+)\"?", RegexOptions.IgnoreCase).Groups[1].Value;
            foreach (var section in body.Split("--" + boundary).Skip(1))
            {
                if (section.StartsWith("--", StringComparison.Ordinal)) break;
                var inner = section.StartsWith("\r\n", StringComparison.Ordinal) ? section[2..] : section;
                foreach (var leaf in Leaves(inner)) yield return leaf;
            }
            yield break;
        }

        var charsetName = Regex.Match(contentType, "charset=\"?([^\";]+)\"?", RegexOptions.IgnoreCase).Groups[1].Value;
        Encoding encoding;
        try { encoding = string.IsNullOrEmpty(charsetName) ? Encoding.UTF8 : Encoding.GetEncoding(charsetName); }
        catch (ArgumentException) { encoding = Encoding.UTF8; }
        var transfer = (headers.GetValueOrDefault("Content-Transfer-Encoding") ?? "").Trim().ToLowerInvariant();
        var content = transfer switch
        {
            "base64" => encoding.GetString(Convert.FromBase64String(Regex.Replace(body, @"\s", ""))),
            "quoted-printable" => DecodeQuotedPrintable(body, encoding),
            _ => body
        };
        yield return (contentType, content);
    }

    private static string DecodeQuotedPrintable(string value, Encoding encoding) => encoding.GetString(QuotedPrintableBytes(value));

    private static byte[] QuotedPrintableBytes(string value)
    {
        value = value.Replace("=\r\n", "");
        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value[i].ToString()));
            }
        }
        return bytes.ToArray();
    }

    /// <summary>
    /// Giải mã tiêu đề dạng =?utf-8?B?...?= / =?utf-8?Q?...?= (RFC 2047). Các đoạn mã hoá liền nhau được ghép byte
    /// trước khi giải mã, vì một ký tự tiếng Việt (nhiều byte UTF-8) có thể bị cắt sang đoạn sau.
    /// </summary>
    internal static string DecodeEncodedWords(string value)
    {
        const string word = @"=\?([^?]+)\?([BbQq])\?([^?]*)\?=";
        var joined = Regex.Replace(value, @"\?=\s+=\?", "?==?");
        return Regex.Replace(joined, $"(?:{word})+", run =>
        {
            var bytes = new List<byte>();
            Encoding encoding = Encoding.UTF8;
            foreach (Match m in Regex.Matches(run.Value, word))
            {
                try { encoding = Encoding.GetEncoding(m.Groups[1].Value); }
                catch (ArgumentException) { encoding = Encoding.UTF8; }
                var data = m.Groups[3].Value;
                bytes.AddRange(m.Groups[2].Value is "B" or "b"
                    ? Convert.FromBase64String(data)
                    : QuotedPrintableBytes(data.Replace('_', ' ')));
            }
            return encoding.GetString(bytes.ToArray());
        });
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try { await _acceptLoop; } catch (Exception) { }
        _stop.Dispose();
    }
}
