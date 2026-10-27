namespace ClanHub.Infrastructure.Email;

public class EmailOptions
{
    public const string Section = "Email";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "no-reply@clanhub.local";
    public string FromName { get; set; } = "ClanHub";
    public bool UseSsl { get; set; }
}