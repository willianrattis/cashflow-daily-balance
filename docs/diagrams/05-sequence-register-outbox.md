# Diagrama de sequência — registro com Outbox

O fluxo normal, sem falhas, de um lançamento: o cliente registra, Lançamentos grava o lançamento
e a mensagem a publicar na mesma transação (o Outbox), o publisher envia a mensagem ao RabbitMQ, e
o consumidor do Consolidado soma o lançamento aos totais do dia. A resposta ao cliente sai antes
de o Consolidado receber o evento: é por isso que o registro não depende do Consolidado estar no
ar.

```mermaid
sequenceDiagram
    autonumber
    actor Caller as Cliente
    participant API as CashFlowEntryService (API)
    participant Handler as RecordEntry handler
    participant DB as Cash Flow Entry DB
    participant Publisher as CashFlowEntryService (publisher do Outbox)
    participant Broker as RabbitMQ
    participant Worker as ConsolidationService (consumidor)
    participant CDB as Consolidation DB

    Caller->>API: POST do lançamento + Idempotency-Key
    API->>Handler: valida o token e o pedido
    Handler->>DB: SELECT do lançamento pela Idempotency-Key (fora de qualquer transação)
    alt chave já usada
        DB-->>Handler: lançamento existente
        Handler-->>API: 200 + Idempotency-Replayed (ou 409 se o fingerprint for diferente)
    else chave nova
        Handler->>DB: INSERT do lançamento + INSERT da mensagem no Outbox (um SaveChanges, uma transação)
        alt corrida: outra requisição com a mesma chave gravou primeiro
            DB-->>Handler: violação do índice único → trata como repetição (200) ou conflito (409)
        else
            DB-->>Handler: commit confirmado
            Handler-->>API: entryId + 201 Created
        end
    end
    API-->>Caller: lançamento gravado

    Publisher->>DB: BEGIN + reserva um lote de mensagens pendentes (SELECT ... FOR UPDATE SKIP LOCKED)
    Publisher->>Broker: publica CashFlowEntryRecorded.v1 (x-retry-count=0, x-correlation-id)
    Broker-->>Publisher: publicação confirmada (mandatory + publisher confirms)
    Publisher->>DB: marca a mensagem como Published + COMMIT (libera as linhas reservadas)

    Broker->>Worker: entrega o evento
    Worker->>CDB: BEGIN + INSERT na Inbox (EventId e EntryId)
    alt EventId ou EntryId já está na Inbox (reentrega)
        CDB-->>Worker: violação da chave primária (EventId) ou do índice único (EntryId)
        Worker->>CDB: ROLLBACK (nada é gravado) → evento ignorado, sem efeito (Duplicate)
    else primeira vez
        Worker->>CDB: inserir ou atualizar o total do dia (upsert) + inserir ou atualizar projection_metadata
        CDB-->>Worker: totais do dia gravados
        Worker->>CDB: COMMIT
    end
    Worker-->>Broker: ACK
```

Como ler os passos:

- **Repetição e corrida (passos 3 a 9).** A busca pela `Idempotency-Key` acontece **antes** de
  qualquer transação, só para responder rápido a uma repetição. Quem impede o lançamento duplicado
  é o índice único em `idempotency_key`: se duas requisições com a mesma chave chegarem juntas, a
  segunda recebe o erro de violação (código `23505` do PostgreSQL) e é tratada como repetição ou
  conflito. O fingerprint é o resumo do valor, da natureza e da descrição; se ele mudar com a
  mesma chave, a resposta é `409` ([ADR-004](../adr/ADR-004-processamento-idempotente.md)).
- **Publicação (passos 11 a 14).** Cada cópia do publisher reserva um lote de mensagens pendentes
  que nenhuma outra cópia pode pegar ao mesmo tempo. Só marca a mensagem como publicada depois de
  o RabbitMQ confirmar que a recebeu e a entregou a uma fila (`mandatory` e publisher confirms,
  [ADR-003](../adr/ADR-003-transactional-outbox.md)).
- **Consumo (passos 15 a 22).** A Inbox, a tabela dos eventos já aplicados, e os totais do dia
  são gravados na mesma transação. O ACK (acknowledgement, a confirmação de recebimento ao broker)
  só sai depois do commit. Um evento entregue de novo esbarra na chave primária (`event_id`) ou no
  índice único (`entry_id`) da Inbox: a transação é desfeita (`ROLLBACK`), nada é somado, e o ACK
  sai do mesmo jeito, porque o evento já estava aplicado.
