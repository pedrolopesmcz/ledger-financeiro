namespace Ledger.Messaging;

public interface IPublicadorDeEventos
{
    /// <summary>
    /// Publica e só retorna depois da confirmação do broker (publisher confirm).
    /// Lança exceção se o broker recusar, não conseguir rotear ou estiver fora.
    /// </summary>
    Task PublicarAsync(string tipo, Guid mensagemId, ReadOnlyMemory<byte> corpo, string? traceParent, CancellationToken ct);
}
