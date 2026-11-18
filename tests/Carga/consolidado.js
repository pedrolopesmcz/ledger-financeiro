// Teste de carga do Consolidado (k6).
// Requisito: picos de 50 req/s no consolidado com no máximo 5% de perda.
// Cenários simultâneos:
//   pico      : 50 req/s de consultas por 60 s (mistura de cache hit e cache miss)
//   escrita   : 10 lançamentos/s ao mesmo tempo, gerando eventos e invalidação de cache
//   estresse  : depois do pico, sobe até 400 req/s para encontrar o limite
import http from 'k6/http';
import { check } from 'k6';

const GW = __ENV.GW || 'http://localhost:5000';
const leitores = [
  { token: __ENV.TOKEN_ANA, conta: __ENV.CONTA_ANA },
  { token: __ENV.TOKEN_BRUNO, conta: __ENV.CONTA_BRUNO },
];
const backoffice = __ENV.TOKEN_BACKOFFICE;

export const options = {
  scenarios: {
    pico: {
      executor: 'constant-arrival-rate', exec: 'consultar',
      rate: 50, timeUnit: '1s', duration: '60s', preAllocatedVUs: 20, maxVUs: 100,
    },
    escrita: {
      executor: 'constant-arrival-rate', exec: 'lancar',
      rate: 10, timeUnit: '1s', duration: '60s', preAllocatedVUs: 5, maxVUs: 30,
    },
    estresse: {
      executor: 'ramping-arrival-rate', exec: 'consultar', startTime: '65s',
      startRate: 50, timeUnit: '1s', preAllocatedVUs: 50, maxVUs: 500,
      stages: [
        { target: 200, duration: '30s' },
        { target: 400, duration: '30s' },
        { target: 400, duration: '20s' },
      ],
    },
  },
  thresholds: {
    // Critérios de aceite do requisito (com margem: a meta interna é perda abaixo de 1%).
    'http_req_failed{scenario:pico}': ['rate<0.05'],
    'http_req_duration{scenario:pico}': ['p(95)<200'],
    'http_req_failed{scenario:escrita}': ['rate<0.01'],
    // Estresse é exploratório: os limites abaixo só existem para as métricas aparecerem no resumo.
    'http_req_failed{scenario:estresse}': ['rate<=1'],
    'http_req_duration{scenario:estresse}': ['p(95)>=0'],
    'http_req_duration{scenario:escrita}': ['p(95)>=0'],
    'http_reqs{scenario:pico}': ['count>0'],
    'http_reqs{scenario:escrita}': ['count>0'],
    'http_reqs{scenario:estresse}': ['count>0'],
  },
  summaryTrendStats: ['avg', 'p(50)', 'p(95)', 'p(99)', 'max'],
};

function sortear(lista) {
  return lista[Math.floor(Math.random() * lista.length)];
}

function diaAleatorio(diasAtras) {
  const d = new Date(Date.now() - Math.floor(Math.random() * diasAtras) * 86400000);
  return d.toISOString().slice(0, 10);
}

export function consultar() {
  const { token, conta } = sortear(leitores);
  const params = { headers: { Authorization: `Bearer ${token}` }, tags: { name: '' } };
  const r = Math.random();
  let res;
  if (r < 0.5) {
    params.tags.name = 'saldo atual';           // tende a cache hit
    res = http.get(`${GW}/contas/${conta}/saldo`, params);
  } else if (r < 0.8) {
    params.tags.name = 'consolidado diario';    // datas variadas: mistura hit e miss
    res = http.get(`${GW}/contas/${conta}/consolidado/${diaAleatorio(60)}`, params);
  } else {
    params.tags.name = 'saldo em instante';     // nunca cacheado: sempre vai ao banco
    const instante = new Date(Date.now() - Math.floor(Math.random() * 30 * 86400000)).toISOString();
    res = http.get(`${GW}/contas/${conta}/saldo?em=${encodeURIComponent(instante)}`, params);
  }
  check(res, { 'status 200': (x) => x.status === 200 });
}

export function lancar() {
  const { conta } = sortear(leitores);
  const res = http.post(
    `${GW}/contas/${conta}/lancamentos`,
    JSON.stringify({ operacao: 'Deposito', valor: 1.0, descricao: 'teste de carga' }),
    {
      headers: {
        Authorization: `Bearer ${backoffice}`,
        'Content-Type': 'application/json',
        'Idempotency-Key': `carga-${__VU}-${__ITER}-${Date.now()}`,
      },
      tags: { name: 'registrar lancamento' },
    },
  );
  check(res, { 'status 201': (x) => x.status === 201 });
}

function linha(data, cenario) {
  const m = (nome) => data.metrics[`${nome}{scenario:${cenario}}`];
  const reqs = m('http_reqs');
  const falhas = m('http_req_failed');
  const dur = m('http_req_duration');
  if (!reqs || !dur) return `| ${cenario} | sem dados | | | | |`;
  const v = dur.values;
  return `| ${cenario} | ${reqs.values.count} | ${reqs.values.rate.toFixed(1)} | ${(falhas.values.rate * 100).toFixed(2)}% | ${v['p(50)'].toFixed(1)} | ${v['p(95)'].toFixed(1)} | ${v['p(99)'].toFixed(1)} |`;
}

export function handleSummary(data) {
  const aprovado = Object.values(data.metrics)
    .every((m) => !m.thresholds || Object.values(m.thresholds).every((t) => t.ok));
  const md = [
    '| Cenário | Requisições | Req/s | Perda | p50 (ms) | p95 (ms) | p99 (ms) |',
    '|---|---|---|---|---|---|---|',
    linha(data, 'pico'),
    linha(data, 'escrita'),
    linha(data, 'estresse'),
    '',
    `Critérios de aceite: **${aprovado ? 'atendidos' : 'NÃO atendidos'}**`,
  ].join('\n');
  return {
    stdout: md + '\n',
    '/resultados/resumo.md': md + '\n',
    '/resultados/k6-resultado.json': JSON.stringify(data, null, 2),
  };
}
