# Evidência de carga — consolidation (2026-10-22)

Gerado por `scripts/load/run.sh consolidation 2m`.

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

## Resultado

- Requisições feitas (iterações do k6, uma requisição cada): 6001
- Requisições que o k6 deixou de enviar por falta de capacidade (`dropped_iterations`): 0
- Tempo de resposta, p95 (95% das respostas levaram até este valor): 6.621 ms
- Tempo de resposta, p99 (99% das respostas levaram até este valor): 8.720583 ms
- Proporção de requisições com falha (`http_req_failed`): 0

### Limites definidos no k6 (thresholds)

- `http_req_duration p(95)<500` → `PASS`
- `http_req_duration p(99)<2000` → `PASS`
- `http_req_failed rate<0.05` → `PASS`
- `dropped_iterations count==0` → `PASS`

## Como ler a "perda" de requisições

Lemos os 5% de "perda" do PDF como requisições que falham temporariamente — timeout, erro `5xx`,
conexão recusada —, e nunca como lançamento perdido (premissa A-004, em
[`docs/01-requisitos-e-assumptions.md`](../../01-requisitos-e-assumptions.md#premissas-assumptions)).
A proporção `http_req_failed` mede exatamente isso.

## Resumo bruto do k6 (summary)

```json
{
    "root_group": {
        "groups": {},
        "checks": {
            "status is 200": {
                "fails": 0,
                "name": "status is 200",
                "path": "::status is 200",
                "id": "6210a8cd14cd70477eba5c5e4cb3fb5f",
                "passes": 6001
            }
        },
        "name": "",
        "path": "",
        "id": "d41d8cd98f00b204e9800998ecf8427e"
    },
    "metrics": {
        "http_req_tls_handshaking": {
            "max": 0,
            "p(90)": 0,
            "p(95)": 0,
            "p(99)": 0,
            "avg": 0,
            "min": 0,
            "med": 0
        },
        "http_req_duration": {
            "p(90)": 5.839958,
            "p(95)": 6.621,
            "p(99)": 8.720583,
            "avg": 3.9162918448591855,
            "min": 1.269333,
            "med": 3.517459,
            "max": 42.693875,
            "thresholds": {
                "p(95)<500": false,
                "p(99)<2000": false
            }
        },
        "http_req_receiving": {
            "avg": 0.07629661806365615,
            "min": 0.019208,
            "med": 0.062375,
            "max": 1.557542,
            "p(90)": 0.134333,
            "p(95)": 0.156166,
            "p(99)": 0.209583
        },
        "http_req_failed": {
            "passes": 0,
            "fails": 6001,
            "thresholds": {
                "rate<0.05": false
            },
            "value": 0
        },
        "http_req_blocked": {
            "avg": 0.015424043826029011,
            "min": 0.001583,
            "med": 0.006,
            "max": 10.412958,
            "p(90)": 0.017667,
            "p(95)": 0.023583,
            "p(99)": 0.159417
        },
        "data_sent": {
            "count": 2772462,
            "rate": 23102.66085464239
        },
        "dropped_iterations": {
            "count": 0,
            "rate": 0,
            "thresholds": {
                "count==0": false
            }
        },
        "iterations": {
            "rate": 50.00575942563288,
            "count": 6001
        },
        "vus": {
            "value": 0,
            "min": 0,
            "max": 2
        },
        "http_req_connecting": {
            "med": 0,
            "max": 10.375291,
            "p(90)": 0,
            "p(95)": 0,
            "p(99)": 0.111125,
            "avg": 0.005231134144309281,
            "min": 0
        },
        "http_reqs": {
            "count": 6001,
            "rate": 50.00575942563288
        },
        "http_req_duration{expected_response:true}": {
            "p(99)": 8.720583,
            "avg": 3.9162918448591855,
            "min": 1.269333,
            "med": 3.517459,
            "max": 42.693875,
            "p(90)": 5.839958,
            "p(95)": 6.621
        },
        "http_req_waiting": {
            "p(90)": 5.688,
            "p(95)": 6.45075,
            "p(99)": 8.521917,
            "avg": 3.8022081666388923,
            "min": 1.232916,
            "med": 3.410458,
            "max": 42.507333
        },
        "checks": {
            "passes": 6001,
            "fails": 0,
            "value": 1
        },
        "data_received": {
            "count": 93633603,
            "rate": 780239.8643181499
        },
        "vus_max": {
            "value": 100,
            "min": 100,
            "max": 100
        },
        "iteration_duration": {
            "med": 3.695958,
            "max": 43.805084,
            "p(90)": 6.210833,
            "p(95)": 7.020458,
            "p(99)": 9.108208,
            "avg": 4.134259602732895,
            "min": 1.321042
        },
        "http_req_sending": {
            "avg": 0.03778706015664071,
            "min": 0.00425,
            "med": 0.028583,
            "max": 5.362583,
            "p(90)": 0.064833,
            "p(95)": 0.076791,
            "p(99)": 0.120208
        }
    }
}
```
