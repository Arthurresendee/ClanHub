using ClanHub.Domain;

namespace ClanHub.Application.Workspaces;

/// <summary>Tabela de permissões da seção 6 do plano.</summary>
public static class WorkspacePermissions
{
    public static bool Allows(WorkspaceRole actual, WorkspaceRole minimum) => actual >= minimum;

    public static bool CanView(WorkspaceRole r) => Allows(r, WorkspaceRole.Viewer);
    public static bool CanManageNotes(WorkspaceRole r) => Allows(r, WorkspaceRole.Staff);
    public static bool CanManageClans(WorkspaceRole r) => Allows(r, WorkspaceRole.Admin);
    public static bool CanManageTeam(WorkspaceRole r) => Allows(r, WorkspaceRole.Admin);
    public static bool CanDeleteWorkspace(WorkspaceRole r) => r == WorkspaceRole.Owner;
}