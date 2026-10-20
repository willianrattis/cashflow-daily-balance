# Diagrama de sequência — Consolidado fora do ar e recuperação

O Consolidado está fora do ar, e um lançamento chega. Lançamentos registra e responde `201`
normalmente; a mensagem é publicada e espera na fila do RabbitMQ. Quando o Consolidado volta, ele
processa as mensagens acumuladas e os totais do dia se atualizam.

```mermaid
sequenceDiagram
    autonumber
    actor Caller as Cliente
    participant API as LaunchService (API)
    participant LDB as Launch DB
    participant Publisher as LaunchService (publisher do Outbox)
    participant Broker as RabbitMQ
    participant Worker as ConsolidationService (consumidor)
    participant CDB as Consolidation DB

    Note over Worker,CDB: ConsolidationService fora do ar
    Caller->>API: POST do lançamento
    API->>LDB: commit do lançamento + mensagem no Outbox
    LDB-->>API: sucesso
    API-->>Caller: 201 Created

    Publisher->>LDB: lê as mensagens pendentes do Outbox
    Publisher->>Broker: publica o evento
    Broker-->>Publisher: aceito na fila durável
    Note over Broker: a mensagem espera na fila até ser confirmada (ACK)

    Note over Worker,CDB: serviço de volta
    Broker->>Worker: entrega as mensagens acumuladas
    Worker->>CDB: grava a Inbox + os totais do dia
    CDB-->>Worker: commit
    Worker-->>Broker: ACK
    Note over Worker,CDB: fila esvaziada e atraso do Consolidado de volta à meta
```

Uma falha no consumidor ou no banco do Consolidado nunca desfaz um lançamento já confirmado. Para
acompanhar uma queda, o monitoramento olha a idade da mensagem mais antiga, o tamanho da fila, a
DLQ (Dead Letter Queue, fila das mensagens que não puderam ser processadas) e o atraso
`consumer_lag_seconds` — e não a resposta de uma consulta isolada, que não mostra quantas
mensagens esperam ([ADR-005](../adr/ADR-005-eventual-consistency.md), decisão sobre a consistência
eventual). O que fazer em cada caso está em [`docs/07-runbooks.md` §2](../07-runbooks.md).
