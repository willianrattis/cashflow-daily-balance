# Diagrama de sequência — timeout e nova tentativa sem duplicar

O cliente registra um lançamento, mas a resposta se perde na rede. Ele tenta de novo com a mesma
`Idempotency-Key` e recebe o lançamento original, sem criar um segundo. Se tentar com a mesma
chave e outro conteúdo, recebe `409`. Exemplo: a chave `venda-0001` foi usada para R$ 100,00 a
crédito; repetir com R$ 100,00 devolve o mesmo lançamento; repetir com R$ 150,00 devolve `409`.

```mermaid
sequenceDiagram
    autonumber
    actor Caller as Cliente
    participant API as CashFlowEntryService (API)
    participant DB as Cash Flow Entry DB

    Caller->>API: POST do lançamento + Idempotency-Key K
    API->>DB: BEGIN
    API->>DB: INSERT do lançamento + mensagem no Outbox
    DB-->>API: COMMIT confirmado
    Note over API,Caller: a resposta se perde por timeout de rede

    Caller->>API: nova tentativa com a mesma chave K
    API->>DB: busca pela chave K
    DB-->>API: lançamento existente + fingerprint igual
    API-->>Caller: 200 OK + Idempotency-Replayed: true

    Caller->>API: nova tentativa com K e conteúdo diferente
    API->>DB: busca pela chave K e compara o fingerprint
    DB-->>API: fingerprint diferente
    API-->>Caller: 409 idempotency_payload_mismatch
```

Quem protege contra duas requisições simultâneas com a mesma chave são as restrições do banco —
regras que o próprio PostgreSQL aplica a toda gravação, aqui o índice único em `idempotency_key`.
Uma consulta antes de gravar, sem essa restrição, não bastaria: as duas requisições poderiam
consultar ao mesmo tempo, não encontrar nada e gravar as duas
([ADR-004](../adr/ADR-004-processamento-idempotente.md)).
