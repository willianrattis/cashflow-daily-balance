# Aspectos transversais

Este documento reúne o que vale para os dois serviços ao mesmo tempo: segurança, resiliência,
observabilidade e capacidade. Ele transforma as decisões dos ADRs (Architecture Decision Records,
registros de decisão de arquitetura) em controles concretos e diz como verificar cada um, sem
acrescentar componentes fora do escopo do desafio.

## 1. Segurança e modelo de ameaças

Um modelo de ameaças lista o que precisa ser protegido, por onde um ataque pode entrar e qual
controle responde a cada ameaça.

### Ativos e fronteiras de confiança

- **O que proteger (ativos):** os lançamentos, o Outbox (a tabela das mensagens a publicar), as
  credenciais e os tokens, os totais do Consolidado, os logs de auditoria e os backups.
- **Fronteiras de confiança** — os pontos em que um dado passa de um lado para outro e precisa
  ser verificado: cliente → API; API → banco; publisher → broker (o RabbitMQ); broker →
  consumidor; serviço → provedor de identidade.
- **Quem decide o acesso:** as informações do token de acesso, emitidas pelo provedor de
  identidade, e as regras de autorização de cada serviço.

### Ameaças e controles

| Ameaça | Controle | Como verificamos |
|---|---|---|
| Token falsificado, vencido ou emitido para outra API | Cada serviço confere a assinatura, o emissor, o `audience` (`cashflow-api`, o mesmo nas duas APIs) e a validade do token. Na Arquitetura Alvo, o provedor de identidade também troca as chaves de assinatura periodicamente (`docs/06-arquitetura-alvo.md` §8). | `JwtAuthenticationTests`, nos dois serviços: token vencido, emissor errado, `audience` inválido ou assinatura inválida → `401`; token sem a permissão (scope) exigida → `403`; token só com a permissão certa → `201` ou `200`. |
| Requisição ou evento repetido (replay): o mesmo comando reenviado, ou o mesmo evento entregue de novo | `Idempotency-Key`, a Inbox (a tabela dos eventos já aplicados) e restrições `UNIQUE` no banco. | `RecordLaunchTests` (requisições simultâneas com a mesma chave geram um só lançamento) e `ConsumerTests` (um evento entregue de novo não é somado duas vezes). |
| Alteração ou remoção de um lançamento | Lançamentos só recebem linhas novas: a API não tem `PUT` nem `DELETE`, e um trigger do banco recusa `UPDATE` e `DELETE` em `launches`. Cada serviço usa um usuário de banco sem poderes de administrador, e cada lançamento guarda quem o registrou. O trigger não bloqueia `TRUNCATE`, mas nada nesta entrega executa esse comando em `launches`. | `DatabaseIntegrityTests`: um `UPDATE` ou `DELETE` direto em SQL falha com o erro do trigger. |
| Corpo da requisição malformado ou malicioso | Validação do formato, com campos desconhecidos recusados. Limites de tamanho: 64 KB (kilobytes) por requisição, 200 caracteres na `Idempotency-Key` e 1000 na descrição. Valor positivo e natureza só `Credit` ou `Debit`. | `InputValidationTests` (`400`), `ProductionErrorEnvelopeTests` (o mesmo `400 invalid_request` com o envelope de erro no ambiente `Production`) e `RecordLaunchTests` (`422` para valor e natureza inválidos). |
| Vazamento de dados nos logs | Os serviços não registram tokens, segredos nem o corpo das requisições; o `correlationId` identifica a operação sem expor dados. No ambiente local, os comandos SQL aparecem no log sem os valores (`'?'`). | Leitura dos logs, como no exemplo da §3; nenhum código registra o cabeçalho `Authorization` ou o corpo de uma requisição. |
| Segredo exposto | Nesta entrega, as senhas e a chave de assinatura são de desenvolvimento e estão no repositório de propósito; fora do ambiente `Development`, o serviço se recusa a iniciar com a chave de desenvolvimento. Na Arquitetura Alvo, os segredos ficam fora do repositório, num cofre de chaves gerenciado, são trocados periodicamente, e cada serviço se autentica no cofre pela própria identidade na nuvem, sem senha guardada. | `JwtSigningKeyGuardTests`: fora de `Development`, o serviço não inicia com a chave de desenvolvimento. Alvo em `docs/06-arquitetura-alvo.md` §8. |
| Painéis de administração expostos: RabbitMQ (porta 15672), Grafana (3000, com acesso anônimo), Prometheus (9090) e PostgreSQL (5433) | Só ficam disponíveis na máquina local do avaliador (`docker-compose.yml`) e nunca foram pensados para acesso público; as credenciais são de desenvolvimento e estão documentadas. Na Arquitetura Alvo, só se chega a eles por VPN (Virtual Private Network, rede privada virtual) ou por uma máquina de acesso controlado (bastion) numa rede privada, e o Grafana não tem acesso anônimo. | `docker-compose.yml` (portas); nenhum deles é descrito como aberto à internet. |
| DoS (Denial of Service, ataque que tenta derrubar o serviço com excesso de requisições) vindo de um cliente autenticado | Limitador de requisições por chamador, em janelas fixas de 1 segundo, separado pelo `sub` do token: 100 escritas e 500 leituras por segundo (configurável em `RateLimiting:*`). Cada recusa (`429`) é contada na métrica de requisições do serviço com `outcome="rate_limited"` e gera uma linha de log de aviso (`Warning`) com o `correlationId` e o `sub` do chamador (§3). Também: limite de 64 KB no corpo da requisição, imposto pelo Kestrel (o servidor web do .NET), e os limites do Npgsql (o driver do PostgreSQL para .NET): 15 s para abrir uma conexão, 30 s por comando (os dois são o padrão do driver) e no máximo 40 conexões abertas por cópia do serviço (A-029, premissa sobre o tamanho do pool de conexões; §2). | `RateLimitingTests` (nos dois serviços): `429` com o envelope de erro; cada `429` aumenta o contador `rate_limited` e gera um log de aviso com o `correlationId`; o limite de um chamador não afeta outro; `/health/live` nunca é limitado. |
| DoS sem token: uma enxurrada de requisições anônimas | A requisição sem token válido recebe `401` antes de chegar ao limitador (`UseAuthorization` roda antes de `UseRateLimiter`, em `RequestPipelineExtensions.cs`, chamado pelo `Program.cs` de cada serviço). Conter uma enxurrada de requisições sem token é papel da entrada do sistema — o API Gateway com WAF (Web Application Firewall, filtro que bloqueia ataques conhecidos) da Arquitetura Alvo, descrito no ADR-012 (decisão sobre a borda) —, e não deste limitador. | `JwtAuthenticationTests`: sem token → `401`. |
| Mensagem envenenada: uma mensagem que sempre faz o consumidor falhar | Novas tentativas limitadas, depois a DLQ (Dead Letter Queue, fila das mensagens que não puderam ser processadas), um alerta e um procedimento de correção (`docs/07-runbooks.md` §4). | `ConsumerTests`: um evento inválido vai para a DLQ sem derrubar o consumidor. |

