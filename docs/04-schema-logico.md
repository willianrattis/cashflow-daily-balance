# Schema lógico e dono de cada dado

Este documento descreve as tabelas de cada banco: o que cada uma guarda, suas colunas e as regras
que o próprio banco garante. O modelo é lógico: não depende do ORM (Object-Relational Mapper, a
biblioteca que converte objetos do código em linhas de tabela; aqui, o Entity Framework Core) nem
das migrações que criam as tabelas. Os tipos são relacionais. Os identificadores são
UUIDs (Universally Unique Identifier: identificadores de 128 bits gerados sem coordenação
central), e os tipos equivalentes no PostgreSQL são `uuid`, `bigint`, `date`, `timestamptz`
(instante com fuso) e `jsonb`.

## Como ler as regras

O banco aplica estas regras a toda gravação, venha da API, de um script ou de alguém conectado
direto a ele. Por isso elas continuam protegendo os dados mesmo quando o código falha.

| Regra | O que garante |
|---|---|
| Chave primária | Identifica cada linha; não pode se repetir nem ficar vazia. |
| `UNIQUE` | O valor da coluna não se repete na tabela. |
| `CHECK` | O valor obedece a uma condição, como `amount_in_cents > 0`. |
| `NOT NULL` | A coluna nunca fica vazia. Nas tabelas abaixo, toda coluna é obrigatória, a não ser as marcadas como "opcional". |
| Trigger | Código que o banco executa automaticamente a cada gravação; aqui, ele recusa alterar ou apagar lançamentos. |
| Índice | Estrutura que acelera a busca por uma coluna. Um índice `UNIQUE` também garante que o valor não se repete. |

## Regras de dono (ownership)

- O banco de Lançamentos (`Cash Flow Entry DB`, `cashflow_entry_db`) pertence só ao `CashFlowEntryService`.
- O banco do Consolidado (`Consolidation DB`, `consolidation_db`) pertence só ao
  `ConsolidationService`.
- Cada serviço se conecta com o próprio usuário de banco (`cashflow_entry_svc`, `consolidation_svc`), que
  não acessa o banco do outro ([`docs/05-aspectos-transversais.md`](05-aspectos-transversais.md)
  §1).
- Não há chave estrangeira (FK, Foreign Key) entre os bancos, nem transação que envolva os dois.
- Lançamentos só recebem linhas novas: não há `UPDATE` de valor ou de natureza, nem `DELETE` —
  nem mesmo um "apagado" lógico que esconda o fato financeiro.

## Banco de Lançamentos (`Cash Flow Entry DB`) — fonte da verdade

### Tabela `cash_flow_entries`

Guarda cada lançamento registrado: o fato financeiro oficial. Uma linha nunca muda depois de
gravada.

| Coluna | Tipo lógico | Regra |
|---|---|---|
| `entry_id` | UUID | Chave primária. Identificador do lançamento, gerado pelo servidor. |
| `idempotency_key` | texto, até 200 caracteres | `UNIQUE`. A chave que o cliente envia no cabeçalho `Idempotency-Key`. |
| `request_fingerprint` | texto, 64 caracteres | Resumo SHA-256 de valor, natureza e descrição, usado para saber se uma repetição traz o mesmo conteúdo. |
| `amount_in_cents` | inteiro | `CHECK amount_in_cents > 0`. |
| `nature` | texto | `CHECK nature IN ('Credit', 'Debit')`. |
| `business_date` | data | O dia do negócio, definido pelo servidor no fuso `America/Sao_Paulo`. |
| `recorded_at_utc` | instante | Definido pelo servidor logo antes do commit da transação (A-019, premissa sobre o instante do registro, em [`docs/01`](01-requisitos-e-assumptions.md)). |
| `description` | texto, até 1000 caracteres, opcional | Texto informativo; não entra em nenhum cálculo de saldo. |
| `principal_id` | texto, até 200 caracteres | Quem registrou: o `sub` do token de acesso. |
| `correlation_id` | texto, até 100 caracteres | O `X-Correlation-Id` da requisição, para seguir a operação nos logs. |

O que cada regra desta tabela impede:

