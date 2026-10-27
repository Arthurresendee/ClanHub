using Microsoft.AspNetCore.Identity;

namespace ClanHub.Infrastructure.Persistence;

public class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser() => Id = Guid.CreateVersion7();

    public string DisplayName { get; set; } = "";
    public string Locale { get; set; } = "pt-BR";
    public string TimeZone { get; set; } = "America/Sao_Paulo";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}