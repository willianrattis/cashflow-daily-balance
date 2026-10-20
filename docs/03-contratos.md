# Contratos externos e de integração

Este documento define o que cada serviço expõe: as APIs HTTP e o evento que Lançamentos publica.
O contrato é o mínimo necessário para que o `ConsolidationService` nunca entre no caminho do
registro. As APIs seguem o estilo REST (Representational State Transfer: recursos com endereço
próprio, como `/launches`, usados com os verbos do HTTP) e trocam JSON. Os exemplos mostram o
contrato; a especificação OpenAPI (a descrição formal da API, em JSON) é gerada a partir do código
e fica em `/openapi/v1.json` em cada serviço, no ambiente `Development` do Docker Compose.

## Convenções gerais

- Todos os endereços começam com `/api/v1`, a versão 1 da API.
- Instantes técnicos, como `recordedAtUtc`, seguem o formato ISO-8601 em UTC e terminam em `Z`:
  `2026-10-16T13:20:01.125Z`.
- `businessDate`, a data do negócio, tem o formato `AAAA-MM-DD` e é calculada pelo servidor no
  fuso `America/Sao_Paulo`.
- Valores em dinheiro são inteiros positivos em centavos de real: R$ 125,00 é `12500`.
- `Credit` (crédito) aumenta o saldo; `Debit` (débito) reduz o saldo.
- Toda resposta de erro que tem corpo usa o mesmo formato, o envelope de erro `ApiError`:

