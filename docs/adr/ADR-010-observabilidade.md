# ADR-010 — Observabilidade: métricas com OpenTelemetry, Prometheus e Grafana, logs em JSON e alertas

- **Status:** Aceita
- **Data:** 2026-10-19

## Contexto

Monitoramento e observabilidade são um diferencial pedido pelo PDF (DF-03, diferencial do PDF:
"Monitoramento e Observabilidade"). Além disso, dois comportamentos centrais só aparecem com o
sistema rodando, não no código: o atraso do Consolidado em relação aos Lançamentos (ADR-005) e
a saúde do Outbox e das filas (ADR-003). Sem métricas e logs que possam ser ligados entre si, um
operador não distingue "o Consolidado está um pouco atrasado" de "o consumidor está parado há
uma hora".

## Decisão

Decidimos instrumentar os serviços com OpenTelemetry, coletar as métricas com Prometheus,
mostrá-las num painel do Grafana e escrever logs em JSON, porque essa combinação roda inteira no
`docker compose`, sem custo e sem segredo, e já mede o que importa: atraso, mensagens acumuladas
e erros.

- **Métricas.** O OpenTelemetry (padrão aberto de instrumentação) conta eventos e mede tempos do
  domínio — por exemplo, mensagens publicadas, falhas de publicação e atraso do consumidor (lista
  completa em `docs/05-aspectos-transversais.md` §3). Cada serviço as expõe em `/metrics`; o
  RabbitMQ expõe as suas pelo plugin `rabbitmq_prometheus`. O Prometheus lê os três endereços a
  cada 5 s.
- **Logs em JSON** (`AddJsonConsole` com `IncludeScopes: true`). Cada linha de log de uma
  requisição ou de uma mensagem processada traz o `correlationId`, o serviço e o ambiente; as
  linhas de requisições HTTP trazem também o `traceId`. Exemplo: um lançamento enviado com o
  cabeçalho `X-Correlation-Id: c-123` gera logs com `c-123` em Lançamentos; o valor segue no
  evento e no cabeçalho da mensagem e aparece de novo nos logs do Consolidado. Procurar `c-123`
  nos dois serviços mostra o caminho inteiro daquele lançamento.
- **Painel.** Um painel do Grafana ("CashFlow — visão operacional") é criado automaticamente,
  com acesso anônimo somente leitura, sem login.
- **Alertas.** Nove regras no Prometheus, conferidas contra os nomes reais das métricas:
  `OutboxStale` (mensagem parada no Outbox), `ProjectionLagHigh` (Consolidado atrasado),
  `DeadLetterQueueNotEmpty` (mensagem na Dead Letter Queue, a fila das mensagens que não
  puderam ser processadas), `ConsolidationBacklogGrowing` (fila do Consolidado crescendo),
  `CashFlowEntryServiceErrors` e `ConsolidationServiceErrors` (erros nas APIs), `TargetDown` (um dos
  três endereços não responde), `CashFlowEntryLatencyHigh` e `ConsolidationLatencyHigh` (APIs acima da
  meta de tempo de resposta).
- **Verificações de saúde.** `/health/live` diz se o processo está vivo. `/health/ready` testa só
  o banco do próprio serviço — nunca o do outro nem o broker, para que a falha deles não tire o
  serviço de circulação (NFR-001, requisito: o Consolidado fora do ar não derruba Lançamentos).
  `/health/broker` testa o RabbitMQ, só para informação, com o resultado guardado por 10 s.

## Alternativas consideradas

- **OpenTelemetry, Prometheus e Grafana (escolhida).** Ganhamos observabilidade completa de
  métricas e alertas rodando localmente, sem custo nem credenciais.
- **APM (Application Performance Monitoring) gerenciado** (produto pago que reúne métricas, logs
  e rastreamento, como Datadog, New Relic ou Application Insights). Ganharíamos tudo isso com
  pouca configuração. Descartamos porque depende de um SaaS (Software as a Service, software
  contratado como serviço na internet) pago e de credenciais externas, o que impede rodar
  localmente sem custo e publicar o repositório sem segredos. Continua uma opção razoável para
  a Arquitetura Alvo (DF-02, diferencial do PDF: estimativa de custos).
- **ELK (Elasticsearch, Logstash e Kibana)**, um conjunto de ferramentas para centralizar e
  pesquisar logs. Ganharíamos busca madura de logs. Descartamos porque só o Elasticsearch é mais
  pesado para rodar localmente que todo o resto somado — desproporcional ao volume de logs desta
  demonstração. Seria a escolha certa com grande volume de logs e uma equipe para operá-lo.
- **Nenhum painel (só `docker compose logs`).** Custo zero. Descartamos porque não deixa ver
  mensagens acumuladas, atraso e alertas ao longo do tempo. Não há cenário em produção em que
  isso bastaria.

## Consequências

**Positivas:**

- Bom, porque a observabilidade funciona 100% local, sem custo e sem segredo.
- Bom, porque métricas do domínio (não só de infraestrutura) tornam o atraso e o acúmulo de
  mensagens medidos, e não deduzidos por sintoma.
- Bom, porque logs em JSON podem ser lidos por qualquer coletor sem mudar a instrumentação.

**Negativas:**

- Ruim, porque, sem rastreamento distribuído completo (o trace visual de uma requisição através
  dos serviços), investigar uma falha que passa por vários componentes depende de ler logs.
- Ruim, porque, sem o Alertmanager (componente que avisa alguém quando um alerta dispara),
  perceber um incidente depende de alguém olhar o Prometheus ou o Grafana, ou de rodar
  `scripts/ops/reconcile.sh`.
- Ruim, porque o Prometheus local não tem retenção longa nem HA (High Availability, alta
  disponibilidade): é observabilidade de demonstração, não de produção.

## Trade-offs

Aceitamos não ter rastreamento distribuído completo nem aviso automático (Alertmanager) nesta
entrega, em troca de custo zero e nenhum segredo. Os dois ficam registrados como evolução
explícita, não como lacuna escondida: hoje, a ligação entre os serviços é feita pelo
`correlationId` nos logs, e as nove regras deixam o problema visível em `/alerts`, mas ninguém
é avisado automaticamente.

## Failure modes

- **Prometheus fora do ar:** as métricas deixam de ser coletadas, mas os serviços continuam
  funcionando normalmente; a observabilidade nunca está no caminho do registro.
- **Plugin de métricas do RabbitMQ desligado:** os painéis do broker ficam vazios; o broker
  funciona normalmente.
- **Limitador de requisições recusando muitas chamadas (`429`):** cada recusa entra na métrica de
  requisições do serviço (`cashflow_entry_requests_total` ou `consolidation_requests_total`) com
  `outcome="rate_limited"`, aparece no painel de requisições por resultado e gera uma linha de log
  de aviso (`Warning`) com o `correlationId` e o `sub` do chamador. Não há alerta dedicado porque
  `429` é a resposta esperada sob abuso, não um erro inesperado.

## Quando revisitar

Se uma operação 24 horas por dia exigir que alguém seja acionado automaticamente (Alertmanager),
ou se uma falha precisar ser investigada através de vários serviços a partir de uma única
requisição (rastreamento completo, com um backend como Jaeger ou Tempo).

## Rastreabilidade

DF-03; OBJ-04 (critério de avaliação do PDF: resiliência e monitoramento proativo); OBJ-08
(critério de avaliação do PDF: métricas e metas claras); NFR-005 (o Consolidado fica no máximo
alguns segundos atrás); [ADR-002](ADR-002-integracao-assincrona.md);
[docs/05-aspectos-transversais.md](../05-aspectos-transversais.md) §3.
