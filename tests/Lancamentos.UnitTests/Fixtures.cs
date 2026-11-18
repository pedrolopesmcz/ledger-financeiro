using Lancamentos.Domain.Comum;
using Lancamentos.Domain.Contas;
using Lancamentos.Domain.Politicas;

namespace Lancamentos.UnitTests;

internal static class Fixtures
{
    public static readonly DateTimeOffset Agora = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    public static readonly IPoliticaDeConta ContaDigital = new PoliticaContaDigital();

    public static readonly IPoliticaDeConta Cartao = new PoliticaCartaoCredito();

    public static Conta NovaConta(TipoConta tipo, decimal limite = 0m) =>
        Conta.Abrir(Guid.NewGuid(), Guid.NewGuid(), tipo, limite, Agora).Valor;

    public static ValorMonetario Reais(decimal quantia) => ValorMonetario.Criar(quantia).Valor;

    public static string Chave() => Guid.NewGuid().ToString();

    public static IPoliticaDeConta PoliticaDe(Conta conta) =>
        conta.Tipo == TipoConta.ContaDigital ? ContaDigital : Cartao;

    public static Lancamento Lancar(Conta conta, TipoOperacao operacao, decimal quantia) =>
        conta.Registrar(PoliticaDe(conta), operacao, Reais(quantia), Chave(), null, Agora).Valor;
}