**Por que esses limites de requisições.** 100 escritas e 500 leituras por segundo, por
chamador, ficam bem acima das metas — 50 lançamentos por segundo (NFR-011, requisito de volume de
escrita) e 50 consultas por segundo no pico (§4). Assim, o teste de carga (`scripts/load/run.sh`,
resultados em `docs/evidence/load/`) e qualquer pico legítimo nunca recebem `429`: o limite existe
para conter um cliente descontrolado ou mal-intencionado, não para apertar o tráfego normal.
Separar a contagem por chamador (o `sub` do token) impede que um único integrador com defeito
esgote a cota de todos; sem isso, ele consumiria sozinho a janela compartilhada. Uma requisição
sem token válido nunca chega ao limitador, como mostra a penúltima linha da tabela.

### Controles mínimos

- **Criptografia em trânsito.** TLS (Transport Layer Security, a criptografia da conexão) em
  todas as conexões externas e entre serviços implantados separadamente. Nesta entrega local, o
  tráfego entre os containers não usa TLS (`docs/06-arquitetura-alvo.md` §8).
- **Menor privilégio.** Lançamentos não grava no banco do Consolidado. Cada serviço se conecta ao
  PostgreSQL com o próprio usuário, sem poderes de administrador (`launch_svc`,
  `consolidation_svc`), e sem permissão de conexão (`CONNECT`) ao banco do outro. O script
  `scripts/postgres/init-databases.sh` cria esses usuários e retira de todos (`PUBLIC`) a
  permissão de conexão que o PostgreSQL dá por padrão. Os scripts de `scripts/ops/` e
  `scripts/postgres/` usam de propósito o usuário administrador — do PostgreSQL, porque precisam
  ler ou escrever nos dois bancos a partir de um só script, ou do RabbitMQ, no caso do
  `replay-dlq.sh`, que cria um shovel (uma regra do broker que move mensagens de uma fila para
  outra); isso está documentado no cabeçalho de cada um.
