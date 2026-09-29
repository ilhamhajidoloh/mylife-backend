using System.Net;
using System.Net.Mail;

namespace back_mylife.Services;

public class EmailSenderService(IConfiguration configuration)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(configuration["Smtp:Host"]) &&
        !string.IsNullOrWhiteSpace(configuration["Smtp:User"]) &&
        !string.IsNullOrWhiteSpace(configuration["Smtp:Password"]);

    public async Task SendTestAsync(string recipient)
    {
        if (!IsConfigured) throw new InvalidOperationException("SMTP is not configured");
        var host = configuration["Smtp:Host"]!;
        var port = int.TryParse(configuration["Smtp:Port"], out var configuredPort) ? configuredPort : 587;
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = !string.Equals(configuration["Smtp:UseSsl"], "false", StringComparison.OrdinalIgnoreCase),
            Credentials = new NetworkCredential(configuration["Smtp:User"], configuration["Smtp:Password"]),
        };
        using var message = new MailMessage(configuration["Smtp:From"] ?? configuration["Smtp:User"]!, recipient)
        {
            Subject = "MyLife: ทดสอบการแจ้งเตือนทางอีเมล",
            Body = "การตั้งค่าการแจ้งเตือนทางอีเมลของคุณใช้งานได้เรียบร้อยแล้ว",
        };
        await client.SendMailAsync(message);
    }

    public async Task SendAsync(string recipient, string subject, string body)
    {
        if (!IsConfigured) return;
        var host = configuration["Smtp:Host"]!;
        var port = int.TryParse(configuration["Smtp:Port"], out var configuredPort) ? configuredPort : 587;
        using var client = new SmtpClient(host, port) { EnableSsl = !string.Equals(configuration["Smtp:UseSsl"], "false", StringComparison.OrdinalIgnoreCase), Credentials = new NetworkCredential(configuration["Smtp:User"], configuration["Smtp:Password"]) };
        using var message = new MailMessage(configuration["Smtp:From"] ?? configuration["Smtp:User"]!, recipient) { Subject = subject, Body = body };
        await client.SendMailAsync(message);
    }
}