| Campo | O que é |
|---|---|
| `code` | Código estável do erro, para programas — por exemplo, `idempotency_payload_mismatch`. A lista está em [Códigos de status](#códigos-de-status). |
| `message` | Explicação em português, para pessoas. |
| `traceId` | Identificador do processamento desta requisição dentro do serviço. Aparece também nos logs; quem investiga um erro procura por ele ([`docs/05`](05-aspectos-transversais.md) §3). |
| `details` | Opcional. Dados que ajudam a entender ou corrigir o erro, como o campo que passou do limite. |

Exemplo de erro — a mesma `Idempotency-Key` reenviada com outro conteúdo:

```json
{
  "code": "idempotency_payload_mismatch",
  "message": "A chave já foi usada com um payload diferente.",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "details": { "launchId": "9f2c..." }
}
```

Esse envelope é o corpo real de toda resposta de erro (`content-type: application/json`). O
`/openapi/v1.json` descreve o mesmo envelope: para cada código de status de erro que um endpoint
pode devolver, ele declara o schema `ApiError` (`code`, `message`, `traceId`, `details`). As
respostas `401`, `403` e `413` não têm corpo (ver [Códigos de status](#códigos-de-status)) e são
declaradas sem schema.

## Autenticação e cabeçalhos

Toda operação exige o cabeçalho `Authorization: Bearer <token>`, com um token JWT (JSON Web Token,
token de acesso assinado) emitido pelo provedor de identidade. Nesta entrega, quem emite o token
é o script `scripts/dev-jwt/make_token.py` (ADR-007, decisão sobre o provedor de identidade
externo). As duas APIs aceitam o mesmo `audience` — o campo do token que diz para qual API ele
vale —, `cashflow-api`. A autorização usa só a permissão (scope) que o token carrega; não há
perfis de usuário.

| Operação | Permissão (scope) exigida |
|---|---|
| Registrar lançamento | `cashflow.launches.write` |
| Consultar lançamento | `cashflow.launches.read` |
| Consultar consolidado | `cashflow.consolidation.read` |

Cabeçalhos aceitos pelas duas APIs:

| Cabeçalho | Uso | O que é |
|---|---|---|
| `Authorization` | Obrigatório na requisição | `Bearer <token>`. Sem token, ou com token inválido: `401`. Token válido, mas sem a permissão da operação: `403`. |
| `X-Correlation-Id` | Opcional na requisição; sempre presente na resposta | Identificador que liga todos os logs de uma mesma operação, nos dois serviços (exemplo de uso em [`docs/05`](05-aspectos-transversais.md) §3). Se o cliente não enviar, o serviço gera um e o devolve no cabeçalho da resposta. Mais de 100 caracteres, ou algum caractere de controle (não imprimível): `400 invalid_correlation_id`. |

O `X-Correlation-Id` não substitui a `Idempotency-Key`: o primeiro serve para investigar um
problema, a segunda para evitar um lançamento duplicado.

## LaunchService API

### Registrar lançamento

`POST /api/v1/launches`

Cabeçalhos obrigatórios:

```http
Authorization: Bearer <token>
Idempotency-Key: venda-0001              # identifica esta operação; repetir a chave não duplica
Content-Type: application/json
```

Cabeçalho opcional:

```http
X-Correlation-Id: 01J...                 # se ausente, o serviço gera um identificador
```

A `Idempotency-Key` é um identificador que o cliente escolhe para cada operação, com até 200
caracteres. Se a resposta se perder, o cliente reenvia a requisição com a mesma chave e recebe o
lançamento original, sem criar um segundo. Sem esse cabeçalho, a resposta é
`400 missing_idempotency_key`.

Corpo:

```json
{
  "amountInCents": 12500,
  "nature": "Credit",
  "description": "Venda do pedido 123"
}
```

| Campo | Obrigatório | O que é |
|---|---|---|
| `amountInCents` | Sim | Valor em centavos, inteiro maior que zero: `12500` é R$ 125,00. Zero ou negativo: `422 invalid_amount`. |
| `nature` | Sim | `Credit` (crédito) ou `Debit` (débito). Qualquer outro valor: `422 invalid_nature`. |
| `description` | Não | Texto livre, com até 1000 caracteres. Não entra em nenhum cálculo de saldo, mas entra no fingerprint de idempotência (A-017, premissa: mudar só a descrição numa repetição gera `409`). |

O servidor define a `businessDate`; o cliente não pode escolher uma data passada nem futura. Um
campo `businessDate` — ou qualquer outro campo desconhecido — no corpo é rejeitado com
`400 invalid_request`: o servidor nunca o ignora em silêncio.

Limites de tamanho, iguais aos das colunas do banco ([`docs/04-schema-logico.md`](04-schema-logico.md)).
Passar do limite gera `400 invalid_request`, com `details.field` dizendo qual campo:

| Campo | Limite |
|---|---|
| `Idempotency-Key` (cabeçalho) | 200 caracteres |
| `description` | 1000 caracteres |

O corpo inteiro da requisição pode ter no máximo 64 KB (kilobytes; 65.536 bytes). Um corpo maior
recebe `413`, sem corpo de resposta: o Kestrel, servidor web embutido no .NET, recusa a requisição
antes de ela chegar ao código da aplicação. JSON malformado, ou com um tipo errado como
`"amountInCents": "abc"`, recebe `400 invalid_request` com o envelope de erro, em qualquer
ambiente — nunca o corpo padrão do framework nem um `500`.

Respostas de sucesso:

- `201 Created` — lançamento novo. O cabeçalho `Location` traz o endereço dele
  (`/api/v1/launches/{launchId}`).
- `200 OK` com o cabeçalho `Idempotency-Replayed: true` — a mesma `Idempotency-Key` com o mesmo
  conteúdo. A resposta é o lançamento original; nada novo é gravado.

```json
{
  "launchId": "9f2c...",
  "amountInCents": 12500,
  "nature": "Credit",
  "businessDate": "2026-10-16",
  "recordedAtUtc": "2026-10-16T13:20:01.125Z",
  "status": "Recorded",
  "correlationId": "01J..."
}
```

| Campo | O que é |
|---|---|
| `launchId` | Identificador do lançamento: um UUID (Universally Unique Identifier, identificador de 128 bits) gerado pelo servidor. |
| `amountInCents` | Valor em centavos, como enviado. |
| `nature` | `Credit` ou `Debit`. |
| `businessDate` | O dia do negócio ao qual o lançamento pertence, definido pelo servidor no fuso de São Paulo. |
| `recordedAtUtc` | O instante do registro, em UTC. |
| `status` | Sempre `Recorded` (gravado). Não há outros estados, porque um lançamento nunca muda. |
| `correlationId` | O `X-Correlation-Id` da requisição que criou o lançamento. |

O `201` só sai depois que o lançamento e a mensagem do Outbox foram gravados juntos, na mesma
transação (commit). A resposta não espera o broker (o RabbitMQ) nem o Consolidado.

### Consultar um lançamento

`GET /api/v1/launches/{launchId}`

Devolve o lançamento, com os mesmos campos da resposta de registro, inclusive `recordedAtUtc` e
`correlationId`. Um `launchId` que não existe recebe `404 launch_not_found`. Não há endpoint
`PUT`, `PATCH` nem `DELETE`: um lançamento nunca muda.

## ConsolidationService API

### Consultar consolidado por intervalo

`GET /api/v1/consolidation?fromDate=2026-10-01&toDate=2026-10-16`

| Parâmetro | Obrigatório | O que é |
|---|---|---|
| `fromDate` | Sim | Primeiro dia do intervalo, no formato `AAAA-MM-DD`. |
| `toDate` | Sim | Último dia do intervalo, no formato `AAAA-MM-DD`. |

Regras:

- O intervalo inclui os dois dias das pontas: um lançamento entra se
  `fromDate <= businessDate <= toDate`.
- As datas são dias do calendário no fuso do negócio, `America/Sao_Paulo`.
- Parâmetro ausente: `400 missing_required_parameter`. Data inválida: `400 invalid_date_format`.
  Os dois usam o envelope de erro, nunca o corpo padrão do framework.
- `fromDate` depois de `toDate`: `422 invalid_date_range`.
- `toDate` depois de hoje, no fuso de São Paulo: `422 future_date_not_allowed`.
- Intervalo com mais de 366 dias: `422 date_range_too_large` (A-015, premissa: limite de 366 dias
  por consulta).
- Período sem lançamentos: os totais vêm zerados, e o intervalo pedido não muda.
- O saldo de abertura (`openingBalanceInCents`) é a soma de tudo o que foi lançado antes de
  `fromDate`.
- O saldo de fechamento é a abertura mais a variação:
  `closingBalanceInCents = openingBalanceInCents + netChangeInCents`.
- `days` traz um item por dia do calendário no intervalo, inclusive os dias sem movimento, com
  zeros. É o "relatório que disponibilize o saldo diário consolidado" que o PDF pede no BR-02
  (requisito de negócio). O saldo passa de um dia para o seguinte: a abertura de um dia é o
  fechamento do dia anterior, a abertura do primeiro dia é a abertura do intervalo, e o
  fechamento do último dia é igual a `closingBalanceInCents`.
- O saldo de um dia pode ficar negativo quando os débitos passam do que havia antes; ele não é
  forçado a zero (INV-007, regra: o saldo pode ficar negativo).
- Todas as leituras de uma mesma consulta — abertura, totais do intervalo, `days` e os campos de
  atualização — são feitas numa única transação `REPEATABLE READ`. Assim, todas enxergam a mesma
  fotografia dos dados (snapshot). Sem isso, a resposta poderia dizer que está atualizada até as
  13:20:05 (`asOfUtc`) sem incluir nos totais o evento aplicado nesse instante, porque ele chegou
  entre uma leitura e outra.
- Um evento atrasado pode mudar o resultado de um dia que já foi consultado. Os campos
  `asOfUtc`, `lastAppliedEventRecordedAtUtc` e `lastAppliedLagSeconds` tornam isso visível (ver
  [Como medir o atraso do Consolidado](#como-medir-o-atraso-do-consolidado-nfr-005)).

Resposta `200 OK`:

```json
{
  "fromDate": "2026-10-01",
  "toDate": "2026-10-03",
  "firstTransactionDate": "2026-09-10",
  "openingBalanceInCents": 100000,
  "creditsInCents": 50000,
  "debitsInCents": 20000,
  "netChangeInCents": 30000,
  "closingBalanceInCents": 130000,
  "days": [
    { "businessDate": "2026-10-01", "openingBalanceInCents": 100000, "creditsInCents": 50000, "debitsInCents": 0, "netChangeInCents": 50000, "closingBalanceInCents": 150000 },
    { "businessDate": "2026-10-02", "openingBalanceInCents": 150000, "creditsInCents": 0, "debitsInCents": 0, "netChangeInCents": 0, "closingBalanceInCents": 150000 },
    { "businessDate": "2026-10-03", "openingBalanceInCents": 150000, "creditsInCents": 0, "debitsInCents": 20000, "netChangeInCents": -20000, "closingBalanceInCents": 130000 }
  ],
  "asOfUtc": "2026-10-16T13:20:05.300Z",
  "lastAppliedEventRecordedAtUtc": "2026-10-16T13:20:03.100Z",
  "lastAppliedLagSeconds": 2.2,
  "projectionVersion": 1842
}
```

| Campo | O que é |
|---|---|
| `fromDate`, `toDate` | O intervalo consultado. |
| `firstTransactionDate` | O primeiro dia, em todo o histórico, que tem algum lançamento. |
| `openingBalanceInCents` | Saldo de abertura: a soma de tudo o que foi lançado antes de `fromDate`. |
| `creditsInCents`, `debitsInCents` | A soma dos créditos e a soma dos débitos dentro do intervalo. |
| `netChangeInCents` | A variação no intervalo: créditos menos débitos. |
| `closingBalanceInCents` | Saldo de fechamento: abertura mais variação. |
| `days` | Um item por dia do intervalo, com os mesmos cinco valores — abertura, créditos, débitos, variação e fechamento — daquele dia. |
| `asOfUtc` | Quando o último evento foi aplicado ao Consolidado: os totais estão atualizados até esse instante. Se nenhum evento foi aplicado ainda, é o instante da consulta. |
| `lastAppliedEventRecordedAtUtc` | Quando o lançamento desse último evento foi registrado em Lançamentos. |
| `lastAppliedLagSeconds` | Quanto esse último evento demorou, em segundos, entre o registro e a aplicação (`asOfUtc − lastAppliedEventRecordedAtUtc`). |
| `projectionVersion` | Contador que aumenta a cada evento aplicado ao Consolidado e recomeça numa reconstrução. Se mudou entre duas consultas, os totais podem ter mudado. |

Se não houver nenhum lançamento no histórico, `firstTransactionDate` é `null`, os totais e cada
dia de `days` são zero, e `lastAppliedEventRecordedAtUtc` e `lastAppliedLagSeconds` são `null`,
porque nunca houve evento a aplicar. Se o banco do Consolidado estiver fora do ar, a API devolve
`503 consolidation_unavailable`; isso não impede novos registros em `LaunchService`.

### Como medir o atraso do Consolidado (NFR-005)

O NFR-005 (requisito: o Consolidado pode ficar alguns segundos atrás dos lançamentos) é medido
assim:

- **Atraso de um evento** é o tempo entre o registro do lançamento e a aplicação do evento no
  Consolidado (`applied_at − recorded_at`). Inclui a espera no Outbox, no broker e no consumidor.
  A métrica `consumer_lag_seconds`, um histograma do `ConsolidationService`, registra esse atraso
  para cada evento aplicado; é ela que mede o NFR-005 no sistema inteiro.
- **Os campos de cada resposta** — `asOfUtc`, `lastAppliedEventRecordedAtUtc` e
  `lastAppliedLagSeconds` — descrevem só o último evento aplicado. Exemplo: um lançamento
  registrado às 13:20:03,100 e aplicado às 13:20:05,300 gera, na consulta seguinte, `asOfUtc` às
  13:20:05,300, `lastAppliedEventRecordedAtUtc` às 13:20:03,100 e `lastAppliedLagSeconds` de 2,2.
- **Um período sem lançamentos não é atraso.** Se ninguém registrou nada na última hora,
  `asOfUtc` fica uma hora no passado, e isso é normal: falta de movimento não é trabalho
  acumulado (backlog). Para saber se há trabalho acumulado, use as métricas e os painéis, nunca
  uma resposta isolada: `consumer_lag_seconds`, `outbox_oldest_age_seconds` (a idade da mensagem
  mais antiga ainda não publicada) e o tamanho da fila principal no RabbitMQ.

## Comportamento de idempotência e conflitos

Idempotência quer dizer que repetir uma requisição tem o mesmo efeito de fazê-la uma vez. Para
isso, o serviço calcula um fingerprint (impressão digital) de cada requisição: um resumo SHA-256
do valor, da natureza e da descrição. O fingerprint fica gravado junto com o lançamento, e o
banco garante que uma `Idempotency-Key` nunca se repita, com uma restrição `UNIQUE` (regra que o
próprio banco aplica a toda gravação).

| Situação | Resultado |
|---|---|
| Mesma `Idempotency-Key`, mesmo conteúdo | `200` com `Idempotency-Replayed: true` e o lançamento original |
| Mesma `Idempotency-Key`, conteúdo diferente | `409 idempotency_payload_mismatch`, com o `launchId` original em `details` |
| Duas requisições com a mesma chave ao mesmo tempo | O banco aceita só uma (restrição `UNIQUE`); a outra lê o lançamento gravado e recebe a mesma resposta de uma repetição |

Exemplo: a chave `venda-0001` chega com R$ 100,00 a crédito e é gravada (`201`). Se o cliente não
receber a resposta e reenviar a mesma requisição, recebe `200` com o mesmo `launchId`. Se
reenviar a mesma chave com R$ 150,00, recebe `409`.

## Evento `LaunchRecorded.v1`

Depois do commit do lançamento, o publisher do Outbox (a tarefa de `LaunchService` que envia as
mensagens gravadas no Outbox) publica este evento no RabbitMQ. A entrega é "pelo menos uma vez"
(at-least-once): nenhum evento se perde, mas o mesmo evento pode chegar mais de uma vez. O
consumidor descarta as cópias com a Inbox e as restrições do banco, pelo `eventId` e pelo
`launchId`, e assim cada lançamento é somado uma única vez.

Roteamento: o evento vai para a exchange `cashflow.events` (o ponto do RabbitMQ que recebe as
mensagens e as distribui às filas), com a routing key `launch.recorded.v1` (o rótulo que escolhe a
fila de destino), e chega à fila durável `consolidation.launch-recorded.v1`, do Consolidado. A
mensagem leva também o cabeçalho AMQP (Advanced Message Queuing Protocol, o protocolo do RabbitMQ)
`x-correlation-id`, com o mesmo `correlationId` do corpo.

```json
{
  "eventId": "e7c1...",
  "eventType": "LaunchRecorded",
  "eventVersion": 1,
  "occurredAtUtc": "2026-10-16T13:20:01.125Z",
  "correlationId": "01J...",
  "causationId": "venda-0001",
  "payload": {
    "launchId": "9f2c...",
    "amountInCents": 12500,
    "nature": "Credit",
    "businessDate": "2026-10-16",
    "recordedAtUtc": "2026-10-16T13:20:01.125Z"
  }
}
```

| Campo | O que é |
|---|---|
| `eventId` | Identificador único do evento (UUID). O consumidor o usa para descartar cópias. |
| `eventType` | Sempre `LaunchRecorded`. |
| `eventVersion` | A versão do formato do evento; hoje, `1`. |
| `occurredAtUtc` | Quando o lançamento foi registrado; igual a `payload.recordedAtUtc`. |
| `correlationId` | O `X-Correlation-Id` da requisição que criou o lançamento. Liga os logs dos dois serviços. |
| `causationId` | O que causou o evento: a `Idempotency-Key` da requisição de registro. |
| `payload.launchId` | O identificador do lançamento. O consumidor também o usa para descartar cópias. |
| `payload.amountInCents`, `payload.nature` | O valor e a natureza do lançamento. |
| `payload.businessDate` | O dia do negócio em cujos totais o lançamento entra. |
| `payload.recordedAtUtc` | O instante do registro, em UTC. |

### Regras do consumidor

1. Validar o envelope e a versão: tipo `LaunchRecorded`, versão `1`, `launchId` preenchido, valor
   positivo, natureza válida e datas preenchidas.
2. Numa só transação local no banco do Consolidado (`Consolidation DB`), gravar o evento na
   Inbox e somar o valor nos totais do dia. Se o `eventId` ou o `launchId` já estiverem na Inbox,
   o evento é uma cópia: desfazer a transação e confirmar a mensagem sem somar de novo. A
   transação, passo a passo, está em
   [`docs/04-schema-logico.md`](04-schema-logico.md#consumo-de-launchrecordedv1).
3. Confirmar a mensagem ao broker — o ACK (acknowledgement, confirmação de recebimento) — só
   depois do commit.
4. Se algo falhar, classificar a falha antes de decidir o destino da mensagem. Uma falha
   temporária de infraestrutura volta das filas de espera enquanto durar e nunca vai para a
   DLQ (Dead Letter Queue, a fila das mensagens que não puderam ser processadas); uma falha
   inesperada tem três novas tentativas e depois vai para a DLQ, com o motivo no cabeçalho
   `x-failure-reason`; uma mensagem inválida vai direto para a DLQ. Os três casos estão em
   [`docs/05-aspectos-transversais.md` §2](05-aspectos-transversais.md#2-resiliência-e-recuperação),
   e o caminho da mensagem, nos diagramas
   [08](diagrams/08-sequence-dlq-replay.md) e [09](diagrams/09-sequence-transient-retry.md).

Uma mudança compatível no evento só acrescenta campos; quem consome ignora os campos que não
conhece. Remover um campo, ou mudar o significado de um campo, exige uma nova versão
(`eventVersion`).

## Códigos de status

| HTTP | Quando | Corpo | Códigos de erro (`code`) |
|---|---|---|---|
| `200` | Consulta bem-sucedida, ou repetição de um registro com a mesma `Idempotency-Key` e o mesmo conteúdo | O recurso | — |
| `201` | Lançamento novo gravado | O lançamento | — |
| `400` | Requisição malformada: JSON inválido, campo desconhecido ou longo demais, cabeçalho ou parâmetro ausente ou inválido | `ApiError` | `invalid_request`, `missing_idempotency_key`, `invalid_correlation_id`, `missing_required_parameter`, `invalid_date_format` |
| `401` | Token ausente ou inválido: assinatura, emissor, `audience` ou validade | Sem corpo | — |
| `403` | Token válido, mas sem a permissão (scope) da operação | Sem corpo | — |
| `404` | Lançamento não encontrado | `ApiError` | `launch_not_found` |
| `409` | Conflito de idempotência | `ApiError` | `idempotency_payload_mismatch`; `concurrent_write_conflict` (conflito de gravação simultânea que não é o da `Idempotency-Key`) |
| `413` | Corpo da requisição maior que 64 KB, recusado pelo Kestrel antes de chegar à aplicação | Sem corpo | — |
| `422` | Requisição bem formada que quebra uma regra de negócio: valor zero ou negativo, natureza inválida, data futura ou intervalo inválido | `ApiError` | `invalid_amount`, `invalid_nature`, `invalid_date_range`, `future_date_not_allowed`, `date_range_too_large` |
| `429` | O chamador passou do limite de requisições por segundo: 100 escritas e 500 leituras por chamador (`sub` do token) | `ApiError` | `rate_limit_exceeded` |
| `500` | Erro inesperado no serviço | `ApiError` | `internal_error` |
| `503` | O banco do Consolidado está fora do ar (consulta do Consolidado). Não é usado para indicar o atraso normal do Consolidado | `ApiError` | `consolidation_unavailable` |

`401` e `403` são as respostas padrão do framework web do .NET (ASP.NET Core) para autenticação e
autorização; `413` é recusado pelo Kestrel antes de qualquer código da aplicação. Por isso, os
três não trazem o envelope `ApiError`.
