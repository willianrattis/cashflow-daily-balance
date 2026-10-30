# Roteiro de testes

Este roteiro mostra, comando a comando, como verificar à mão cada garantia do desafio contra o
ambiente real, que sobe com `docker compose`. Não usa os containers temporários dos testes
automatizados (Testcontainers): tudo roda contra os mesmos serviços que o avaliador vê.

- **Pré-requisitos e portas:** [`README.md`](../README.md#3-pré-requisitos).
- **Tokens de acesso:** os comandos usam as variáveis `WRITE_TOKEN` e `READ_TOKEN`, criadas no
  cenário 2 pelo `scripts/dev-jwt/make_token.py`.
- **Ordem:** os cenários dependem uns dos outros. Rode-os em ordem, a partir de um ambiente vazio
  (`docker compose down -v`).
- **Códigos:** cada cenário cita o requisito que verifica. O texto de cada código está em
  [`docs/09-rastreabilidade.md`](09-rastreabilidade.md) (os do PDF) e em
  [`docs/01-requisitos-e-assumptions.md`](01-requisitos-e-assumptions.md) (os nossos).

Cada cenário traz: objetivo, requisito, pré-condição, comandos, resultado esperado, como
verificar, o teste automatizado que prova o mesmo e como voltar ao estado inicial.

## 1. Subir o ambiente e checar saúde
- **Objetivo:** mostrar que o ambiente inteiro — os dois serviços, o PostgreSQL, o RabbitMQ, o
  Prometheus e o Grafana — sobe do zero, sem nenhum dado, e que os dois serviços respondem às
  verificações de saúde.
- **Requisito:** MR-07 (requisito obrigatório do PDF: README com instruções claras para rodar
  localmente) e NFR-008 (qualquer pessoa consegue reproduzir a execução).
- **Pré-condição:** `docker compose down -v`.
- **Comandos:**
  ```bash
  docker compose up -d --build --wait   # --wait espera os 6 containers subirem e os 4 com verificação de saúde ficarem healthy
  curl -s http://localhost:5101/health/live; curl -s http://localhost:5101/health/ready; echo
  curl -s http://localhost:5102/health/live; curl -s http://localhost:5102/health/ready; echo
  ```
- **Resultado esperado:** `--wait` termina sem estourar o tempo; cada chamada responde `200
  Healthy`; `http://localhost:5101/swagger` e `.../5102/swagger` abrem no navegador.
- **Como verificar:** `docker compose ps` mostra os 6 serviços `healthy` ou `running`.
- **Teste automatizado:** `.github/workflows/ci.yml` (`docker compose up -d --wait`).
- **Como voltar ao estado inicial:** deixe o ambiente no ar para os próximos cenários.

## 2. Gerar token; chamada sem token e com scope errado
- **Objetivo:** mostrar que nenhuma operação de negócio responde sem um token de acesso válido —
  um JWT (JSON Web Token, o token assinado que diz quem chama) — e sem a permissão (scope) certa
  para aquela operação.
- **Requisito:** OBJ-05 (critério de avaliação do PDF: segurança, com autenticação e autorização)
  e NFR-007 (as APIs autenticam e autorizam pessoas e sistemas).
- **Pré-condição:** cenário 1 concluído.
- **Comandos:**
  ```bash
  WRITE_TOKEN=$(python3 scripts/dev-jwt/make_token.py --scope cashflow.entries.write)
  READ_TOKEN=$(python3 scripts/dev-jwt/make_token.py --scope cashflow.consolidation.read)
  curl -i http://localhost:5101/api/v1/entries/00000000-0000-0000-0000-000000000000          # sem token
  curl -i -H "Authorization: Bearer $READ_TOKEN" http://localhost:5101/api/v1/entries/00000000-0000-0000-0000-000000000000  # scope errado
  ```
- **Resultado esperado:** a primeira chamada recebe `401`, sem corpo; a segunda, `403`, sem corpo.
- **Como verificar:** `curl -i` mostra a linha de status; nenhum corpo de erro é devolvido.
- **Teste automatizado:** `CashFlowEntryService.IntegrationTests/JwtAuthenticationTests.cs`
  (`Anonymous_request_returns_401`, `Missing_scope_returns_403`) e o par equivalente em
  `ConsolidationService.IntegrationTests`.
- **Como voltar ao estado inicial:** nada foi gravado.

## 3. Registrar um lançamento e consultá-lo
- **Objetivo:** mostrar o caso básico: registrar um crédito de R$ 100,00 e lê-lo de volta pelo
  identificador (`entryId`), com a data de negócio definida pelo servidor.
- **Requisito:** BR-01 (requisito de negócio do PDF: "Serviço que faça o controle de
  lançamentos"), FR-001 (registrar um lançamento) e FR-002 (crédito ou débito).
- **Pré-condição:** `WRITE_TOKEN` do cenário 2.
- **Comandos:**
  ```bash
  ENTRY_READ_TOKEN=$(python3 scripts/dev-jwt/make_token.py --scope cashflow.entries.read)
  CREDIT=$(curl -s -X POST http://localhost:5101/api/v1/entries \
    -H "Authorization: Bearer $WRITE_TOKEN" -H "Idempotency-Key: roteiro-credit-1" -H "Content-Type: application/json" \
    -d '{"amountInCents":10000,"nature":"Credit","description":"Venda 1"}')
  ENTRY_ID=$(python3 -c "import json,sys; print(json.loads(sys.argv[1])['entryId'])" "$CREDIT")
  curl -s http://localhost:5101/api/v1/entries/$ENTRY_ID -H "Authorization: Bearer $ENTRY_READ_TOKEN"
  ```
- **Resultado esperado:** o `POST` responde `201` com `entryId`, `businessDate` (hoje, no fuso
  `America/Sao_Paulo`) e `status: "Recorded"`. O `GET` — com a permissão de leitura
  `cashflow.entries.read`, diferente da de escrita usada no `POST` — devolve o mesmo registro.
- **Como verificar:** compare os campos do corpo do `POST` e do `GET`.
- **Teste automatizado:** `RecordEntryTests.cs`
  (`Registering_a_entry_returns_201_and_persists_outbox_without_publishing`) e `GetEntryTests.cs`
  (`Getting_an_existing_entry_returns_its_immutable_record`), em `CashFlowEntryService.IntegrationTests`.
- **Como voltar ao estado inicial:** não há como: um lançamento nunca é alterado nem apagado, por
  desenho.

## 4. Idempotência
- **Objetivo:** mostrar que repetir a mesma requisição — mesma `Idempotency-Key`, mesmo conteúdo,
  como faria um cliente depois de um timeout — devolve o lançamento original sem criar outro, e
  que reusar a chave com outro conteúdo é recusado.
- **Requisito:** FR-004 (não criar um lançamento duplicado quando o cliente repete a requisição);
  critérios de aceitação em `docs/09`.
- **Pré-condição:** cenário 3 (a chave `roteiro-credit-1` já foi usada).
- **Comandos:**
  ```bash
  curl -i -X POST http://localhost:5101/api/v1/entries \
    -H "Authorization: Bearer $WRITE_TOKEN" -H "Idempotency-Key: roteiro-credit-1" -H "Content-Type: application/json" \
    -d '{"amountInCents":10000,"nature":"Credit","description":"Venda 1"}'                 # mesma key, mesmo payload
  curl -i -X POST http://localhost:5101/api/v1/entries \
    -H "Authorization: Bearer $WRITE_TOKEN" -H "Idempotency-Key: roteiro-credit-1" -H "Content-Type: application/json" \
    -d '{"amountInCents":99999,"nature":"Credit","description":"Venda 1"}'                 # mesma key, payload diferente
  ```
- **Resultado esperado:** a primeira responde `200`, com o cabeçalho `Idempotency-Replayed: true`
  e o mesmo `entryId`; a segunda, `409`, com `code: "idempotency_payload_mismatch"`.
- **Como verificar:** o cabeçalho da primeira resposta; o campo `code` do corpo da segunda.
- **Teste automatizado:** `RecordEntryTests.cs`
  (`Retry_with_same_key_and_same_payload_replays_without_a_second_movement`,
  `Reusing_the_same_key_with_a_different_payload_returns_409`).
- **Como voltar ao estado inicial:** nada novo foi gravado.

## 5. Validações
- **Objetivo:** mostrar que um pedido inválido — valor zero, natureza desconhecida, sem
  `Idempotency-Key` ou maior que 64 KB (kilobytes) — é recusado e nunca é gravado.
- **Requisito:** INV-002 (o valor é um inteiro positivo em centavos, e a natureza é `Credit` ou
  `Debit`) e o contrato da API ([`docs/03-contratos.md`](03-contratos.md)).
- **Pré-condição:** `WRITE_TOKEN` do cenário 2.
- **Comandos:**
  ```bash
  curl -i -X POST http://localhost:5101/api/v1/entries -H "Authorization: Bearer $WRITE_TOKEN" \
    -H "Idempotency-Key: roteiro-invalid-1" -H "Content-Type: application/json" -d '{"amountInCents":0,"nature":"Credit"}'
  curl -i -X POST http://localhost:5101/api/v1/entries -H "Authorization: Bearer $WRITE_TOKEN" \
    -H "Idempotency-Key: roteiro-invalid-2" -H "Content-Type: application/json" -d '{"amountInCents":100,"nature":"Invalid"}'
  curl -i -X POST http://localhost:5101/api/v1/entries -H "Authorization: Bearer $WRITE_TOKEN" \
    -H "Content-Type: application/json" -d '{"amountInCents":100,"nature":"Credit"}'          # sem Idempotency-Key
  curl -i -X POST http://localhost:5101/api/v1/entries -H "Authorization: Bearer $WRITE_TOKEN" \
    -H "Idempotency-Key: roteiro-invalid-3" -H "Content-Type: application/json" \
    -d "{\"amountInCents\":100,\"nature\":\"Credit\",\"description\":\"$(python3 -c "print('e'*70000)")\"}"  # corpo > 64 KB
  ```
- **Resultado esperado:** valor zero e natureza inválida → `422`; sem `Idempotency-Key` → `400`;
  corpo acima de 64 KB → `413`, sem corpo.
- **Como verificar:** o campo `code` de cada resposta `400` e `422`; a falta de corpo no `413`.
- **Teste automatizado:** `RecordEntryTests.cs` (`Non_positive_amount_returns_422`,
  `Invalid_nature_returns_422`, `Missing_idempotency_key_returns_400`) e `InputValidationTests.cs`
  (`Oversized_description_returns_400_invalid_request`). O `413` só acontece com o servidor web
  real do .NET (Kestrel), e por isso é provado pelo `scripts/e2e/smoke.sh`
  (`oversized_body_returns_413`).
- **Como voltar ao estado inicial:** nada foi gravado: todas as chamadas foram recusadas.

## 6. Consolidado
- **Objetivo:** mostrar que a consulta do saldo diário soma corretamente o que foi registrado e
  aplica as regras de intervalo: período sem movimento, data futura e o limite de 366 dias.
- **Requisito:** BR-02 (requisito de negócio do PDF: o relatório do saldo diário consolidado),
  FR-003 (consultar por intervalo de datas), FR-006 (período sem movimento) e A-015 (premissa: no
  máximo 366 dias por consulta).
- **Pré-condição:** `READ_TOKEN` do cenário 2; ao menos um lançamento do cenário 3.
- **Comandos:**
  ```bash
  TODAY=$(TZ=America/Sao_Paulo date +%F)
  sleep 2   # dá tempo do consumer aplicar o evento do cenário 3 antes da primeira consulta
  curl -s "http://localhost:5102/api/v1/consolidation?fromDate=$TODAY&toDate=$TODAY" -H "Authorization: Bearer $READ_TOKEN"
  curl -s "http://localhost:5102/api/v1/consolidation?fromDate=2020-01-01&toDate=2020-01-02" -H "Authorization: Bearer $READ_TOKEN"  # período vazio, no passado
  curl -i "http://localhost:5102/api/v1/consolidation?fromDate=$TODAY&toDate=2099-01-01" -H "Authorization: Bearer $READ_TOKEN"      # data futura
  curl -i "http://localhost:5102/api/v1/consolidation?fromDate=2020-01-01&toDate=2021-06-01" -H "Authorization: Bearer $READ_TOKEN"  # > 366 dias
  ```
- **Resultado esperado:** a primeira responde `200`, com `days[]` cobrindo cada dia do intervalo e
  os campos de atualidade (`asOfUtc`, `lastAppliedEventRecordedAtUtc`, `lastAppliedLagSeconds`)
  preenchidos; o período vazio responde `200` com totais zerados; a data futura e o intervalo
  acima de 366 dias respondem `422`.
- **Como verificar:** o `closingBalanceInCents` da resposta é igual ao `closingBalanceInCents` do
  último item de `days[]`.
- **Teste automatizado:** `GetConsolidationTests.cs`
  (`Query_computes_opening_credits_debits_net_and_closing_for_the_window`,
  `Query_returns_daily_breakdown_with_running_balance_across_a_gap_day`,
  `Query_for_a_period_with_no_movement_returns_zeros_without_shifting_the_filter`,
  `Query_with_a_future_toDate_returns_422`,
  `Query_with_a_date_range_longer_than_366_days_returns_422`), em
  `ConsolidationService.IntegrationTests`.
- **Como voltar ao estado inicial:** nada foi gravado: todas as chamadas são de leitura.

## 7. Requisito de disponibilidade (NFR-A)
- **Objetivo:** mostrar o requisito central do PDF: com o Consolidado parado, o registro de
  lançamentos continua funcionando, e os eventos esperam na fila do RabbitMQ até ele voltar.
- **Requisito:** NFR-A (requisito do PDF: "O serviço de controle de lançamento não deve ficar
  indisponível se o sistema de consolidado diário cair") e NFR-001, que o detalha.
- **Pré-condição:** ambiente no ar; `WRITE_TOKEN` do cenário 2.
- **Comandos:**
  ```bash
  docker compose stop consolidationservice
  curl -i -X POST http://localhost:5101/api/v1/entries -H "Authorization: Bearer $WRITE_TOKEN" \
    -H "Idempotency-Key: roteiro-outage-1" -H "Content-Type: application/json" -d '{"amountInCents":500,"nature":"Credit"}'
  sleep 1 && docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages
  docker compose start consolidationservice
  sleep 5 && ./scripts/ops/reconcile.sh
  ```
- **Resultado esperado:** o `POST` responde `201` com o Consolidado parado; o `reconcile.sh` sai
  com código `0` depois que o serviço volta e processa as mensagens acumuladas.
- **Como verificar:** o `list_queues`, rodado antes de religar, mostra a fila
  `consolidation.cashflow-entry-recorded.v1` com mensagens acumuladas enquanto o Consolidado está parado.
- **Teste automatizado:** `scripts/e2e/smoke.sh` (`nfr001_consolidation_down`); além disso, toda a
  suíte `CashFlowEntryService.IntegrationTests` roda com o broker inacessível, de propósito.
- **Como voltar ao estado inicial:** os próprios comandos religam o serviço.

## 8. Broker fora do ar
- **Objetivo:** mostrar que o registro continua funcionando com o RabbitMQ fora do ar: o evento
  fica guardado no Outbox — a tabela, no banco de Lançamentos, das mensagens ainda não publicadas
  — e é publicado quando o broker volta.
- **Requisito:** NFR-A, com a decisão de integração assíncrona (ADR-002), e A-009 (premissa: o
  lançamento e a mensagem do Outbox são gravados na mesma transação).
- **Pré-condição:** ambiente no ar; `WRITE_TOKEN` do cenário 2.
- **Comandos:**
  ```bash
  docker compose stop rabbitmq
  curl -s -X POST http://localhost:5101/api/v1/entries -H "Authorization: Bearer $WRITE_TOKEN" \
    -H "Idempotency-Key: roteiro-broker-1" -H "Content-Type: application/json" -d '{"amountInCents":100,"nature":"Credit"}'
  sleep 15   # dá tempo do Outbox Publisher tentar publicar e falhar pelo menos uma vez
  docker compose exec -T postgres psql -U cashflow -d cashflow_entry_db -c \
    "SELECT status, attempt_count FROM outbox_messages ORDER BY created_at_utc DESC LIMIT 1;"
  docker compose start rabbitmq
  sleep 20 && ./scripts/ops/reconcile.sh   # repita se ainda FAIL: o consumo pode não ter terminado
  ```
- **Resultado esperado:** o `POST` responde `201`. Enquanto o broker está fora, a linha do Outbox
  fica `Failed`, pronta para nova tentativa, com `attempt_count >= 1`. Depois que o broker volta e
  as mensagens acumuladas são processadas, o `reconcile.sh` sai com código `0`.
- **Como verificar:** a consulta SQL acima, antes de religar o broker.
- **Teste automatizado:** `scripts/e2e/smoke.sh` (`broker_down`).
- **Como voltar ao estado inicial:** os próprios comandos religam o broker.

## 9. Mensagem inválida vai para a DLQ (Dead Letter Queue)
- **Objetivo:** mostrar que um evento malformado vai para a DLQ, a fila das mensagens que não
  puderam ser processadas, sem derrubar o consumidor do Consolidado e sem
  afetar nenhum lançamento.
- **Requisito:** a decisão de integração assíncrona (ADR-002) e o alerta `DeadLetterQueueNotEmpty`
  (`observability/prometheus/alerts.yml`).
- **Pré-condição:** ambiente no ar.
- **Comandos:**
  ```bash
  curl -s -u cashflow:cashflow-dev-only -X POST http://localhost:15672/api/exchanges/%2F/cashflow.events/publish \
    -H "Content-Type: application/json" \
    -d '{"properties":{},"routing_key":"cashflow.entry.recorded.v1","payload":"not-json","payload_encoding":"string"}'
  sleep 2 && docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages | grep dlq
  ./scripts/ops/replay-dlq.sh
  ```
- **Resultado esperado:** a fila `consolidation.cashflow-entry-recorded.v1.dlq` ganha 1 mensagem. O
  `replay-dlq.sh` a devolve à fila principal, onde ela é recusada de novo: é uma mensagem
  permanentemente inválida, e voltar à DLQ é o esperado, e não um defeito do reenvio.
- **Como verificar:** a quantidade de mensagens na DLQ antes e depois do reenvio.
- **Teste automatizado:** `ConsumerTests.cs`
  (`An_invalid_event_reaches_the_dead_letter_queue_without_crashing_the_consumer`, em
  `ConsolidationService.IntegrationTests`); `scripts/e2e/smoke.sh` (`dlq_rejected_message`, que
  apaga a mensagem que cria quando a DLQ estava vazia antes do passo; se a DLQ já tinha
  mensagens, ele deixa a nova no lugar e só avisa, para não apagar uma mensagem que não é dele).
- **Como voltar ao estado inicial:** apague a mensagem reenviada:
  `curl -s -u cashflow:cashflow-dev-only -X POST http://localhost:15672/api/queues/%2F/consolidation.cashflow-entry-recorded.v1.dlq/get -d '{"count":1,"ackmode":"ack_requeue_false","encoding":"auto"}'`.

## 10. Durabilidade e recuperação
- **Objetivo:** mostrar que as mensagens já publicadas sobrevivem a um reinício completo do
  ambiente, e que os totais do Consolidado podem ser apagados e recalculados do zero a partir dos
  lançamentos.
- **Requisito:** NFR-009 (os totais podem ser recuperados se forem perdidos) e FR-007 (reconstruir
  o Consolidado do zero); OBJ-04 em `docs/09` (critério de avaliação do PDF: resiliência e
  recuperação).
- **Pré-condição:** ambiente no ar, de preferência recém-criado (`down -v && up`), para números
  previsíveis.
- **Comandos:**
  ```bash
  ./scripts/e2e/smoke.sh --durability   # para consolidationservice, registra, docker compose down/up, confirma
  ./scripts/e2e/smoke.sh --recovery     # reconcile -> rebuild-consolidation.sh --yes -> reconcile
  ```
- **Resultado esperado:** todas as linhas `PASS`; o `rebuild-consolidation.sh` termina com o
  `reconcile.sh` saindo com código `0`.
- **Como verificar:** a saída do próprio script (tabela `STEP`/`RESULT`/`ELAPSED`).
- **Teste automatizado:** os dois modos do script são a prova; não há teste xUnit equivalente,
  porque seria preciso derrubar o ambiente inteiro.
- **Como voltar ao estado inicial:** o script religa os serviços que parou (`trap` no `EXIT`).

## 11. Carga 50 requisições por segundo
- **Objetivo:** mostrar que o sistema aguenta o pico que o PDF descreve — 50 requisições por
  segundo — com no máximo 5% de requisições com falha.
- **Requisito:** NFR-B (requisito do PDF: 50 requisições por segundo no pico), NFR-C (requisito
  do PDF: no máximo 5% de perda de requisições) e A-013 (premissa: o pico de escrita é de 50
  lançamentos por segundo durante 15 minutos).
- **Pré-condição:** ambiente no ar, de preferência sem outra carga ao mesmo tempo.
- **Comandos:**
  ```bash
  scripts/load/run.sh all   # consolidation + entries, 2 minutos cada
  ```
- **Resultado esperado:** `http_req_failed < 5%` no cenário de consulta (NFR-C; o cenário de
  lançamentos não tem esse limite no k6), `dropped_iterations == 0` e p95 e p99
  — os tempos abaixo dos quais ficam 95% e 99% das respostas — dentro dos limites do cenário (veja
  o relatório).
- **Como ler o resultado contra o "máximo 5% de perda":** `http_req_failed` é a proporção de
  requisições que falharam — timeout, erro `5xx` ou conexão recusada. É assim que lemos o NFR-C
  (premissa A-004): nenhum lançamento se perde, por desenho (o Outbox e a Inbox); só a requisição
  pode falhar para o cliente.
- **Como verificar:** os relatórios `docs/evidence/load/<data>-{consolidation,entries}.md`,
  gerados pelo script. Os registrados no repositório são
  [2026-10-19-consolidation.md](evidence/load/2026-10-19-consolidation.md) (0% de falha),
  [2026-10-19-launches.md](evidence/load/2026-10-19-launches.md) e
  [2026-10-20-launches.md](evidence/load/2026-10-20-launches.md), o pico de 15 minutos de
  lançamentos (`scripts/load/run.sh entries 15m`).
- **Teste automatizado:** `tests/load/consolidation-50rps.js`, `tests/load/entries-50rps.js`,
  rodados pelo k6 (ferramenta de teste de carga) dentro de um container.
- **Como voltar ao estado inicial:** o próprio script faz isso. O teste de consulta grava um
  histórico sintético direto nos totais do Consolidado, para consultar uma janela de datas
  realista; no fim, o script recalcula os totais a partir do Outbox, o que apaga esse histórico, e
  roda o `reconcile.sh`.

## 12. Observabilidade
- **Objetivo:** mostrar onde encontrar as métricas, o painel, os alertas e o identificador de
  correlação (`correlationId`), que liga todas as linhas de log de uma mesma operação.
- **Requisito:** DF-03 (diferencial do PDF: "Monitoramento e Observabilidade") e OBJ-08 (critério
  de avaliação do PDF: métricas e metas claras).
- **Pré-condição:** ambiente no ar, com algum tráfego gerado pelos cenários anteriores.
- **Comandos:** abra no navegador `http://localhost:3000/dashboards/f/cashflow/` (painel
  "CashFlow — visão operacional") e `http://localhost:9090/alerts` (9 regras, pendentes ou
  disparadas); depois:
  ```bash
  docker compose logs cashflowentryservice --tail=50 | grep -o '"correlationId":"[^"]*"' | head -5
  ```
- **Resultado esperado:** o painel com as 6 áreas (CashFlowEntryService, Outbox, Consumer, Broker,
  ConsolidationService API, Availability); o `correlationId` presente nas linhas de log JSON de
  cada requisição e do publisher do Outbox.
- **Como verificar:** [`docs/05-aspectos-transversais.md` §3](05-aspectos-transversais.md#3-observabilidade)
  lista as 9 regras e as condições de cada uma.
- **Teste automatizado:** `CashFlowEntryService.OutboxPublisherTests/CorrelationHeaderTests.cs` (o
  `correlationId` viaja num cabeçalho da mensagem publicada no RabbitMQ).
- **Como voltar ao estado inicial:** nada a desfazer: só leitura.

## Testes automatizados

| Projeto | O que prova | Comando |
|---|---|---|
| `CashFlowEntryService.UnitTests` | Regras do lançamento, fingerprint (o resumo do conteúdo da requisição, comparado nas repetições), espera crescente entre as tentativas do publisher (backoff) e `traceId` | `dotnet test tests/CashFlowEntryService.UnitTests` |
| `CashFlowEntryService.IntegrationTests` | A API real com PostgreSQL em container: autenticação, idempotência, validação, lançamentos que não podem ser alterados, limite de requisições — tudo sem broker acessível | `dotnet test tests/CashFlowEntryService.IntegrationTests` |
| `CashFlowEntryService.OutboxPublisherTests` | O publisher do Outbox contra um RabbitMQ real, em container | `dotnet test tests/CashFlowEntryService.OutboxPublisherTests` |
| `ConsolidationService.UnitTests` | O cálculo do saldo de abertura, dos créditos, dos débitos e do saldo de fechamento; a classificação das falhas do consumidor (temporária ou inesperada); `traceId` | `dotnet test tests/ConsolidationService.UnitTests` |
| `ConsolidationService.IntegrationTests` | Consumidor, Inbox, DLQ, consulta por intervalo e autenticação, com PostgreSQL e RabbitMQ em containers | `dotnet test tests/ConsolidationService.IntegrationTests` |
| `tests/load/*.js` | 50 requisições por segundo contra o ambiente real, com limites de tempo de resposta e de falhas | `scripts/load/run.sh all` |
