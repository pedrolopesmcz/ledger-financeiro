namespace Consolidado.Core;

/// <summary>
/// O dia contábil segue o fuso do banco (horário de Brasília), não o UTC.
/// Uma compra às 22h de segunda em São Paulo já é terça em UTC, mas pertence
/// ao consolidado de segunda.
/// </summary>
public sealed class DiaContabil
{
    public const string FusoPadrao = "America/Sao_Paulo";

    private readonly TimeZoneInfo _fuso;

    public DiaContabil(string fuso = FusoPadrao)
    {
        _fuso = TimeZoneInfo.FindSystemTimeZoneById(fuso);
    }

    public string Fuso => _fuso.Id;

    public DateOnly De(DateTimeOffset instante) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instante, _fuso).DateTime);
}
