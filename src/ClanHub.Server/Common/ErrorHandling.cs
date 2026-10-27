using ClanHub.Application.Games;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ClanHub.Server.Common;

/// <summary>Converte erros da Supercell em ProblemDetails (RFC 9457).</summary>
public class SupercellExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        // O navegador cancelou a requisição (trocou de tela): não é erro do servidor.
        if (ex is OperationCanceledException && ctx.RequestAborted.IsCancellationRequested)
        {
            ctx.Response.StatusCode = 499;
            return true;
        }

        (int status, string title)? map = ex switch
        {
            SupercellException s => s.Kind switch
            {
                SupercellErrorKind.NotFound => (404, "Não encontrado no jogo."),
                SupercellErrorKind.BadRequest => (400, "Tag ou parâmetro inválido."),
                SupercellErrorKind.PrivateWarLog => (409, "O log de guerra do clã é privado."),
                SupercellErrorKind.Throttled => (429, "Limite de requisições da Supercell atingido. Tente em instantes."),
                SupercellErrorKind.Maintenance => (503, "O jogo está em manutenção."),
                _ => (502, "Falha ao consultar a Supercell."),
            },
            GameFeatureNotSupportedException f => (400, f.Message),
            _ => null,
        };
        if (map is null) return false;

        ctx.Response.StatusCode = map.Value.status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = new ProblemDetails { Status = map.Value.status, Title = map.Value.title },
        });
    }
}