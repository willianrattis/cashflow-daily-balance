# Evidência de carga — consolidation (2026-10-30)

Gerado por `scripts/load/run.sh consolidation 2m`.

## Especificações da máquina

- Sistema operacional da máquina: Darwin 27.2.0 (arm64)
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

- Requisições feitas (iterações do k6, uma requisição cada): 6000
- Requisições que o k6 deixou de enviar por falta de capacidade (`dropped_iterations`): 0
- Tempo de resposta, p95 (95% das respostas levaram até este valor): 6.682080949999999 ms
- Tempo de resposta, p99 (99% das respostas levaram até este valor): 10.435520089999985 ms
- Proporção de requisições com falha (`http_req_failed`): 0

### Limites definidos no k6 (thresholds)

- `http_req_failed rate<0.05` → `PASS`
- `http_req_duration p(95)<500` → `PASS`
- `http_req_duration p(99)<2000` → `PASS`
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
        "name": "",
        "path": "",
        "id": "d41d8cd98f00b204e9800998ecf8427e",
        "groups": {},
        "checks": {
            "status is 200": {
                "passes": 6000,
                "fails": 0,
                "name": "status is 200",
                "path": "::status is 200",
                "id": "6210a8cd14cd70477eba5c5e4cb3fb5f"
            }
        }
    },
    "metrics": {
        "http_req_receiving": {
            "p(99)": 0.22505558999999992,
            "avg": 0.07823602350000006,
            "min": 0.018709,
            "med": 0.065625,
            "max": 2.899875,
            "p(90)": 0.1250041,
            "p(95)": 0.14408714999999997
        },
        "http_req_failed": {
            "passes": 0,
            "fails": 6000,
            "thresholds": {
                "rate<0.05": false
            },
            "value": 0
        },
        "http_req_tls_handshaking": {
            "p(90)": 0,
            "p(95)": 0,
            "p(99)": 0,
            "avg": 0,
            "min": 0,
            "med": 0,
            "max": 0
        },
        "http_req_duration": {
            "med": 3.7242295,
            "max": 226.480167,
            "p(90)": 5.8633503000000005,
            "p(95)": 6.682080949999999,
            "p(99)": 10.435520089999985,
            "avg": 4.287882756666672,
            "min": 1.273583,
            "thresholds": {
                "p(95)<500": false,
                "p(99)<2000": false
            }
        },
        "http_req_blocked": {
            "p(95)": 0.022835099999999987,
            "p(99)": 0.18233633,
            "avg": 0.014001153166666652,
            "min": 0.001417,
            "med": 0.00675,
            "max": 10.108292,
            "p(90)": 0.0172081
        },
        "http_req_sending": {
            "avg": 0.03783340483333328,
            "min": 0.004042,
            "med": 0.031208,
            "max": 3.804333,
            "p(90)": 0.0626661,
            "p(95)": 0.07463124999999997,
            "p(99)": 0.1208099099999999
        },
        "data_received": {
            "count": 93708000,
            "rate": 780886.9014356523
        },
        "vus": {
            "value": 0,
            "min": 0,
            "max": 1
        },
        "dropped_iterations": {
            "count": 0,
            "rate": 0,
            "thresholds": {
                "count==0": false
            }
        },
        "iteration_duration": {
            "max": 227.60725,
            "p(90)": 6.2484166000000005,
            "p(95)": 7.028564899999999,
            "p(99)": 11.074868569999998,
            "avg": 4.5142177596666615,
            "min": 1.33325,
            "med": 3.9291665
        },
        "checks": {
            "fails": 0,
            "passes": 6000,
            "value": 1
        },
        "iterations": {
            "count": 6000,
            "rate": 49.99916131615138
        },
        "data_sent": {
            "count": 2772000,
            "rate": 23099.612528061938
        },
        "http_req_duration{expected_response:true}": {
            "min": 1.273583,
            "med": 3.7242295,
            "max": 226.480167,
            "p(90)": 5.8633503000000005,
            "p(95)": 6.682080949999999,
            "p(99)": 10.435520089999985,
            "avg": 4.287882756666672
        },
        "vus_max": {
            "value": 100,
            "min": 100,
            "max": 100
        },
        "http_req_waiting": {
            "min": 1.228125,
            "med": 3.607354,
            "max": 226.273125,
            "p(90)": 5.7222791,
            "p(95)": 6.527537199999999,
            "p(99)": 10.257865509999984,
            "avg": 4.171813328333353
        },
        "http_reqs": {
            "count": 6000,
            "rate": 49.99916131615138
        },
        "http_req_connecting": {
            "med": 0,
            "max": 10.057584,
            "p(90)": 0,
            "p(95)": 0,
            "p(99)": 0.12754657999999996,
            "avg": 0.004062153500000003,
            "min": 0
        }
    }
}
```
