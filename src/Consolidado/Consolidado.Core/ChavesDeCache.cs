using System.Globalization;

namespace Consolidado.Core;

/// <summary>Chaves versionadas (v1): mudar o formato do valor invalida tudo sem limpar o Redis.</summary>
public static class ChavesDeCache
{
    public static string Posicao(Guid contaId) => $"consolidado:v1:posicao:{contaId}";

    public static string Dia(Guid contaId, DateOnly dia) =>
        $"consolidado:v1:dia:{contaId}:{dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
}