- **Chave primária `entry_id`.** Dois lançamentos nunca têm o mesmo identificador.
- **`UNIQUE (idempotency_key)`**, no índice `ux_cash_flow_entries_idempotency_key`. Impede que uma
  requisição repetida grave um segundo lançamento, mesmo quando as duas cópias chegam ao mesmo
  tempo. Exemplo: dois `POST` com a chave `venda-0001` chegam no mesmo milissegundo; o banco grava
  o primeiro e recusa o segundo com o erro `23505` (violação de unicidade); o serviço então lê o
  primeiro e responde como a uma repetição ([`docs/03`](03-contratos.md#comportamento-de-idempotência-e-conflitos)).
- **`CHECK amount_in_cents > 0` e `CHECK nature IN ('Credit', 'Debit')`.** Impedem um valor zero
  ou negativo e uma natureza desconhecida, mesmo que um defeito na API ou um script pule a
  validação. Exemplo: um `INSERT` com `amount_in_cents = -500` ou com `nature = 'Refund'` falha
  no banco.
- **Colunas obrigatórias (`NOT NULL`).** Um lançamento sem data do negócio, sem autor ou sem
  fingerprint não pode ser gravado.
- **Trigger `trg_cash_flow_entries_append_only`.** Antes de qualquer `UPDATE` ou `DELETE` em `cash_flow_entries`, o
  banco executa uma função que cancela a operação com o erro
  `cash_flow_entries is append-only: UPDATE is not allowed (INV-001)`. Isso garante a regra INV-001 (um
  lançamento confirmado nunca é alterado nem apagado) até contra alguém conectado direto ao banco;
  o teste `DatabaseIntegrityTests` prova. O trigger não bloqueia `TRUNCATE`, porque o PostgreSQL
  não dispara triggers de linha para esse comando; nada nesta entrega executa `TRUNCATE` em
  `cash_flow_entries`. Risco que resta: o usuário do serviço é dono da tabela e poderia, em tese, remover o
  trigger (A-024, premissa registrada em `docs/01`).
- **Índice `(business_date, recorded_at_utc)`**, chamado `ix_cash_flow_entries_business_date`. Acelera a
  busca dos lançamentos de uma data, por exemplo numa auditoria.

O fingerprint fica gravado junto com o lançamento para que uma repetição possa ser comparada
mesmo depois de o serviço reiniciar.

### Tabela `outbox_messages`

Guarda a mensagem a publicar de cada lançamento, gravada na mesma transação do lançamento. O
publisher (a tarefa de `CashFlowEntryService` que roda em segundo plano) lê as mensagens pendentes, envia
ao RabbitMQ e as marca como publicadas. As mensagens publicadas continuam guardadas, porque são a
fonte da reconstrução do Consolidado (A-012).

| Coluna | Tipo lógico | Regra |
|---|---|---|
| `outbox_id` | UUID | Chave primária. |
| `aggregate_id` | UUID | O `entry_id` do lançamento que gerou a mensagem. |
| `event_id` | UUID | `UNIQUE`. O identificador do evento, o mesmo em todas as tentativas de publicação. |
| `event_type` | texto | `CashFlowEntryRecorded`. |
| `event_version` | inteiro | A versão do formato do evento; hoje, `1`. |
| `payload` | JSON | O evento completo, exatamente como será publicado. |
| `occurred_at_utc` | instante | Quando o lançamento foi registrado. |
| `status` | texto | `Pending` (a publicar), `Published` (publicada e confirmada pelo RabbitMQ) ou `Failed` (a última tentativa falhou e haverá outra). |
| `attempt_count` | inteiro | Quantas tentativas de publicação já falharam. |
| `next_attempt_at_utc` | instante, opcional | Quando tentar de novo. Cada falha afasta esse instante: 2 s, 4 s, 8 s… até 5 minutos, mais até 1 s aleatório. |
| `published_at_utc` | instante, opcional | Quando o RabbitMQ confirmou a publicação. |
| `last_error` | texto, até 2000 caracteres, opcional | O erro da última tentativa, para diagnóstico. |
| `created_at_utc` | instante | Quando a linha foi criada; usado para retenção e auditoria. |

O que cada regra desta tabela impede:

- **`UNIQUE (event_id)`.** Dois registros do Outbox nunca carregam o mesmo evento.
- **`UNIQUE (aggregate_id, event_type, event_version)`.** Um lançamento gera um único evento
  `CashFlowEntryRecorded` versão 1. Exemplo: se um defeito tentasse gravar uma segunda mensagem para o
  lançamento `9f2c…`, o banco recusaria, e o Consolidado nunca receberia o mesmo lançamento em dois
  eventos diferentes.
- **Índice parcial em `(status, next_attempt_at_utc)`**, só para as mensagens ainda não
  publicadas. Acelera a busca do publisher pelo próximo lote, sem percorrer o histórico já
  publicado.
- `cash_flow_entries` e `outbox_messages` são gravadas na mesma transação local: ou as duas linhas
  existem, ou nenhuma.
- O publisher só altera os campos de controle da publicação (`status`, tentativas, datas e erro),
  nunca o lançamento.

## Banco do Consolidado (`Consolidation DB`) — cópia derivada

### Tabela `inbox_messages`

Registra cada evento já aplicado aos totais. Serve para que um evento entregue de novo pelo
RabbitMQ (reentrega) seja descartado em vez de somado duas vezes.

| Coluna | Tipo lógico | Regra |
|---|---|---|
| `event_id` | UUID | Chave primária: a primeira proteção contra a reentrega do mesmo evento. |
| `entry_id` | UUID | `UNIQUE`: a segunda proteção, pelo lançamento. |
| `event_type` | texto | O tipo recebido (`CashFlowEntryRecorded`). |
| `event_version` | inteiro | A versão validada (`1`). |
| `received_at_utc` | instante | Quando o consumidor começou a aplicar o evento. |
| `processed_at_utc` | instante, opcional | Quando o evento foi aplicado. Hoje é gravado com o mesmo valor de `received_at_utc`, na mesma transação que soma os totais. |
| `status` | texto | Sempre `Processed`. Um evento inválido nunca gera linha aqui: vai direto para a DLQ (Dead Letter Queue, fila das mensagens que não puderam ser processadas), sem passar pela Inbox. |

O que cada regra desta tabela impede:

- **Chave primária `event_id`.** Impede aplicar o mesmo evento duas vezes. Exemplo: o consumidor
  soma R$ 100,00, o banco confirma a transação e o consumidor cai antes de mandar ao RabbitMQ o
  ACK (acknowledgement, a confirmação de recebimento). O RabbitMQ entrega o evento de novo; a
  gravação na Inbox falha por chave repetida, a transação é desfeita, nada é somado, e a mensagem
  é confirmada e descartada.
- **`UNIQUE (entry_id)`**, no índice `ux_inbox_entry_id`. Impede somar o mesmo lançamento duas
  vezes mesmo que ele chegue em dois eventos com `eventId` diferentes, por exemplo por um defeito
  de quem publica.

### Tabela `daily_consolidations`

Guarda os totais de cada dia — uma linha por data do negócio, com a soma dos créditos, a soma dos
débitos e a variação. É atualizada a cada evento aplicado, e é o que a consulta do Consolidado lê.

| Coluna | Tipo lógico | Regra |
|---|---|---|
| `business_date` | data | Chave primária: um dia, uma linha. |
| `credits_in_cents` | inteiro, nunca negativo | Soma dos créditos do dia. |
| `debits_in_cents` | inteiro, nunca negativo | Soma dos débitos do dia. |
| `net_change_in_cents` | inteiro, pode ser negativo | Variação do dia: créditos menos débitos. |
| `last_event_occurred_at_utc` | instante | O maior `recordedAtUtc` entre os eventos já somados neste dia. |
| `projection_version` | inteiro | Quantos eventos já foram somados neste dia. |
| `updated_at_utc` | instante | Quando a linha mudou pela última vez. |

O que cada regra desta tabela impede:

- **Chave primária `business_date`.** Existe um único total por dia. A linha nasce com o primeiro
  evento do dia; dias sem movimento não têm linha, e a consulta os preenche com zeros.
- **`CHECK credits_in_cents >= 0` e `CHECK debits_in_cents >= 0`.** Somas de valores positivos
  nunca ficam negativas; se ficarem, há um defeito, e o banco recusa a gravação.
- **`CHECK net_change_in_cents = credits_in_cents - debits_in_cents`.** A variação sempre bate com
  os dois totais. Exemplo: uma gravação que aumentasse `credits_in_cents` em 10000 sem ajustar
  `net_change_in_cents` seria recusada.

Como a consulta usa esta tabela: o saldo de abertura (`openingBalanceInCents`) é a soma de
`net_change_in_cents` de todos os dias antes de `fromDate`; a variação do intervalo
(`netChangeInCents`) é a soma dentro do intervalo; e o saldo de fechamento
(`closingBalanceInCents`) é a soma dos dois. Essa escolha mantém a tabela simples e faz a
reconstrução chegar sempre ao mesmo resultado. Se o histórico crescer a ponto de a consulta passar
da meta de tempo de resposta, dá para acrescentar saldos gravados em datas fixas (snapshots,
fotografias do saldo num momento, como no fim de cada mês): a abertura somaria só os dias depois
da última fotografia, sem mudar o contrato da API.

### Tabela `projection_metadata`

Guarda os dados do último evento aplicado: `last_applied_event_recorded_at_utc` (quando o
lançamento foi registrado), `last_applied_at_utc` (quando o evento foi aplicado) e
`projection_version` (quantos eventos já foram aplicados desde a última reconstrução). Esses
valores alimentam os campos `lastAppliedEventRecordedAtUtc`, `asOfUtc` e `projectionVersion` da
consulta ([`docs/03`](03-contratos.md#consultar-consolidado-por-intervalo)).

A tabela tem sempre uma única linha, porque não há nada a separar por comerciante nem por
consumidor: a coluna `id` é a chave primária, e um `CHECK (id = 1)` impede uma segunda linha. É
dado de operação, não dado financeiro, e nunca deve ser usado para recriar lançamentos.

## Transações críticas

### Registro de lançamento

Tudo acontece numa só transação no banco de Lançamentos, e nada é enviado ao broker nem ao
Consolidado antes do commit:

```text
validar o cabeçalho Idempotency-Key, os tamanhos, o valor e a natureza
procurar a Idempotency-Key: se existir, responder 200 (mesmo conteúdo) ou 409, sem gravar
BEGIN transação no Cash Flow Entry DB
  INSERT cash_flow_entries (business_date e recorded_at_utc definidos pelo servidor)
  INSERT outbox_messages
COMMIT                     (se a UNIQUE recusar a chave: ler o lançamento gravado e responder como repetição)
responder 201
```

### Consumo de `CashFlowEntryRecorded.v1`

Tudo acontece numa só transação no banco do Consolidado, e o ACK só sai depois do commit:

```text
BEGIN transação no Consolidation DB
  INSERT inbox_messages (já como Processed)
  se a chave primária ou a UNIQUE recusar: ROLLBACK -> é uma cópia; ACK sem somar nada
  somar o valor no dia      (INSERT ... ON CONFLICT DO UPDATE em daily_consolidations)
  registrar o último evento (INSERT ... ON CONFLICT DO UPDATE em projection_metadata)
COMMIT
ACK da mensagem no broker
```

Se o commit falhar, a transação inteira é desfeita e nada é somado. A mensagem volta para uma
nova tentativa — por uma fila de espera, ou entregue de novo pelo RabbitMQ se o consumidor cair
antes do ACK —, e essa tentativa começa do zero, com segurança.

## Rebuild da projeção

O FR-007 e o NFR-009 (requisitos: o Consolidado pode ser reconstruído a partir dos lançamentos)
estão implementados como um script de operação, `scripts/ops/rebuild-consolidation.sh`, que um
operador executa à mão, em caso excepcional. O passo a passo está em
[`docs/07-runbooks.md` §5](07-runbooks.md#5-totais-divergentes-conferir-reenviar-a-dlq-reconstruir).
Do ponto de vista dos dados:

- a tabela `cash_flow_entries` nunca é alterada; só as tabelas do Consolidado são apagadas
  (`inbox_messages`, `daily_consolidations`, `projection_metadata`), e as mensagens do Outbox
  voltam a `Pending`, para o publisher de `CashFlowEntryService` publicá-las de novo;
- a fonte é o Outbox, e não a tabela `cash_flow_entries`, porque o Outbox já guarda cada evento
  `CashFlowEntryRecorded.v1` completo, no formato do contrato. Por isso as mensagens publicadas precisam
  ficar guardadas (A-012, em `docs/01-requisitos-e-assumptions.md`);
- o `ConsolidationService` aplica os eventos pelo mesmo caminho do consumo normal, com a Inbox
  descartando cópias — não há lógica especial de reconstrução no serviço;
- a conferência final (`scripts/ops/reconcile.sh`) compara os totais direto com `cash_flow_entries`, e não
  com o Outbox; assim, usar o Outbox como fonte não compromete a conferência.

Não há uma segunda versão dos totais, montada em paralelo para depois ser trocada: a reconstrução
acontece na própria tabela. O broker também não precisa guardar mensagens para sempre: a fonte da
reconstrução é o Outbox, não o broker.
