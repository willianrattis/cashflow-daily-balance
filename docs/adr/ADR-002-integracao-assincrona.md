# ADR-002 — Integração assíncrona entre Lançamentos e Consolidado

- **Status:** Aceita
- **Data:** 2026-10-19

## Contexto

O NFR-A (requisito não funcional do PDF: "O serviço de controle de lançamento não deve ficar
indisponível se o sistema de consolidado diário cair") impede que o registro dependa do
Consolidado. Se Lançamentos chamasse o Consolidado a cada registro e esperasse a resposta, uma
queda do Consolidado derrubaria o registro junto. Já o Consolidado pode ficar alguns segundos
atrás, mas precisa dar conta das mensagens acumuladas (o backlog) quando volta de uma parada.

## Decisão

Decidimos integrar os dois serviços por mensagens assíncronas, através de um broker (servidor
que guarda as mensagens e as entrega ao destino), porque assim Lançamentos não espera o
Consolidado: grava, deixa a mensagem para entrega e responde ao cliente, mesmo com o
Consolidado fora do ar.

- Lançamentos publica o evento `CashFlowEntryRecorded.v1` ("lançamento registrado", versão 1). O
  Consolidado o lê de uma fila durável, que sobrevive a um reinício do broker.
- O broker é o RabbitMQ, que oferece de forma simples o que precisamos: filas, confirmação de
  processamento, novas tentativas e DLQ (Dead Letter Queue, fila das mensagens que não puderam
  ser processadas). Trocar de produto não muda o formato do evento.
- A entrega é "pelo menos uma vez" (at-least-once): nada se perde, mas a mesma mensagem pode
  chegar duas vezes. Por isso o Consolidado ignora cópias repetidas (ADR-004) e só confirma o
  recebimento — o ACK (acknowledgement) — depois de gravar o evento na Inbox (tabela dos
  eventos já aplicados) e somar o valor ao dia.

## Alternativas consideradas

- **Mensagens assíncronas por um broker, com eventos duráveis (escolhida).** Ganhamos o
  isolamento de falhas que o NFR-A exige, novas tentativas automáticas, uma DLQ e mensagens que
  esperam na fila enquanto o Consolidado está parado.
- **Chamada HTTP síncrona.** A cada registro, Lançamentos chama uma API REST (Representational
  State Transfer, o estilo comum de API sobre HTTP) do Consolidado e espera a resposta. Ganharíamos um
  Consolidado sempre atualizado e nenhum broker para operar. Descartamos porque a
  disponibilidade e a latência de um serviço passariam a depender do outro: se o Consolidado
  caísse, o registro cairia junto — o que o NFR-A proíbe. Seria a escolha certa se as duas
  partes pudessem cair juntas.
- **Log de eventos distribuído** (por exemplo, Kafka: os eventos ficam gravados em ordem por
  muito tempo, e cada consumidor pode reler o passado). Ganharíamos reprocessamento contínuo e
  muitos consumidores independentes. Descartamos porque o desafio não pede nada disso e a
  operação é mais cara que a de uma fila (cluster, partições, retenção). Seria a escolha certa
  com grande volume, vários consumidores do mesmo evento ou reprocessamento frequente.

## Consequências

**Positivas:**

- Bom, porque Lançamentos continua aceitando registros com o Consolidado ou o broker fora do ar
  (NFR-A).
- Bom, porque fila, novas tentativas e DLQ tornam as falhas visíveis e tratáveis.
- Bom, porque quem publica e quem consome escalam e são implantados separadamente.

**Negativas:**

- Ruim, porque a consulta pode estar alguns segundos atrás (consistência eventual, ADR-005).
- Ruim, porque é preciso monitorar atraso, tamanho da fila, novas tentativas e DLQ (ADR-010).
- Ruim, porque o formato do evento precisa de versão e de compatibilidade entre versões.

## Trade-offs

Aceitamos que uma mensagem possa chegar duplicada para nunca perder um evento. A alternativa,
"no máximo uma vez" (at-most-once), perderia o evento na primeira falha de rede e violaria a
garantia de que nenhum lançamento confirmado se perde. O que torna a troca segura é o
Consolidado descartar a cópia repetida pela Inbox (ADR-004).

## Failure modes

- **Broker fora do ar:** a mensagem espera no Outbox (tabela de Lançamentos com as mensagens a
  publicar) e o publisher tenta de novo (ADR-003).
- **Consolidado fora do ar:** as mensagens esperam na fila, sem bloquear o registro.
- **Mensagem inválida (formato ou versão desconhecidos):** vai direto para a DLQ e gera alerta;
  tentar de novo não resolveria.
- **Falha temporária de infraestrutura no Consolidado** (conexão caiu, tempo esgotado, banco
  reiniciando): nunca vai para a DLQ, por mais longa que seja a falha. A mensagem sobe os níveis
  de espera (5 s, depois 30 s) e então fica no último, de 120 s, tentando de novo a cada 120 s
  até dar certo (ADR-011).
- **Falha inesperada no Consolidado** (um bug, ou um formato que o código não reconhece): três
  novas tentativas, uma em cada fila de espera, e depois a DLQ.
- **Mensagem entregue de novo:** a Inbox e as restrições de unicidade do banco impedem somar o
  valor duas vezes (ADR-004).
- **Reenvio da DLQ:** `scripts/ops/replay-dlq.sh` devolve as mensagens à fila principal com o
  shovel dinâmico do RabbitMQ (uma transferência entre filas feita pelo próprio broker). Uma
  mensagem inválida por natureza volta para a DLQ; a correção é no código ou em quem a produziu
  (`docs/07-runbooks.md` §4).

## Quando revisitar

Se o volume exigir reprocessamento contínuo, retenção longa ou muitos consumidores competindo
por partes da mesma fila, comparar o RabbitMQ com um log de eventos como o Kafka
(`docs/05-aspectos-transversais.md` §4). Se três filas de espera não bastarem para separar
falha temporária de falha inesperada, revisitar a escada de novas tentativas (ADR-011).

## Rastreabilidade

FR-001, FR-003 e FR-007 (registrar, consultar por intervalo e reconstruir o Consolidado);
NFR-001 (o Consolidado fora do ar não derruba Lançamentos; refina o NFR-A); NFR-002 (50
requisições por segundo no Consolidado); NFR-005 (Consolidado até alguns segundos atrás);
NFR-010 (nenhuma mensagem pendente expira sem aviso); NFR-014 (metas de tempo de recuperação).
Texto completo em [`docs/01-requisitos-e-assumptions.md`](../01-requisitos-e-assumptions.md);
formato do evento no [contrato do evento](../03-contratos.md#evento-entryrecordedv1).
