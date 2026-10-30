# ADR-003 — Transactional Outbox para publicação confiável

- **Status:** Aceita
- **Data:** 2026-10-19

## Contexto

O lançamento e o evento que avisa o Consolidado precisam existir juntos. Mas o banco e o broker
(o RabbitMQ, servidor que entrega as mensagens ao Consolidado) são sistemas separados, sem uma
transação comum. Se Lançamentos gravasse e publicasse logo depois, uma queda entre os dois
passos deixaria um lançamento sem evento, que o Consolidado nunca somaria — violando o NFR-004
(requisito nosso: um lançamento confirmado ao cliente nunca pode se perder). O mecanismo também
precisa funcionar com várias cópias de Lançamentos ao mesmo tempo e não pode deixar linhas do
banco travadas por minutos quando o broker estiver fora.

## Decisão

Decidimos gravar o lançamento e o evento na mesma transação do banco de Lançamentos, numa
tabela chamada Outbox, e publicar depois, porque assim o próprio banco garante que os dois
existem juntos ou nenhum existe, sem uma transação que inclua o broker.

1. **Registro.** Lançamentos grava a linha em `cash_flow_entries` e o evento `CashFlowEntryRecorded.v1` em
   `outbox_messages` na mesma transação, e só responde sucesso depois que ela é confirmada.
2. **Publicação.** O publisher do Outbox roda em segundo plano dentro do próprio Lançamentos
   (um `BackgroundService`, ADR-011). A cada ciclo, lê um lote de até 100 mensagens pendentes
   (ou que falharam e já podem ser tentadas de novo), publica cada uma e marca o resultado.
3. **Confirmação.** Só marca uma mensagem como enviada depois que o RabbitMQ responde que a
   aceitou (recurso chamado publisher confirms).
4. **Mensagem sem destino.** Se nenhuma fila estiver ligada ao destino (por exemplo, por erro
   de configuração), o RabbitMQ descarta a mensagem — e mesmo assim responde "recebi". O
   publisher marcaria como enviada uma mensagem perdida. Por isso publicamos com
   `mandatory: true`: nesse caso o RabbitMQ devolve a mensagem, e o publisher a marca como falha
   e tenta de novo.
5. **Novas tentativas.** A espera entre tentativas cresce — 2 s, 4 s, 8 s… até 5 minutos — com
   um acréscimo aleatório de até 1 s (backoff com jitter).
6. **Várias cópias.** Cada cópia do publisher reserva um lote de mensagens que nenhuma outra
   cópia pode pegar ao mesmo tempo; as outras pulam essas linhas (no PostgreSQL,
   `SELECT … FOR UPDATE SKIP LOCKED`). Não é preciso coordenação externa.
7. **Guarda permanente.** As mensagens publicadas continuam no Outbox, sem expiração (A-012,
   premissa: o Outbox publicado é guardado enquanto for a fonte da reconstrução). É a partir
   delas que `scripts/ops/rebuild-consolidation.sh` reconstrói o Consolidado do zero, sem
   depender de o broker guardar mensagens.

Detalhes em `OutboxPublisherService`, `RabbitMqEventPublisher` e
`docs/05-aspectos-transversais.md`: lote, ritmo dos ciclos e tempos-limite na §2; métricas na §3.

## Alternativas consideradas

- **Transactional Outbox (escolhida).** Ganhamos a garantia do NFR-004 usando só o banco que já
  temos, com um publisher que tem novas tentativas e métricas próprias.
- **Publicar logo depois de gravar** (confirmar a transação e publicar, na mesma requisição). É
  o mais simples. Descartamos porque uma queda entre os dois passos deixa um lançamento sem
  evento — a perda que o NFR-004 proíbe. Seria aceitável se perder um evento de vez em quando
  não tivesse consequência.
- **Transação distribuída com o broker** (2PC, Two-Phase Commit ou confirmação em duas fases:
  um coordenador pergunta ao banco e ao broker se podem confirmar, e só confirma se os dois
  disserem sim). Ganharíamos gravação e publicação atômicas, sem duplicatas. Descartamos porque
  o registro passaria a travar sempre que o broker não respondesse, e o RabbitMQ não oferece
  esse protocolo. Seria a escolha certa entre sistemas que suportam 2PC, quando uma duplicata é
  inaceitável.
