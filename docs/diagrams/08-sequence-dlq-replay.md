# Diagrama de sequência — falha inesperada no consumidor: filas de espera, DLQ (Dead Letter Queue) e reenvio

Quando o consumidor do Consolidado falha de um jeito inesperado — por exemplo, um defeito no
código —, a mensagem não é perdida nem tentada sem fim. Ela passa por três filas de espera
(`retry.1`, `retry.2` e `retry.3`). Em cada uma, espera um tempo — o TTL (Time To Live, tempo de
vida), de 5 s, 30 s e 120 s — e o RabbitMQ a devolve à fila principal para nova tentativa. Se
falhar nas três, vai para a DLQ, a fila das mensagens que não puderam ser processadas. Depois de
corrigida a causa, um operador a reenvia com `scripts/ops/replay-dlq.sh`.

```mermaid
sequenceDiagram
    autonumber
    participant Broker as RabbitMQ
    participant Worker as ConsolidationService (consumidor)
    participant CDB as Consolidation DB
    participant Retry as Fila de espera retry.N (TTL)
    participant DLQ as DLQ
    participant Op as Operador

    Broker->>Worker: entrega o evento (x-retry-count=0)
    Worker->>CDB: BEGIN + INSERT na Inbox + soma aos totais do dia
    CDB-->>Worker: falha inesperada (não temporária)
    Worker->>Retry: publica uma cópia em retry.1 (x-retry-count=1)
    Retry-->>Worker: publicação confirmada
    Worker-->>Broker: ACK da entrega original (a cópia já está confirmada)
    Note over Retry: o TTL acaba e a mensagem volta à fila principal
    Retry->>Broker: devolve à fila principal

    Broker->>Worker: reentrega (x-retry-count=1)
    Worker->>CDB: soma aos totais do dia
    CDB-->>Worker: falha inesperada de novo
    Worker->>Retry: publica uma cópia em retry.2 (x-retry-count=2)
    Retry-->>Worker: publicação confirmada
    Worker-->>Broker: ACK da entrega original (a cópia já está confirmada)
    Note over Retry: o TTL acaba
    Retry->>Broker: devolve à fila principal

    Broker->>Worker: reentrega (x-retry-count=2)
    Worker->>CDB: soma aos totais do dia
    CDB-->>Worker: falha inesperada de novo
    Worker->>Retry: publica uma cópia em retry.3 (x-retry-count=3)
    Retry-->>Worker: publicação confirmada
    Worker-->>Broker: ACK da entrega original (a cópia já está confirmada)
    Note over Retry: o TTL acaba
    Retry->>Broker: devolve à fila principal

    Broker->>Worker: reentrega (x-retry-count=3, última tentativa)
    Worker->>CDB: soma aos totais do dia
    CDB-->>Worker: falha inesperada de novo
    Worker->>DLQ: publica com x-failure-reason (tentativas esgotadas)
    DLQ-->>Worker: publicação confirmada
    Worker-->>Broker: ACK da entrega original (a cópia já está confirmada)

    Note over Op,DLQ: investigação manual e correção do defeito
    Op->>DLQ: scripts/ops/replay-dlq.sh (shovel dinâmico)
    DLQ->>Broker: move as mensagens capturadas para a fila principal
    Broker->>Worker: reentrega (sem x-retry-count, conta como primeira tentativa)
    Worker->>CDB: INSERT na Inbox
    alt EventId ou LaunchId já está na Inbox
        CDB-->>Worker: violação da restrição UNIQUE → evento ignorado, sem efeito (Duplicate)
    else primeira vez
        Worker->>CDB: soma aos totais do dia
        CDB-->>Worker: sucesso
    end
    Worker-->>Broker: ACK
```

Três situações chegam a este fluxo, e nenhuma é uma falha temporária — essa nunca vai para a DLQ
([09-sequence-transient-retry.md](09-sequence-transient-retry.md) §1):

- **Falha inesperada** — qualquer erro que o classificador de falhas
  (`ConsumerFailureClassifier.Classify`) não reconhece como temporário (a lista está no
  diagrama 09). Passa por `retry.1`, `retry.2` e `retry.3`, e só então vai para a DLQ. É o fluxo
  desenhado acima.
- **Mensagem inválida** — JSON malformado, ou formato e versão que a validação recusa antes de
  tentar somar. Vai direto para a DLQ, sem passar pelas filas de espera: ela nunca teria sucesso
  numa nova tentativa.
- **Mensagem repetida** — o mesmo `EventId` ou `LaunchId` já está na Inbox. Não é uma falha: o
  consumidor ignora a cópia, confirma (ACK) normalmente e nunca chega às filas de espera.

Cada cópia publicada numa fila de espera ou na DLQ é confirmada pelo broker (publisher confirms)
antes do ACK da entrega original. Se a publicação da cópia falhar, a entrega original é devolvida
à fila (NACK com `requeue: true`, o contrário do ACK) e nunca se perde em silêncio.

O reenvio (`scripts/ops/replay-dlq.sh`) usa um shovel dinâmico, recurso do RabbitMQ que move as
mensagens da DLQ de volta para a fila principal. A Inbox — `EventId` e `LaunchId`,
[ADR-004](../adr/ADR-004-processamento-idempotente.md) — garante que uma mensagem já somada antes
seja ignorada, sem efeito. Uma mensagem permanentemente inválida volta para a DLQ, como na
primeira vez: é o esperado, e não uma falha do reenvio.
