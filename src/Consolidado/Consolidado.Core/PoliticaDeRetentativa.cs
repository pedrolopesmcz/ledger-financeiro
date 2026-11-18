namespace Consolidado.Core;

public enum DestinoDaFalha
{
    Retentar,
    FilaDeMensagensMortas,
}

/// <summary>
/// Falha transitória (banco fora, timeout) volta para a fila de retry com
/// atraso. Depois de N tentativas, ou se a mensagem é inválida, vai para a
/// DLQ: não trava a fila e fica disponível para análise e reprocessamento.
/// </summary>
public sealed class PoliticaDeRetentativa
{
    public const string Secao = "Retentativa";

    public int MaximoDeTentativas { get; set; } = 5;

    public TimeSpan Atraso { get; set; } = TimeSpan.FromSeconds(2);

    public DestinoDaFalha Decidir(int tentativasJaFeitas, bool mensagemInvalida) =>
        mensagemInvalida || tentativasJaFeitas >= MaximoDeTentativas
            ? DestinoDaFalha.FilaDeMensagensMortas
            : DestinoDaFalha.Retentar;
}