- **Captura de mudanças no banco** — CDC (Change Data Capture: uma ferramenta, como o Debezium,
  que lê o log de transações do PostgreSQL e transforma cada lançamento gravado em evento).
  Ganharíamos a publicação sem código no serviço. Descartamos porque exige operar mais uma peça
  de infraestrutura, desnecessária a 50 lançamentos por segundo. Seria a escolha certa em
  escala maior, ou para publicar mudanças de sistemas que não podemos alterar.

## Consequências

**Positivas:**

- Bom, porque a perda máxima de lançamentos confirmados — o RPO (Recovery Point Objective) — é
  zero, desde que o banco esteja disponível e com backup (NFR-004).
- Bom, porque várias cópias de Lançamentos podem publicar contra o mesmo banco; é o caminho de
  escala previsto em `docs/diagrams/svg/Deployment-Alvo.svg`.
- Bom, porque o Outbox também é a fonte da reconstrução do Consolidado (A-012).

**Negativas:**

- Ruim, porque o mesmo evento pode ser publicado mais de uma vez (ver Trade-offs); o
  Consolidado descarta a cópia (ADR-004, decisão sobre idempotência).
- Ruim, porque a tabela do Outbox cresce sem limite. Uma limpeza periódica é evolução futura e
  exigiria antes mudar a fonte da reconstrução para `cash_flow_entries` (A-012).
- Ruim, porque, quando o registro responde, o Consolidado pode ainda não ter somado o
  lançamento — a consistência eventual, escolhida de propósito (ADR-005).

## Trade-offs

No lugar de uma transação distribuída, aceitamos que o mesmo evento possa ser publicado duas
vezes. Exemplo: o publisher envia o evento de um lançamento de R$ 100,00 e o broker confirma,
mas o processo cai antes de marcar a linha como enviada; ao voltar, envia o mesmo evento de
novo. O mesmo acontece se a confirmação passar do tempo-limite de 5 s: o publisher trata como
falha e publica de novo, mesmo que o broker tenha recebido a primeira cópia. Isso só é seguro
porque o Consolidado descarta a cópia repetida pelo `eventId` e pelo `entryId` antes de
confirmar o recebimento (ADR-004); sem esse descarte, os R$ 100,00 seriam somados duas vezes.

Aceitamos também interromper o lote inteiro na primeira falha de conexão com o broker. Enquanto
o publisher trabalha num lote, as linhas reservadas ficam travadas no banco (`FOR UPDATE`). Com
o broker fora do ar, se cada uma das 100 linhas esperasse seus 5 s de tempo-limite, as travas
durariam até cerca de 8 minutos; parando na primeira falha, são liberadas em segundos. O custo:
as linhas que não chegaram a ser tentadas ficam para o ciclo seguinte.

## Failure modes

- **Queda depois de confirmar a transação:** o publisher encontra a mensagem pendente no
  próximo ciclo.
- **Queda depois de publicar e antes de marcar como enviada:** a mensagem é publicada de novo e
  o Consolidado descarta a cópia (ADR-004).
- **Broker fora do ar:** a mensagem continua no Outbox e é tentada de novo, com espera
  crescente; o lote para na primeira falha de conexão.
- **Nenhuma fila ligada ao destino** (primeira inicialização, broker recriado): a devolução por
  `mandatory: true` vira uma falha tratada (item 4 da Decisão). O publisher não cria a fila do
  Consolidado para resolver isso: amarraria a configuração de Lançamentos à de outro serviço. A
  fila é criada e ligada só pelo Consolidado.
- **Outbox parado:** a métrica `outbox_oldest_age_seconds` (idade da mensagem pendente mais
  antiga) dispara o alerta `OutboxStale` acima de 60 s por 2 minutos (ADR-010); o procedimento
  está em `docs/07-runbooks.md`.

## Quando revisitar

Mover o publisher para um processo separado se a carga dele divergir muito da carga da API
(ADR-011). Criar uma limpeza do Outbox publicado se o crescimento da tabela pressionar o
armazenamento — mudando antes a fonte da reconstrução para `cash_flow_entries`.

## Rastreabilidade

NFR-001 (o Consolidado fora do ar não derruba Lançamentos); NFR-004; NFR-009 e FR-007 (o
Consolidado pode ser reconstruído); NFR-010 (nenhuma mensagem pendente expira sem aviso); A-009
(lançamento e Outbox na mesma transação); A-012; A-013 (pico de 50 lançamentos por segundo
durante 15 minutos); OBJ-03 (critério de avaliação do PDF: escalabilidade). Decisões
relacionadas: [ADR-002](ADR-002-integracao-assincrona.md),
[ADR-004](ADR-004-processamento-idempotente.md), [ADR-011](ADR-011-topologia-de-processos.md).
