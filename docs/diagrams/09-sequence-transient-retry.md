# Diagrama de sequência — falha temporária: o consumidor nunca para e nunca manda à DLQ (Dead Letter Queue)

Dois casos de indisponibilidade que o consumidor e o publisher resolvem sozinhos, sem intervenção
manual e sem perder nenhum evento. Eles são diferentes da falha **inesperada**
([08-sequence-dlq-replay.md](08-sequence-dlq-replay.md)), que tem um número máximo de tentativas
e termina na DLQ, a fila das mensagens que não puderam ser processadas, e do consumidor **parado**
([07-sequence-outage-recovery.md](07-sequence-outage-recovery.md)), quando nenhuma entrega
acontece.

## 1. Banco do Consolidado fora do ar — o consumidor continua ligado e as reentregas continuam

Com o banco do Consolidado (`Consolidation DB`) fora do ar, cada tentativa falha. A mensagem passa
pelas filas de espera — `retry.1`, `retry.2` e `retry.3`, com TTL (Time To Live, o tempo que a
mensagem espera antes de voltar à fila principal) de 5 s, 30 s e 120 s — e, a partir da última,
volta para ela de novo, enquanto a queda durar. Quando o banco volta, a próxima tentativa funciona.

```mermaid
sequenceDiagram
    autonumber
    participant Broker as RabbitMQ
    participant Worker as ConsolidationService (consumidor)
    participant CDB as Consolidation DB
    participant Retry as Fila de espera retry.N (TTL)

    Note over CDB: Consolidation DB fora do ar

    Broker->>Worker: entrega o evento (x-retry-count=0)
    Worker->>CDB: BEGIN + soma aos totais do dia
    CDB-->>Worker: falha temporária (conexão ou timeout)
    Note over Worker: ConsumerFailureClassifier → Transient (temporária)
    Worker->>Retry: publica uma cópia em retry.1 (x-retry-count=1)
    Retry-->>Worker: publicação confirmada
    Worker-->>Broker: ACK da entrega original
    Note over Retry: o TTL (5 s) acaba
    Retry->>Broker: devolve à fila principal

    Broker->>Worker: reentrega (x-retry-count=1)
    Worker->>CDB: soma aos totais do dia
    CDB-->>Worker: falha temporária de novo
    Worker->>Retry: publica uma cópia em retry.2 (x-retry-count=2)
    Worker-->>Broker: ACK
    Note over Retry: o TTL (30 s) acaba
    Retry->>Broker: devolve à fila principal

    Broker->>Worker: reentrega (x-retry-count=2)
    Worker->>CDB: soma aos totais do dia
    CDB-->>Worker: falha temporária de novo
    Worker->>Retry: publica uma cópia em retry.3 (x-retry-count=3)
    Worker-->>Broker: ACK
    Note over Retry: o TTL (120 s) acaba
    Retry->>Broker: devolve à fila principal

    loop Enquanto o Consolidation DB estiver fora (nunca vai para a DLQ)
        Broker->>Worker: reentrega (x-retry-count de 3 em diante)
        Worker->>CDB: soma aos totais do dia
        CDB-->>Worker: falha temporária de novo
        Worker->>Retry: publica uma cópia em retry.3 (x-retry-count + 1, sempre o último nível)
        Worker-->>Broker: ACK
        Note over Retry: o TTL (120 s) acaba
        Retry->>Broker: devolve à fila principal
    end

    Note over CDB: Consolidation DB de volta
    Broker->>Worker: reentrega
    Worker->>CDB: BEGIN + soma aos totais do dia
    CDB-->>Worker: commit
    Worker-->>Broker: ACK
    Note over Worker,CDB: as mensagens acumuladas são processadas e consumer_lag_seconds volta à meta
```

**O que conta como falha temporária.** O classificador de falhas
(`ConsumerFailureClassifier.Classify`) trata como temporária uma falha de infraestrutura que pode
passar sozinha:

- um erro do PostgreSQL de conexão (`SqlState` da classe `08`) ou de falta de recursos (classe
  `53`);
