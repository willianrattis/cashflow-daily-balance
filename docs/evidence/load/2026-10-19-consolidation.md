# Evidência de carga — consolidation (2026-10-19)

Gerado por `scripts/load/run.sh consolidation 2m`. As linhas "Intervalo consultado" e "Dados
gravados antes do teste" foram acrescentadas à mão depois da execução, para descrever o cenário; a
versão atual do script não as escreve. Os números vêm do resumo do k6 desta mesma execução.

## Especificações da máquina

- Sistema operacional da máquina: Darwin 27.0.0 (arm64)
- Processadores da máquina: 10
- Memória da máquina: 32.0 GiB
- Processadores disponíveis ao Docker: 10
- Memória disponível ao Docker: 7.7 GiB

## Parâmetros do cenário

- Alvo: ConsolidationService `GET /consolidation`
- Taxa: 50 requisições por segundo, constantes (`constant-arrival-rate`: o k6 inicia 50
  requisições a cada segundo, por mais que cada uma demore)
- Duração: 2m
- Ferramenta: k6 (teste de carga), na imagem Docker `grafana/k6:0.54.0`
- Intervalo consultado: ~96 dias (de 95 dias atrás até hoje), cobrindo o histórico gravado abaixo e o
  dia de hoje
- Dados gravados antes do teste: `scripts/load/seed-consolidation-history.sh 90` — 90 dias de
  histórico sintético escritos direto em `daily_consolidations`, fora do caminho normal. No fim do
  teste, `rebuild-consolidation.sh` e `reconcile.sh` desfazem esse histórico (veja a nota no próprio
  `scripts/load/run.sh`)

## Resultado

- Requisições feitas (iterações do k6, uma requisição cada): 6001
- Requisições que o k6 deixou de enviar por falta de capacidade (`dropped_iterations`): 0
- Tempo de resposta, p95 (95% das respostas levaram até este valor): 6.065 ms
- Tempo de resposta, p99 (99% das respostas levaram até este valor): 8.0115 ms
- Proporção de requisições com falha (`http_req_failed`): 0

### Limites definidos no k6 (thresholds)

- `http_req_failed rate<0.05` → `PASS`
- `dropped_iterations count==0` → `PASS`
- `http_req_duration p(95)<500` → `PASS`
- `http_req_duration p(99)<2000` → `PASS`

## Como ler a "perda" de requisições

Lemos os 5% de "perda" do PDF como requisições que falham temporariamente — timeout, erro `5xx`,
conexão recusada —, e nunca como lançamento perdido (premissa A-004, em
[`docs/01-requisitos-e-assumptions.md`](../../01-requisitos-e-assumptions.md#premissas-assumptions)).
A proporção `http_req_failed` mede exatamente isso.

## Resumo bruto do k6 (summary)

```json
{
    "metrics": {
        "vus_max": {
            "value": 100,
            "min": 100,
            "max": 100
        },
        "http_req_failed": {
            "passes": 0,
            "fails": 6001,
            "thresholds": {
                "rate<0.05": false
            },
            "value": 0
        },
        "http_req_sending": {
            "p(95)": 0.067,
            "p(99)": 0.09925,
            "avg": 0.03513997817030504,
            "min": 0.00375,
            "med": 0.029,
            "max": 2.309042,
            "p(90)": 0.058
        },
        "http_reqs": {
            "count": 6001,
            "rate": 50.00596850232704
        },
        "http_req_receiving": {
            "p(99)": 0.210334,
            "avg": 0.07372555157473744,
            "min": 0.015042,
            "med": 0.056375,
            "max": 1.919833,
            "p(90)": 0.136584,
            "p(95)": 0.155959
        },
        "http_req_duration{expected_response:true}": {
            "max": 275.466042,
            "p(90)": 5.482958,
            "p(95)": 6.065,
            "p(99)": 8.0115,
            "avg": 4.117610394434253,
            "min": 1.247416,
            "med": 3.602666
        },
        "http_req_tls_handshaking": {
            "avg": 0,
            "min": 0,
            "med": 0,
            "max": 0,
            "p(90)": 0,
            "p(95)": 0,
            "p(99)": 0
        },
        "http_req_blocked": {
            "avg": 0.013116540576570553,
            "min": 0.001417,
            "med": 0.005791,
            "max": 13.45875,
            "p(90)": 0.016542,
            "p(95)": 0.021375,
            "p(99)": 0.124917
        },
        "vus": {
            "value": 0,
            "min": 0,
            "max": 1
        },
        "checks": {
            "passes": 6001,
            "fails": 0,
            "value": 1
        },
        "data_sent": {
            "rate": 23102.75744807509,
            "count": 2772462
        },
        "http_req_waiting": {
            "p(99)": 7.82,
            "avg": 4.008744864689204,
            "min": 1.207833,
            "med": 3.497208,
            "max": 275.395792,
            "p(90)": 5.324875,
            "p(95)": 5.900583
        },
        "iterations": {
            "count": 6001,
            "rate": 50.00596850232704
        },
        "dropped_iterations": {
            "count": 0,
            "rate": 0,
            "thresholds": {
                "count==0": false
            }
        },
        "iteration_duration": {
            "avg": 4.320781911848024,
            "min": 1.306125,
            "med": 3.774708,
            "max": 278.756042,
            "p(90)": 5.792167,
            "p(95)": 6.409959,
            "p(99)": 8.406042
        },
        "http_req_duration": {
            "med": 3.602666,
            "max": 275.466042,
            "p(90)": 5.482958,
            "p(95)": 6.065,
            "p(99)": 8.0115,
            "avg": 4.117610394434253,
            "min": 1.247416,
            "thresholds": {
                "p(95)<500": false,
                "p(99)<2000": false
            }
        },
        "http_req_connecting": {
            "min": 0,
            "med": 0,
            "max": 13.42925,
            "p(90)": 0,
            "p(95)": 0,
            "p(99)": 0.0865,
            "avg": 0.003929185469088485
        },
        "data_received": {
            "count": 93633603,
            "rate": 780243.1265418088
        }
    },
    "root_group": {
        "checks": {
            "status is 200": {
                "name": "status is 200",
                "path": "::status is 200",
                "id": "6210a8cd14cd70477eba5c5e4cb3fb5f",
                "passes": 6001,
                "fails": 0
            }
        },
        "name": "",
        "path": "",
        "id": "d41d8cd98f00b204e9800998ecf8427e",
        "groups": {}
    }
}
```
