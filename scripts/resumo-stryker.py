"""Lê os relatórios JSON do Stryker e imprime score e mutantes sobreviventes (formato de anotação do GitHub)."""
import glob, json, sys

anotar = '--github' in sys.argv
linhas_md = ['| Projeto | Mutantes | Mortos | Sobreviventes | Sem cobertura | Score |', '|---|---|---|---|---|---|']
for caminho in sorted(glob.glob('tests/*/StrykerOutput/*/reports/mutation-report.json')):
    projeto = caminho.split('/')[1]
    dados = json.load(open(caminho, encoding='utf-8'))
    contagem, sobreviventes = {}, []
    for arquivo, info in dados['files'].items():
        for m in info['mutants']:
            contagem[m['status']] = contagem.get(m['status'], 0) + 1
            if m['status'] in ('Survived', 'NoCoverage'):
                linha = m['location']['start']['line']
                sobreviventes.append(f"{arquivo.split('/src/')[-1]}:{linha} {m['mutatorName']} -> {m.get('replacement', '')[:80]}")
    detectados = contagem.get('Killed', 0) + contagem.get('Timeout', 0)
    nao_detectados = contagem.get('Survived', 0) + contagem.get('NoCoverage', 0)
    validos = detectados + nao_detectados
    score = 100 * detectados / validos if validos else 0
    linhas_md.append(f"| {projeto} | {validos} | {detectados} | {contagem.get('Survived', 0)} | {contagem.get('NoCoverage', 0)} | {score:.1f}% |")
    if anotar:
        print(f"::notice::Stryker {projeto}: score {score:.1f}% ({detectados}/{validos})")
        for s in sobreviventes[:25]:
            print(f"::warning::{projeto} sobrevivente {s}")
print('\n'.join(linhas_md))
