# Procedimentos de operação (runbooks)

Este documento diz o que fazer quando uma peça cai ou quando os totais do Consolidado não batem
com os lançamentos. Cada procedimento (runbook) segue a mesma ordem: quando usar, sintoma,
impacto, passos, verificação e o risco que resta. O comportamento do sistema em cada falha — e
por que nada se perde — está em
[`docs/05-aspectos-transversais.md` §2](05-aspectos-transversais.md#2-resiliência-e-recuperação).

Os comandos valem para o ambiente local (`docker compose`) e rodam a partir da raiz do
repositório. Em produção, troque `docker compose exec`, `stop` e `start` pelo equivalente do
ambiente (`kubectl`, o console do banco ou do broker gerenciado); o diagnóstico e a verificação
continuam os mesmos. Os dois bancos aparecem pelos nomes dos diagramas — DB (database) quer dizer
banco de dados: `Cash Flow Entry DB` é o banco de Lançamentos, e `Consolidation DB`, o do Consolidado.

**Alertas.** Os alertas citados aparecem em http://localhost:9090/alerts; o que cada um mede está
em [`docs/05-aspectos-transversais.md` §3](05-aspectos-transversais.md#alertas). Nesta entrega,
nada envia notificação: o Alertmanager, a ferramenta que envia os alertas por e-mail ou chat, é
uma evolução. Alguém precisa olhar `/alerts` ou o painel do Grafana, ou rodar as verificações
abaixo à mão ou num agendador (cron).

## Scripts de operação

Três scripts, executados à mão por um operador, verificam e corrigem o Consolidado. Eles não
fazem parte das APIs e não ligam um serviço ao outro: leem e gravam direto nos bancos e no broker,
fora do caminho das requisições.

| Script | O que faz | Quando usar |
|---|---|---|
| `scripts/ops/reconcile.sh` | Compara, dia a dia (`business_date`), a soma dos lançamentos (`cashflow_entry_db.cash_flow_entries`) com os totais do Consolidado (`consolidation_db.daily_consolidations`). Sai com código `1` se algum dia não bater. | Para conferir os totais, sempre que houver dúvida, e no fim dos outros procedimentos. |
| `scripts/ops/replay-dlq.sh [timeout]` | Devolve à fila principal as mensagens que estão na DLQ (Dead Letter Queue, fila das mensagens que não puderam ser processadas). Usa um shovel dinâmico, recurso do RabbitMQ que move mensagens de uma fila para outra. | Depois de corrigida a causa da falha (§4). |
| `scripts/ops/rebuild-consolidation.sh [--yes]` | Apaga e recalcula do zero os totais do Consolidado, a partir das mensagens guardadas no Outbox de Lançamentos — a tabela `outbox_messages`, com uma mensagem por lançamento. A tabela `cash_flow_entries` nunca é alterada. | Quando os totais divergem e reenviar a DLQ não resolve (§5). |

Para apagar todos os dados de teste — dos dois bancos, do RabbitMQ, do Prometheus e do Grafana —,
use `docker compose down -v`. Nunca em produção: o comando apaga os volumes inteiros, e não só as
linhas de teste.

## 1. Broker (RabbitMQ) fora do ar

**Quando usar.** Quando o RabbitMQ para de responder e o publisher do Outbox — a tarefa de
Lançamentos que envia as mensagens ao broker — não consegue publicar.

**Sintoma.** No `CashFlowEntryService`, `broker_publish_failures_total{reason="connection"}` sobe e
`outbox_pending_count` e `outbox_oldest_age_seconds` crescem; o painel "RabbitMQ up" fica vermelho;
`/health/broker` dos dois serviços responde `Degraded`.

**Impacto.** Os lançamentos continuam sendo aceitos normalmente: o broker nunca está no caminho
do registro (NFR-001, requisito: o Consolidado fora do ar não derruba Lançamentos; o ADR-002,
decisão sobre a integração assíncrona, estende essa garantia ao broker). O Consolidado para de
receber eventos novos e fica atrasado; a métrica de atraso `consumer_lag_seconds` cresce. Nenhum
dinheiro se perde: o Outbox guarda as mensagens como `Failed`, prontas para nova tentativa, pelo
tempo que for preciso (ADR-003, decisão sobre o Outbox).

**Passos.**
1. Confirmar o diagnóstico:
   ```bash
   curl -s http://localhost:5101/health/broker; echo
   docker compose ps rabbitmq
   docker compose logs --tail=100 rabbitmq
   ```
2. Religar o broker, ou tratar a causa: disco, memória ou rede.
   ```bash
   docker compose start rabbitmq
   ```
3. Nada a fazer nos serviços. Os dois reconectam sozinhos (`AutomaticRecoveryEnabled = true`), e
   o publisher do Outbox envia as mensagens acumuladas em lotes de até 100 (`Outbox:BatchSize`).
   Quando um lote sai inteiro, ele busca o próximo na hora; só espera 500 ms
   (`Outbox:PollIntervalMs`) quando não há mais um lote cheio para enviar
   (`docs/05-aspectos-transversais.md` §2).

**Verificação.**
```bash
curl -s http://localhost:5101/health/broker; echo    # Healthy
docker compose exec postgres psql -U cashflow -d cashflow_entry_db \
  -c "SELECT status, attempt_count FROM outbox_messages ORDER BY created_at_utc DESC LIMIT 5;"
  # status volta a Published
./scripts/ops/reconcile.sh                            # PASS depois que o backlog esvaziar
```

**Risco que resta.** Enquanto o broker está fora, o Outbox cresce dentro do banco de Lançamentos.
Numa queda muito longa e com pouco disco, ele pode, em tese, pressionar o armazenamento do
próprio banco. O alerta `OutboxStale` (mensagem esperando há mais de 60 s, por 2 minutos) mostra a
situação.

## 2. ConsolidationService parado ou com mensagens acumulando

**Quando usar.** Quando o Consolidado está parado, ou não dá conta das mensagens que chegam e elas
se acumulam na fila (backlog).

**Sintoma.** `/health/ready` do `ConsolidationService` falha, ou o processo está parado. O painel
"Queue depth — main, retry.1-3, DLQ (messages ready)" mostra a fila principal
(`consolidation.cashflow-entry-recorded.v1`) crescendo sem consumidor. `consumer_lag_seconds` para de
receber pontos novos.

**Impacto.** Os lançamentos continuam sendo aceitos (NFR-001). O Consolidado fica desatualizado:
`GET /consolidation` responde com os totais de antes da parada — `asOfUtc` e
`lastAppliedLagSeconds` presos no último evento aplicado — ou, se a própria API caiu, com erro de
conexão.

**Passos.**
1. Diagnosticar:
   ```bash
   docker compose ps consolidationservice
   docker compose logs --tail=200 consolidationservice
   ```
2. Religar o serviço:
   ```bash
   docker compose start consolidationservice
   ```
   O `docker-compose.yml` já religa o serviço sozinho quando ele cai (`restart: unless-stopped`).
   Este passo cobre os outros casos: alguém o parou de propósito e esqueceu, ou o ambiente não tem
   essa política.
3. Se o serviço voltar com muitas mensagens acumuladas, acompanhe a fila esvaziar:
   ```bash
   watch -n 5 "docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages"
   ```

**Verificação.**
```bash
curl -s http://localhost:5102/health/ready; echo    # Healthy
./scripts/ops/reconcile.sh                            # PASS depois do dreno completo
```

**Risco que resta.** Nenhuma perda: as mensagens esperam na fila durável (volume `rabbitmq-data`)
pelo tempo que o serviço ficar parado. Nenhuma é confirmada ao broker — o ACK (acknowledgement, a
confirmação de recebimento) — antes de ser gravada no banco do Consolidado. O único efeito é o
Consolidado atrasado por mais tempo, o que é visível e esperado.

## 3. Consolidation DB fora do ar (ou indisponibilidade de qualquer duração)

**Quando usar.** Quando o banco do Consolidado (`Consolidation DB`) cai — por segundos ou por
horas — e o consumidor não consegue gravar.

**Sintoma.** `consumer_events_total{outcome="retry_transient"}` sobe; `/health/ready` do
`ConsolidationService` responde `Unhealthy`; os logs do consumidor mostram
`"Falha transitória processando evento ...; mantendo em retry, nunca DLQ."`.

**Impacto.** Os lançamentos continuam sendo aceitos (NFR-001). O consumidor trata a queda do banco
como falha temporária: a mensagem nunca é dada como processada nem vai para a DLQ. Ela passa
pelas filas de espera e, a partir do último nível, volta a ele indefinidamente, enquanto a queda
durar. A meta é
voltar em até 60 minutos — o RTO (Recovery Time Objective, tempo máximo para voltar) do
Consolidado, no NFR-014 —, mas o mecanismo em si não impõe esse limite.

**Passos.**
1. Diagnosticar:
   ```bash
   docker compose ps postgres
   docker compose logs --tail=200 postgres
   curl -s http://localhost:5102/health/ready; echo
   ```
2. Recuperar o PostgreSQL: religar o container, restaurar um backup ou liberar disco, conforme a
   causa.
3. Nada a fazer nos serviços: o consumidor continua tentando sozinho e, quando o banco volta, a
   próxima tentativa funciona.

**Verificação.**
```bash
docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages | grep dlq   # deve continuar 0
curl -s http://localhost:5102/metrics | grep 'consumer_events_total{.*outcome="retry_transient"'
./scripts/ops/reconcile.sh
```

**Risco que resta.** Se a queda durar mais que a espera mais longa configurada (o último valor de
`RabbitMq:RetryDelaysMs`), as mensagens continuam voltando a esse último nível, sem limite de
tempo. Os alertas `ProjectionLagHigh` (o Consolidado atrás da meta do NFR-005 — 95% dos
lançamentos na consulta em até 5 s — por 5 minutos) e
`ConsolidationBacklogGrowing` (a fila principal crescendo por 5 minutos) mostram a situação.

## 4. DLQ não vazia

**Quando usar.** Quando há alguma mensagem na DLQ — por exemplo, com o alerta
`DeadLetterQueueNotEmpty` disparado.

**Sintoma.** `dlq_messages_total` aumenta; o painel "Queue depth — main, retry.1-3, DLQ (messages
ready)" mostra mensagens em `consolidation.cashflow-entry-recorded.v1.dlq`;
`consumer_events_total{outcome="rejected"}` ou `{outcome="dead_lettered"}` sobe.

**Impacto.** O lançamento original não se perde: ele está em `cash_flow_entries`, que nunca muda. Os eventos
que estão na DLQ não foram somados aos totais: o Consolidado fica incompleto para esses
lançamentos até alguém agir.

**Passos.**
1. Ver a causa antes de reenviar — nem toda mensagem na DLQ deve voltar sem investigação:
   ```bash
   curl -s -u cashflow:cashflow-dev-only -X POST \
     http://localhost:15672/api/queues/%2F/consolidation.cashflow-entry-recorded.v1.dlq/get \
     -H "Content-Type: application/json" \
     -d '{"count":10,"ackmode":"ack_requeue_true","encoding":"auto"}' | python3 -m json.tool
   ```
   O motivo de cada mensagem está no cabeçalho `x-failure-reason`.
2. Se a causa já foi corrigida — por exemplo, um defeito no consumidor, ou uma falha temporária
   classificada errado, já corrigida no código —, ou se as mensagens podem ser processadas de novo,
   reenvie:
   ```bash
   ./scripts/ops/replay-dlq.sh
   ```
3. Uma mensagem **permanentemente inválida** — formato incompatível, ou um defeito em quem a
   publicou — volta para a DLQ depois do reenvio. É o esperado, e não um defeito do reenvio. Nesse
   caso, a correção é no código ou em quem publica, e não um novo reenvio.

O reenvio é seguro mesmo para uma mensagem que já tinha sido somada: a Inbox, a tabela dos eventos
já aplicados, descarta a cópia repetida (ADR-004, decisão sobre idempotência).

**Verificação.**
```bash
docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages | grep dlq
./scripts/ops/reconcile.sh
```

**Risco que resta.** Mensagens permanentemente inválidas podem se acumular na DLQ até alguém
investigar.

## 5. Totais divergentes: conferir, reenviar a DLQ, reconstruir

**Quando usar.** Quando os totais do Consolidado não batem com a soma dos lançamentos:
`scripts/ops/reconcile.sh` sai com código `1`.

**Sintoma.** O `reconcile.sh` lista os dias (`business_date`) em que `cashflow_entry_db.cash_flow_entries` e
`consolidation_db.daily_consolidations` divergem.

**Impacto.** Para esses dias, o Consolidado não mostra o que os lançamentos dizem. A tabela
`cash_flow_entries` nunca é afetada, e a divergência sempre pode ser corrigida recalculando os totais
(NFR-009, requisito: os totais podem ser recuperados; A-005, premissa: os lançamentos são a fonte
da verdade).

**Depois de um teste de carga, não há nada a fazer.** O teste de consulta grava um histórico
sintético direto nos totais e, no fim, o próprio script recalcula os totais e roda o
`reconcile.sh` ([`docs/10-roteiro-de-testes.md` §11](10-roteiro-de-testes.md#11-carga-50-requisições-por-segundo)).

**Passos, da menor para a maior intervenção.**

1. **Conferir e localizar os dias.**
   ```bash
   ./scripts/ops/reconcile.sh
   ```
2. **Se houver mensagens na DLQ relacionadas, reenviar primeiro** — é mais barato que recalcular
   tudo:
   ```bash
   ./scripts/ops/replay-dlq.sh
   ./scripts/ops/reconcile.sh
   ```
3. **Se a divergência continuar** — por exemplo, a tabela de totais foi alterada direto no banco,
   ou um defeito já corrigido deixou totais errados —, recalcular os totais a partir do Outbox:
   ```bash
   ./scripts/ops/rebuild-consolidation.sh --yes
   ```
   O script, em ordem: para o `ConsolidationService`; apaga a Inbox, os totais por dia e os
   metadados (`inbox_messages`, `daily_consolidations`, `projection_metadata`); esvazia as filas
   do Consolidado, incluindo a DLQ; marca todo o Outbox como `Pending`, para o publisher enviar
   tudo de novo; religa o `ConsolidationService`; espera o Outbox ser todo publicado e as filas do Consolidado
   esvaziarem — a principal e as três de espera; a DLQ só gera um aviso —; e roda o
   `reconcile.sh`.

**Verificação.** O `rebuild-consolidation.sh` termina rodando o `reconcile.sh`: código de saída
`0` no fim do script é a prova.

**Alertas esperados durante a reconstrução.** `consumer_lag_seconds` sobe para minutos, e os
alertas `ProjectionLagHigh` e `ConsolidationBacklogGrowing` podem ficar pendentes ou disparar.
O motivo: a métrica mede `agora − occurredAtUtc` de cada evento aplicado, e a reconstrução
reaplica todos os eventos antigos. Um lançamento registrado há 30 minutos e reaplicado agora mede
30 minutos de atraso. Não é uma regressão. O caso se reconhece pela reconstrução (ou pelo teste de
carga) rodada há pouco — veja `docker compose logs consolidationservice` ou a saída do script — e
pelo atraso que volta ao normal assim que os eventos antigos terminam de ser processados.
Silencie o alerta, ou anote a janela da reconstrução, em vez de tratá-lo como incidente.

**Risco que resta.** A reconstrução depende de as mensagens publicadas continuarem no Outbox; não
há tarefa que as apague (A-012, premissa: as mensagens do Outbox ficam guardadas sem prazo). Se
um lançamento muito antigo perdesse a linha do Outbox por um motivo fora do previsto, este
mecanismo não o republicaria; seria preciso ler direto de `cash_flow_entries`, uma evolução não
implementada. Durante a reconstrução, o Consolidado fica fora do ar: é uma operação manual, e não
uma degradação invisível ao usuário.

## 6. Cash Flow Entry DB fora do ar

**Quando usar.** Quando o banco de Lançamentos (`Cash Flow Entry DB`) cai e o registro de lançamentos
falha.

**Sintoma.** `/health/ready` do `CashFlowEntryService` responde `Unhealthy`; registrar e consultar
lançamentos falha. `CashFlowEntryService` depende do próprio banco para tudo, por desenho: não há
alternativa.

**Impacto.** Sem o banco, Lançamentos não tem onde registrar. Ao contrário do Consolidado
(NFR-001), nenhum requisito torna esta queda tolerável: caiu a própria fonte da verdade do
serviço. Por isso, aqui **não há como contornar**: os ADR-001 e ADR-002 (decisões sobre o estilo
arquitetural e a integração assíncrona) tornam o Consolidado dispensável, e não o banco de
Lançamentos.

**Passos.**
1. Diagnosticar:
   ```bash
   docker compose ps postgres
   docker compose logs --tail=200 postgres
   ```
2. Recuperar o PostgreSQL: religar, restaurar o backup mais recente, resolver disco, memória ou
   rede. Em produção, vale o procedimento de alta disponibilidade do banco gerenciado — HA (High
   Availability), com a troca automática para a cópia de espera
   ([`docs/06-arquitetura-alvo.md` §4](06-arquitetura-alvo.md)).
3. Não há nada a republicar nem a reconstruir: o banco de Lançamentos é a fonte da verdade, e
   nada derivado dele precisa ser refeito.

**Verificação.**
```bash
curl -s http://localhost:5101/health/ready; echo    # Healthy
# Registrar um lançamento de teste e confirmar 201 normalmente.
```

**Risco que resta.** A meta de não perder nenhum lançamento confirmado — RPO (Recovery Point
Objective, perda máxima aceitável de dados) igual a zero, NFR-014 — depende de o backup e a
replicação do banco estarem configurados e testados. Isso é responsabilidade da infraestrutura e
está fora do que este ambiente local demonstra, com um único PostgreSQL sem cópia. A topologia de
produção, com a cópia síncrona em outra zona, está em `docs/diagrams/svg/Deployment-Alvo.svg`.

## 7. Erros ou lentidão das APIs acima da meta

**Quando usar.** Quando dispara um alerta de erro ou de lentidão das APIs:
`ConsolidationServiceErrors`, `CashFlowEntryLatencyHigh` ou `ConsolidationLatencyHigh`.

**Sintoma.** Um desses alertas aparece em http://localhost:9090/alerts; os painéis "5xx rate" ou
de latência p95 do Grafana ficam fora da meta.

**Impacto.** Nenhum lançamento se perde e o registro continua disponível (NFR-001): é um sinal de
que a API está mais lenta ou falhando mais, e não de dado perdido. `ConsolidationServiceErrors`
mede, no Consolidado, a "perda de requisições" do NFR-C (requisito do PDF: no máximo 5% de perda
de requisições), lida como em A-004 (premissa: perda é requisição que falha, e não dado perdido):
mais de 5% das consultas com erro em 5 minutos. O alerta usa uma proporção, e não uma contagem,
porque o limite aceito é uma porcentagem, e não zero. `CashFlowEntryLatencyHigh` e
`ConsolidationLatencyHigh` medem as metas de tempo de resposta do NFR-013 (p95 acima de 300 ms e
de 500 ms por 10 minutos).

**Passos.**
1. Procurar primeiro a causa mais provável — falta de recursos no PostgreSQL ou na máquina, e não
   o código:
   ```bash
   docker compose stats --no-stream
   docker compose logs --tail=100 cashflowentryservice consolidationservice | grep -i error
   ```
2. Confirmar que os erros são do tipo `outcome="error"` (resposta `5xx` ou exceção), e não de
   validação ou conflito de negócio (`4xx`): o alerta mede só o primeiro. O painel "5xx rate" do
   Consolidado soma também `outcome="unavailable"` (`503`, banco fora do ar); o alerta, não.
3. Sem causa de infraestrutura aparente, é sinal de capacidade, e não de defeito no código: veja a
   escalabilidade em [`docs/06-arquitetura-alvo.md` §6](06-arquitetura-alvo.md#6-escalabilidade)
   antes de supor um defeito.

**Verificação.**
```bash
curl -s 'http://localhost:9090/api/v1/query?query=histogram_quantile(0.95,sum(rate(cashflow_entry_request_duration_ms_milliseconds_bucket%5B5m%5D))by(le))'
curl -s 'http://localhost:9090/api/v1/query?query=histogram_quantile(0.95,sum(rate(consolidation_query_duration_ms_milliseconds_bucket%5B5m%5D))by(le))'
```

**Risco que resta.** Como os demais alertas desta entrega, este só torna o problema visível em
`/alerts`; ninguém é notificado automaticamente.

## 8. Mudar as esperas das filas de retry (`RabbitMq:RetryDelaysMs`)

**Quando usar.** Antes de mudar as esperas entre as novas tentativas do consumidor (hoje 5 s, 30 s
e 120 s) num broker que já tem as filas criadas.

**Sintoma, se a mudança for feita só reiniciando o serviço.** Cada fila de espera
(`consolidation.cashflow-entry-recorded.v1.retry.1`, `.2` e `.3`) é criada com um TTL (Time To Live, tempo
de vida da mensagem na fila) fixo, o argumento `x-message-ttl`. O RabbitMQ recusa criar de novo uma
fila que já existe com outro valor. O consumidor não consegue começar a consumir e tenta de novo a
cada 5 s, para sempre. O log mostra:

```text
Falha ao conectar/consumir do RabbitMQ; nova tentativa em 5s.
RabbitMQ.Client.Exceptions.OperationInterruptedException: ... code=406, text='PRECONDITION_FAILED -
inequivalent arg 'x-message-ttl' for queue 'consolidation.cashflow-entry-recorded.v1.retry.1' in vhost '/':
received '10000' but current is '5000''
```

`/health/ready` continua `Healthy`, porque olha só o banco; quem mostra o problema é a fila
principal crescendo (alerta `ConsolidationBacklogGrowing`) e
`rabbitmqctl list_consumers` sem nenhum consumidor.

**Impacto.** Os lançamentos continuam sendo aceitos (NFR-001, requisito: o Consolidado fora do ar
não derruba Lançamentos). O Consolidado para de receber eventos até o procedimento abaixo.

**Passos.**
1. Parar o consumidor, para que nenhuma mensagem nova entre nas filas de espera:
   ```bash
   docker compose stop consolidationservice
   ```
2. Esperar as três filas de espera chegarem a 0. Mesmo com o consumidor parado, cada mensagem
   volta sozinha para a fila principal quando o TTL acaba, ou seja, em no máximo 120 s. Nunca apague
   uma fila com mensagens:
   ```bash
   docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages | grep 'retry\.'
   ```
3. Apagar as três filas de espera. A fila principal e a DLQ não mudam:
   ```bash
   for n in 1 2 3; do
     docker compose exec -T rabbitmq rabbitmqctl -q delete_queue "consolidation.cashflow-entry-recorded.v1.retry.$n"
   done
   ```
   Cada comando deve responder `Queue was successfully deleted with 0 ready messages`.
4. Mudar o valor (no compose, por exemplo `RabbitMq__RetryDelaysMs__0: "10000"` no serviço
   `consolidationservice`) e religar o serviço. Ele cria as filas de novo, já com as novas esperas:
   ```bash
   docker compose up -d --wait consolidationservice
   ```

**Verificação.**
```bash
docker compose exec -T rabbitmq rabbitmqctl -q list_queues name arguments | grep 'retry\.'   # x-message-ttl com os novos valores
docker compose exec -T rabbitmq rabbitmqctl -q list_consumers queue_name                     # um consumidor em consolidation.cashflow-entry-recorded.v1
docker compose logs --since 1m consolidationservice | grep -c 'Falha ao conectar'           # 0
```

**Risco que resta.** Entre os passos 1 e 4, o Consolidado fica parado e atrasado, como no §2. Se
uma fila for apagada com mensagens, essas mensagens saem do broker; elas continuam no Outbox de
Lançamentos e voltam com a reconstrução (§5). O mesmo procedimento vale para mudar o número de
níveis; a partir de então, uma falha inesperada passa a ter tantas novas tentativas quantos níveis
houver.
