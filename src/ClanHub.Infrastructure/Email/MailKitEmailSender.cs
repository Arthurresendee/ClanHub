using ClanHub.Infrastructure.Persistence;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ClanHub.Infrastructure.Email;

public class MailKitEmailSender(IOptions<EmailOptions> options, Microsoft.Extensions.Configuration.IConfiguration config, ILogger<MailKitEmailSender> logger)
    : IEmailSender<ApplicationUser>
{
    private readonly EmailOptions _o = options.Value;

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(email, "Confirme seu e-mail",
            $"<p>Olá, {System.Net.WebUtility.HtmlEncode(user.DisplayName)}!</p><p><a href=\"{confirmationLink}\">Confirmar meu e-mail</a></p>");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(email, "Redefinição de senha", $"<p><a href=\"{resetLink}\">Redefinir minha senha</a></p>");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
            {
        var baseUrl = (config["App:PublicUrl"] ?? "").TrimEnd('/');
        var link = baseUrl.Length == 0 ? "" :
            $"<p><a href=\"{baseUrl}/redefinir-senha?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(System.Net.WebUtility.HtmlDecode(resetCode))}\">Definir nova senha</a></p>";
        return SendAsync(email, "Redefinição de senha", $"{link}<p>Seu código: <b>{resetCode}</b></p>");
    }

    private async Task SendAsync(string to, string subject, string html)
    {
        if (string.IsNullOrWhiteSpace(_o.Host))
        {
            logger.LogWarning("SMTP não configurado; e-mail '{Subject}' para {To} NÃO foi enviado. Conteúdo (útil em desenvolvimento): {Body}", subject, to, html);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_o.FromName, _o.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_o.Host, _o.Port,
            _o.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);
        if (!string.IsNullOrEmpty(_o.User)) await client.AuthenticateAsync(_o.User, _o.Password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}