- **Containers sem administrador.** Os containers rodam com um usuário comum, e não com o
  administrador do sistema (`USER $APP_UID` nos dois Dockerfiles).
- **Criptografia dos dados gravados em disco** — bancos, backups e volumes do broker —, onde o
  ambiente oferecer. Nesta entrega local, não está configurada (`docs/06-arquitetura-alvo.md` §8).
- **Trilha de auditoria.** Cada lançamento guarda quem o registrou (`sub`), o `correlationId` e o
  instante do registro. A operação é sempre a mesma — um registro novo —, porque lançamentos não
  mudam.
- **Dependências e imagens com versão fixa** (`Directory.Packages.props` e as tags das imagens).
  Atualizá-las e verificá-las contra vulnerabilidades conhecidas (scan) é uma prática a adotar no
  pipeline de entrega; não há scan nesta entrega.
- **Fora destes serviços:** o provedor de identidade, a interface gráfica e a gestão de usuários
  de produção.

## 2. Resiliência e recuperação

### Estratégia por falha

| Falha | O que acontece | O que garante |
|---|---|---|
| `ConsolidationService` fora do ar | Lançamentos continua registrando; as mensagens se acumulam na fila. | Integração por mensagens e Outbox. |
| Broker fora do ar | As mensagens ficam pendentes no Outbox. | O publisher tenta de novo, esperando cada vez mais entre as tentativas, sem apagar nenhuma mensagem. |
| Nenhuma fila ligada ao destino da mensagem (no primeiro início, ou com o broker recriado) | A mensagem continua pendente no Outbox; nunca é marcada como publicada. | Publicação com `mandatory: true` e com confirmação do broker (publisher confirms): o RabbitMQ devolve a mensagem que não chegou a nenhuma fila, e o publisher trata isso como falha e tenta de novo (ADR-003, decisão sobre o Outbox). |
| Falha temporária no consumidor: conexão, timeout, PostgreSQL sem recursos | A mensagem não é dada como processada e nunca vai para a DLQ: volta de uma fila de espera quantas vezes for preciso. | O classificador de falhas trata o caso como temporário (ADR-002, decisão sobre a integração assíncrona): a mensagem sobe os níveis de espera (5 s, depois 30 s) e então fica no último nível, de 120 s, tentando de novo a cada 120 s até dar certo. Métrica `consumer_events_total{outcome="retry_transient"}`. |
| Falha inesperada no consumidor: um defeito, ou um formato que o código não entende | Até três novas tentativas, depois a DLQ. | Novas tentativas limitadas (`RabbitMq:RetryDelaysMs`) e a métrica de atraso; `outcome="retry_unexpected"` enquanto há tentativas, depois `outcome="dead_lettered"`. |
| Evento inválido: formato ou versão errados | Vai direto para a DLQ, sem nova tentativa. | DLQ com o motivo no cabeçalho `x-failure-reason`, alerta e reenvio controlado (`scripts/ops/replay-dlq.sh`). |
| Banco do Consolidado fora do ar, por qualquer tempo | A mensagem nunca é dada como processada, e não vai para a DLQ enquanto o erro for temporário. | A gravação acontece numa transação local antes do ACK (acknowledgement, a confirmação de recebimento ao broker); o classificador trata o banco fora do ar como falha temporária (ADR-002). |
| Mais requisições simultâneas do que conexões livres com o PostgreSQL (por exemplo, 100 consultas ao mesmo tempo) | A requisição espera uma conexão livre do pool de conexões (o conjunto de conexões abertas que cada serviço reaproveita entre requisições), em vez de falhar. Se nenhuma se liberar em 15 s, a consulta responde `503` com `consolidation_unavailable`. | Cada serviço abre no máximo 40 conexões (`Maximum Pool Size=40` na connection string). Juntos, os dois ficam em 80, abaixo das 97 que o PostgreSQL local aceita de usuários comuns (`max_connections` = 100, menos 3 reservadas ao administrador), e sobram 17 para migrações e para os scripts de `scripts/ops/` (A-029). Sem esse limite, cada serviço podia abrir até 100: numa rajada de 1.500 consultas, 100 em paralelo, o PostgreSQL recusou conexões (`remaining connection slots are reserved for roles with the SUPERUSER attribute`) e de 1 a 36 consultas responderam `503`; com o limite, 3 rajadas iguais deram 4.500 respostas `200`. |
| Banco de Lançamentos fora do ar | O registro falha, e o cliente nunca recebe um sucesso falso. | Verificação de saúde (`/health/ready`), timeout e as metas de recuperação abaixo. |
| Totais do Consolidado corrompidos | A consulta fica degradada enquanto os totais são reconstruídos. | Reconstrução a partir dos lançamentos (`scripts/ops/rebuild-consolidation.sh`). |