- o servidor desligando ou ainda sem aceitar conexões (`57P01`, `57P02`, `57P03`), uma consulta
  cancelada por tempo (`57014`), uma linha travada por outra transação (`55P03`), um conflito
  entre transações ou um impasse entre elas, o deadlock (`40001`, `40P01`);
- uma exceção que o driver do PostgreSQL (Npgsql) marca como temporária, ou um
  `TimeoutException`, `SocketException` ou `IOException`.

Qualquer outro erro é inesperado e segue o diagrama 08. Uma falha temporária nunca avança para a
DLQ: quando o contador de tentativas (`retryCount`) chega ao último nível (`retry.3`), cada nova
tentativa volta a esse mesmo nível, sem limite, até o banco voltar. É o comportamento decidido no
ADR-002 (decisão sobre a integração assíncrona, seção "Failure modes") e detalhado no ADR-011
(decisão sobre a topologia de processos, seção "Decisão"), e não um defeito.

## 2. Broker fora do ar — o Outbox espera cada vez mais e interrompe o lote

Com o RabbitMQ fora do ar, o publisher do Outbox não consegue publicar. Na primeira falha de
conexão, ele marca aquela mensagem para tentar mais tarde e interrompe o lote: as outras mensagens
reservadas nem são tentadas, porque também falhariam. A espera entre as tentativas dobra a cada
vez que a mesma mensagem falha (backoff), com um tempo aleatório somado (jitter), para que as
mensagens não tentem todas no mesmo instante. Cada mensagem tem a própria espera: uma que nunca foi
tentada continua `Pending` e entra no ciclo seguinte.

```mermaid
sequenceDiagram
    autonumber
    participant Publisher as CashFlowEntryService (publisher do Outbox)
    participant LDB as Cash Flow Entry DB
    participant Broker as RabbitMQ

    Note over Broker: RabbitMQ fora do ar

    Publisher->>LDB: BEGIN + reserva um lote de mensagens pendentes (FOR UPDATE SKIP LOCKED)
    Publisher->>Broker: tenta publicar o evento 1 do lote
    Broker--xPublisher: falha de conexão (estoura Outbox:PublishTimeoutMs)
    Note over Publisher: reason=connection, espera = 2 s · 2^tentativas (até 5 min) + jitter
    Publisher->>LDB: marca o evento 1 como Failed, next_attempt_at_utc = agora + espera
    Note over Publisher: interrompe o lote no primeiro erro de conexão: os eventos 2..N nem são tentados
    Publisher->>LDB: COMMIT (libera as linhas não tentadas)

    Note over Publisher: ciclo seguinte (500 ms depois), ainda sem broker
    Publisher->>LDB: BEGIN + reserva um lote: os eventos 2..N, ainda Pending (o evento 1 fica de fora até a espera dele acabar)
    Publisher->>Broker: tenta publicar o evento 2
    Broker--xPublisher: falha de conexão de novo
    Publisher->>LDB: marca o evento 2 como Failed (primeira falha dele: espera de 2 s) + interrompe o lote + COMMIT

    Note over Publisher: a espera do evento 1 acabou, broker ainda fora
    Publisher->>LDB: BEGIN + reserva um lote que inclui o evento 1
    Publisher->>Broker: tenta publicar o evento 1
    Broker--xPublisher: falha de conexão
    Publisher->>LDB: marca o evento 1 como Failed, com a espera dobrada (segunda falha dele: 4 s) + COMMIT

    Note over Broker: RabbitMQ de volta
    Publisher->>LDB: BEGIN + reserva um lote (as Failed cuja espera acabou + as Pending)
    Publisher->>Broker: publica
    Broker-->>Publisher: publicação confirmada (mandatory + publisher confirms)
    Publisher->>LDB: marca Published + COMMIT
    Note over Publisher,Broker: nenhum lançamento aceito durante a queda se perdeu, só atrasou
```

Lançamentos nunca deixa de aceitar registros durante a queda do broker (NFR-001, requisito: o
Consolidado fora do ar não derruba Lançamentos; o ADR-002 estende a garantia ao broker). O
publisher do Outbox é uma tarefa em segundo plano (`BackgroundService`), separada do caminho das
requisições HTTP: o `POST /entries` continua respondendo `201` normalmente, e só a publicação
fica para depois.