O publisher do Outbox espera cada vez mais entre uma tentativa e outra — 2 s, 4 s, 8 s… — com um
tempo aleatório de até 1 s somado a cada espera, para que muitas mensagens não tentem de novo no
mesmo instante. A espera tem um teto, 5 minutos, mas o número de tentativas não tem: enquanto o
broker estiver fora, o publisher continua tentando, porque nenhum lançamento pode ficar sem
publicar por desistência. O consumidor usa esperas fixas de 5 s, 30 s e 120 s. Um comando
financeiro nunca deve ser repetido às cegas sem `Idempotency-Key`. Um disjuntor (circuit breaker,
que corta as chamadas a uma dependência que está falhando) é útil para dependências síncronas
externas, mas o caminho de registro não tem nenhuma chamada síncrona ao Consolidado.

O publisher do Outbox trabalha em ciclos. Em cada ciclo, ele reserva um lote de até 100 mensagens
pendentes (`Outbox:BatchSize`) e publica uma por uma; cada mensagem tem até 5 s para o broker
confirmar (`Outbox:PublishTimeoutMs`). Se o lote inteiro foi publicado, provavelmente há mais
mensagens esperando, e o publisher busca o próximo lote na hora; senão, espera 500 ms
(`Outbox:PollIntervalMs`) antes do próximo ciclo. Na primeira falha de conexão ou de tempo
esgotado, o publisher interrompe o lote: as mensagens restantes ficam intactas para o próximo
ciclo, em vez de cada uma gastar até 5 s numa conexão que vai falhar.

### Objetivos de recuperação

As metas vêm do NFR-014 (requisito: metas de recuperação, em
[`docs/01-requisitos-e-assumptions.md`](01-requisitos-e-assumptions.md#requisitos-não-funcionais)):
no banco de Lançamentos, RPO (Recovery Point Objective, perda máxima aceitável de dados) igual a
zero e RTO (Recovery Time Objective, tempo máximo para voltar a funcionar) de até 15 minutos; no
Consolidado, RTO de até 60 minutos, porque perder os totais não é perder dinheiro — eles são
recalculados a partir dos lançamentos. Como cada componente cumpre essas metas na Arquitetura Alvo
está em [`docs/06-arquitetura-alvo.md` §5](06-arquitetura-alvo.md). Os backups devem ser
criptografados, testados com uma restauração de verdade e monitorados pela idade do último
backup. O procedimento de operação (runbook) de cada falha está em
[`docs/07-runbooks.md`](07-runbooks.md).

## 3. Observabilidade

### Logs estruturados

Os dois serviços escrevem logs em JSON, um objeto por linha, com o instante em UTC (`Timestamp`,
com milissegundos, por exemplo `2026-10-21T14:56:39.652Z`), o nível (`LogLevel`), a origem
(`Category`), a mensagem (`Message`) e o contexto da operação em que a linha foi escrita
(`Scopes`). Como toda linha começa pelo `Timestamp`, ordenar as linhas de texto (`sort`) as põe em
ordem de tempo, mesmo misturando os dois serviços.

| Onde o log é escrito | Campos de contexto |
|---|---|
| Uma requisição HTTP, nos dois serviços | `service`, `environment`, `correlationId`, `traceId` |
| O publisher do Outbox, em Lançamentos | `service`, `environment`, `eventId`, `launchId`, `correlationId` |
| O consumidor, no Consolidado | `service`, `environment`, `eventId`, `launchId`, `correlationId`, `retryCount` (quantas vezes a mensagem já voltou de uma fila de espera) |

Os logs nunca registram tokens, segredos nem o conteúdo financeiro completo da requisição.

### Métricas essenciais

| Métrica | Para que serve |
|---|---|
| `launch_requests_total{operation,outcome}` | Conta os registros (`operation="record"`); `outcome` é `success` (sucesso), `validation` (requisição inválida), `conflict` (conflito de idempotência), `error` ou `rate_limited` (recusado pelo limitador de requisições, `429`). Uma consulta de lançamento recusada pelo limitador também é contada, com `operation="get"`; as consultas atendidas não entram nesta métrica. |
| `launch_request_duration_ms` | Tempo de cada registro; dele saem o p95 e o p99, os valores abaixo dos quais ficam 95% e 99% das medições. |
| `outbox_pending_count` e `outbox_oldest_age_seconds` | Quantas mensagens esperam no Outbox e há quanto tempo espera a mais antiga: o risco de algo não ser publicado. |
| `outbox_published_total` e `outbox_publish_batch_duration_ms` | Quantas mensagens foram publicadas e quanto dura cada ciclo do publisher: reservar um lote de mensagens, publicá-las e confirmar no banco (ADR-003). |
| `broker_publish_failures_total` | Falhas ao publicar no RabbitMQ, por motivo (`reason`): a saúde da integração. |
| `consumer_events_total{outcome}` | Eventos tratados pelo consumidor, por resultado: `processed` (aplicado), `duplicate` (cópia descartada), `rejected` (inválido, foi para a DLQ), `retry_transient` (falha temporária), `retry_unexpected` (falha inesperada, ainda com tentativas) e `dead_lettered` (foi para a DLQ depois das tentativas). |
| `consumer_lag_seconds` | Atraso de cada evento, do registro à aplicação (`applied_at − recorded_at`). É a medida do NFR-005 (requisito: o Consolidado fica no máximo alguns segundos atrás dos lançamentos). |
| `consolidation_requests_total{outcome}` | Consultas ao Consolidado; `outcome` é `success`, `validation`, `unavailable` (banco fora do ar), `error` ou `rate_limited` (recusada pelo limitador de requisições, `429`). |
| `consolidation_query_duration_ms` | Tempo de cada consulta, com p95 e p99. |
| `dlq_messages_total` | Mensagens enviadas à DLQ. |

A reconstrução do Consolidado (`scripts/ops/rebuild-consolidation.sh`) é um script de operação, e
não código do serviço; por isso não há métrica de duração no Prometheus. Para medir quanto ela
demora, basta cronometrar o script, por exemplo com
`time scripts/ops/rebuild-consolidation.sh --yes`.

Durante uma reconstrução — ou no fim do teste de carga de consulta, que reconstrói o Consolidado
—, `consumer_lag_seconds` sobe para a idade dos eventos reaplicados, e os alertas de atraso podem
disparar sem que haja defeito; como reconhecer e tratar o caso está em
[`docs/07-runbooks.md` §5](07-runbooks.md#5-totais-divergentes-conferir-reenviar-a-dlq-reconstruir).

### Como seguir uma operação pelos dois serviços (`correlationId` e `traceId`)

Dois identificadores ligam os logs de uma operação:

- **`correlationId`** acompanha um lançamento do começo ao fim, atravessando os dois serviços e o
  broker. Vem do cabeçalho `X-Correlation-Id` enviado pelo cliente; se o cliente não enviar,
  Lançamentos gera um. O valor volta ao cliente no cabeçalho da resposta e no campo
  `correlationId` do lançamento, fica gravado na tabela `launches`, viaja no corpo do evento e no
  cabeçalho AMQP (Advanced Message Queuing Protocol, o protocolo do RabbitMQ) `x-correlation-id`, e
  aparece nos logs do publisher e do consumidor. As cópias enviadas às filas de espera e à DLQ
  mantêm o mesmo `correlationId`, no cabeçalho e no corpo, porque o consumidor copia o cabeçalho
  original em vez de criar outro.
- **`traceId`** identifica uma requisição HTTP dentro de um serviço. É o identificador do padrão
  de rastreamento do W3C (World Wide Web Consortium, o órgão de padrões da web), chamado Trace
  Context, que o framework web do .NET (ASP.NET Core) cria para cada requisição; na falta dele, o
  serviço usa o identificador interno da requisição (`HttpContext.TraceIdentifier`). O `traceId`
  aparece nos logs da requisição e no corpo de toda resposta de erro. Ele não atravessa o broker:
  o publisher e o consumidor trabalham fora de uma requisição HTTP e usam o `correlationId`.

**Exemplo: investigar um lançamento que não apareceu no saldo.** Um caixa registrou uma venda de
R$ 125,00 com o cabeçalho `X-Correlation-Id: caixa2-pedido-123`. Se o cliente não enviou o
cabeçalho, o valor gerado está no cabeçalho da resposta e no campo `correlationId` do lançamento.

1. Procure o valor nos logs dos dois serviços, em ordem de tempo:

   ```bash
   docker compose logs --no-log-prefix launchservice consolidationservice | grep caixa2-pedido-123 | sort
   ```

   `--no-log-prefix` tira o nome do container do começo de cada linha; assim, a linha começa pelo
   `Timestamp`, e o `sort` põe as linhas dos dois serviços em ordem de tempo.

2. Leia as linhas de cima para baixo. Sem falha, elas mostram três momentos. Abaixo, a saída real
   do ambiente local, resumida aos campos que importam:

   | `Timestamp` (UTC) | Serviço e momento | O que a linha mostra | Campos de contexto |
   |---|---|---|---|
   | `2026-10-21T15:26:09.679Z` a `.687Z` | `LaunchService`, requisição HTTP | A busca pela `Idempotency-Key` e, em seguida, o lançamento e a mensagem do Outbox gravados no banco (`INSERT INTO launches …`, `INSERT INTO outbox_messages …`) | `correlationId: caixa2-pedido-123`, `traceId: 0d83d4f5…` |
   | `2026-10-21T15:26:09.968Z` | `LaunchService`, publisher do Outbox | "Evento d3f4f3e5… (lançamento 0c744aa6…) publicado no broker." | `correlationId`, `eventId: d3f4f3e5…`, `launchId: 0c744aa6…` |
   | `2026-10-21T15:26:10.006Z` a `.009Z` | `ConsolidationService`, consumidor | A Inbox, os totais do dia e os dados do último evento gravados (`INSERT INTO inbox_messages …`, `INSERT INTO daily_consolidations …`, `INSERT INTO projection_metadata …`) | `correlationId`, o mesmo `eventId` e o mesmo `launchId`, `retryCount: 0` |

   Neste exemplo, entre gravar o lançamento (`09.687Z`) e somá-lo aos totais do dia (`10.008Z`),
   passaram cerca de 0,3 s.

   No ambiente local (`Development`), os comandos SQL aparecem no log, sem os valores dos
   parâmetros; é por eles que a requisição e o consumo ficam visíveis quando nada falha.

3. Veja onde a história para:
   - Só as linhas da requisição, ou um aviso "Falha ao publicar evento … nova tentativa agendada":
     o lançamento está gravado, mas ainda não foi publicado — o broker pode estar fora do ar.
     Confira `outbox_oldest_age_seconds` e o alerta `OutboxStale` (`docs/07-runbooks.md` §1).
   - Publicado, mas sem linha do Consolidado: o evento está na fila, e o Consolidado está parado
     ou atrasado (`docs/07-runbooks.md` §2).
   - Linhas do Consolidado com o aviso "Falha transitória processando evento … (tentativa N)" e
     `retryCount` crescendo: o banco do Consolidado está com problema, e a mensagem está voltando
     das filas de espera (`docs/07-runbooks.md` §3).
   - Aviso "… enviando à DLQ" ou "Evento inválido … enviado à DLQ": a mensagem está na DLQ, com o
     motivo no cabeçalho `x-failure-reason` e o mesmo `x-correlation-id` (`docs/07-runbooks.md`
     §4).

4. Se o cliente recebeu um erro, use o `traceId` do corpo da resposta: ele leva às linhas daquela
   requisição no serviço que respondeu.

### Alertas

Nove regras de alerta rodam no Prometheus (`observability/prometheus/alerts.yml`, carregado pela
opção `rule_files`). Elas aparecem, pendentes ou disparadas, em http://localhost:9090/alerts.

| Alerta | Dispara quando | O que indica |
|---|---|---|
| `OutboxStale` | `outbox_oldest_age_seconds > 60` por 2 min | Uma mensagem espera mais de 1 minuto para ser publicada; o broker pode estar fora do ar. |
| `ProjectionLagHigh` | p95 de `consumer_lag_seconds` acima de 5 s por 5 min (NFR-005) | O Consolidado está mais atrasado que a meta. |
| `DeadLetterQueueNotEmpty` | DLQ com alguma mensagem por 1 min | Há uma mensagem que não pôde ser processada. |
| `ConsolidationBacklogGrowing` | Fila principal do Consolidado crescendo por 5 min | O consumidor não está dando conta das mensagens. |
| `LaunchServiceErrors` | Taxa de `launch_requests_total{outcome="error"}` acima de zero por 5 min | Erros inesperados no registro. |
| `TargetDown` | `up == 0` por 1 min, em qualquer um dos 3 alvos que o Prometheus consulta (os dois serviços e o RabbitMQ) | Um deles não responde ao Prometheus. |
| `ConsolidationServiceErrors` | Mais de 5% das consultas com `outcome="error"` por 5 min (NFR-C, requisito do PDF: no máximo 5% de perda de requisições, lido como em A-004) | Falhas na consulta acima do limite do PDF. |
| `LaunchLatencyHigh` | p95 de `launch_request_duration_ms` acima de 300 ms por 10 min (NFR-013, metas de tempo de resposta) | Registro mais lento que a meta. |
| `ConsolidationLatencyHigh` | p95 de `consolidation_query_duration_ms` acima de 500 ms por 10 min (NFR-013) | Consulta mais lenta que a meta. |

Essas regras só tornam o problema visível no Prometheus. Enviar a notificação — por e-mail ou
Slack, com o Alertmanager — é uma evolução, não implementada nesta entrega: alguém ainda precisa
olhar `/alerts` ou o painel.

O rastreamento distribuído completo — exportar o caminho de cada requisição por todos os serviços
para uma ferramenta como Jaeger ou Tempo — também é uma evolução, descrita no ADR-010 (decisão
sobre observabilidade). Esta entrega usa o rastreamento nativo do ASP.NET Core só como fonte do
`traceId`, sem exportar nada e sem container novo. O mínimo implementado são as métricas
essenciais, os logs estruturados com correlação e as nove regras de alerta acima.

## 4. Capacidade, custos e transição

### Perfil de capacidade

- Consulta: 50 requisições por segundo no pico, com pelo menos 95% de sucesso no teste exigido.
- Escrita assumida: 50 lançamentos por segundo durante 15 minutos (A-013, premissa sobre o pico de
  escrita); 10 vezes esse volume é um cenário avaliado só no papel.
- A primeira forma de crescer é horizontal, com mais cópias das APIs e dos processos em segundo
  plano. A chave primária por data da tabela de totais (`daily_consolidations`) mantém a consulta
  diária rápida.
- Se a consulta de períodos longos passar da meta, acrescentar saldos gravados em datas fixas
  (snapshots) ou réplicas de leitura (read replicas, cópias do banco que só atendem consultas),
  sem mudar a fonte da verdade.
- Se aparecerem reprocessamento contínuo e muitos consumidores, reavaliar o RabbitMQ diante do
  Kafka (um log de eventos distribuído, que guarda as mensagens para serem relidas). Não antecipar
  essa complexidade sem evidência.

O raciocínio completo de capacidade — sinais para criar mais cópias, como frear quem envia mais
do que o consumidor processa (backpressure), preparação para picos conhecidos e os números de
`docs/evidence/load/` — está em
[`docs/06-arquitetura-alvo.md` §6](06-arquitetura-alvo.md#6-escalabilidade).

### Principais fontes de custo

1. Alta disponibilidade, IOPS (operações de leitura e escrita em disco por segundo), backup e
   retenção do banco de Lançamentos.
2. Capacidade e retenção do broker e da DLQ.
3. Cópias das APIs e dos processos em segundo plano, e o tráfego de rede.
4. Armazenamento e retenção de logs, métricas e rastreamento.
5. Provedor de identidade e gateway, conforme o ambiente da empresa.

O Docker Compose mantém barato rodar a demonstração localmente. Em produção, PostgreSQL, broker e
observabilidade gerenciados por um provedor de nuvem podem valer a pena por reduzir a operação,
mesmo custando mais. Um modelo ilustrativo de custos — provedor e região de referência,
dimensionamento por componente, cenários base, de crescimento e de pico, ambientes e licenças —
está em [`docs/08-custos.md`](08-custos.md). Provedor e região são uma premissa explícita, não uma
decisão de arquitetura; o ambiente real da empresa segue em aberto (OQ-001, questão aberta: qual
é o ambiente de destino).

### Estratégia de transição condicional

O desafio não traz um sistema legado (A-007, premissa: não há informações sobre um sistema
legado); por isso, a estratégia é condicional, e não um plano real de migração. O diagrama, os
critérios para virar a chave (cutover) e para voltar atrás (rollback) e o passo a passo — mapear,
copiar o histórico (backfill), publicar, ler em paralelo para comparar (leitura sombra), trocar os
consumidores e desligar o legado — estão em
[`docs/06-arquitetura-alvo.md` §10](06-arquitetura-alvo.md#10-arquitetura-de-transição-df-01).
Este documento fica com o que vale sempre, e não com um cenário condicional de migração.

## 5. Critérios de prontidão operacional

Antes de dizer que o sistema está pronto para produção:

- As verificações de saúde separam "o processo está vivo" (`/health/live`) de "está pronto para
  receber requisições" (`/health/ready`), e cada uma testa só as dependências do próprio serviço.
- Os painéis mostram as metas — os SLOs (Service Level Objectives, metas internas de nível de
  serviço) — das APIs, do Outbox, das mensagens acumuladas, da DLQ e do atraso do Consolidado.
- Há procedimento documentado para reenviar a DLQ e para reconstruir o Consolidado.
- Backups e restauração têm evidência de que funcionam, e não só configuração.
- Os testes de carga e de falha rodam antes de qualquer afirmação de que os requisitos não
  funcionais foram atendidos.
- As premissas de custo, de ambiente e de retenção continuam visíveis, para serem validadas.
