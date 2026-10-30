# ADR-011 — Topologia de processos: publisher e consumidor no mesmo processo da API, não separados

- **Status:** Aceita
- **Data:** 2026-10-19

## Contexto

Lançamentos precisa publicar o evento `CashFlowEntryRecorded.v1` depois de gravar (ADR-003, decisão
sobre o Outbox); o Consolidado precisa consumi-lo e manter a consulta no ar (ADR-005). Nenhum
desses dois trabalhos em segundo plano precisa, obrigatoriamente, rodar no mesmo processo da API
do seu serviço. Onde cada um roda é uma decisão de topologia, separada da decisão de dividir o
domínio em dois serviços.

## Decisão

Decidimos rodar o publisher do Outbox dentro do processo de Lançamentos e o consumidor dentro do
processo do Consolidado, ao lado da API de consulta, porque assim há dois programas para
implantar, e não quatro, sem perder o isolamento entre os serviços que o NFR-001 (requisito: o
Consolidado fora do ar não derruba Lançamentos) exige.

- `OutboxPublisherService` e `CashFlowEntryRecordedConsumerService` são `BackgroundService`s: tarefas
  contínuas do .NET que rodam no mesmo processo da API.
- Quando o consumidor não consegue processar uma mensagem, ele a coloca numa fila de espera.
  Cada fila de espera tem um TTL (Time To Live, tempo de vida): quando o tempo acaba, o próprio
  RabbitMQ devolve a mensagem à fila principal, e ela é tentada de novo. Há três níveis, com
  esperas de 5 s, 30 s e 120 s (`RabbitMq:RetryDelaysMs`; mecânica em
  `docs/05-aspectos-transversais.md` §2).
- O tipo de falha decide o caminho (ADR-002). Uma falha temporária (banco ou rede fora do ar)
  sobe os níveis (5 s, depois 30 s) e então fica no último, de 120 s, tentando de novo a cada
  120 s até dar certo; nunca vai para a DLQ (Dead Letter Queue, fila das mensagens que não
  puderam ser processadas). Uma falha inesperada (um bug) passa pelos três níveis e depois vai
  para a DLQ.

**Trade-off aceito:** dois programas, e não quatro, ao custo de uma falha compartilhada dentro de
cada serviço: um consumidor que cai e reinicia sem parar (crash loop) derruba também a API de
consulta do mesmo serviço, e vice-versa. Aceitamos porque:

1. Lançamentos nunca é afetado por uma queda do consumidor do Consolidado. O requisito mais
   rígido do desafio (NFR-001) é entre *serviços*, não entre a API e o trabalho em segundo plano
   do mesmo serviço.
2. Uma queda do consumidor já é visível e recuperável sem perda (o Outbox é a fonte da
   reconstrução, A-012).
3. O publisher funciona com várias cópias (ADR-003), e várias cópias do consumidor dividem a
   mesma fila: escalar o serviço já escala o trabalho em segundo plano junto.

## Alternativas consideradas

- **No mesmo processo da API do próprio serviço (escolhida).** Ganhamos metade dos programas
  para implantar, sem perder o isolamento entre Lançamentos e Consolidado.
- **Processo separado para o publisher e para o consumidor** (quatro programas). Ganharíamos
  isolamento de falhas entre a API e o trabalho em segundo plano, e escala e implantação
  independentes. Descartamos por ora porque dobra o número de programas sem um motivo concreto:
  no volume assumido (A-013, premissa: pico de 50 lançamentos por segundo durante 15 minutos), a
  carga do publisher e do consumidor não difere o bastante da carga das APIs. Seria a escolha
  certa quando um deles precisar de recursos ou de um ritmo de implantação diferentes dos da API.
- **Um único processo de segundo plano para os dois serviços.** Economizaria um programa.
  Descartamos sem ressalva: seria um processo com acesso aos dois bancos, quebrando a separação
  de dados do ADR-001 e do ADR-006 (decisão sobre isolamento de dados).
- **Plugin de mensagens atrasadas do RabbitMQ** (delayed-message: entrega a mensagem só depois de
  um tempo, numa única fila). Resolveria as esperas com uma fila só. Descartamos porque é um
  plugin de terceiros que não vem na imagem oficial; as três filas com TTL resolvem o mesmo
  problema com o que o broker já oferece. Seria a escolha certa se o número de níveis crescesse
  muito.
- **Novas tentativas dentro do processo, sem voltar ao broker** (por exemplo, com a biblioteca
  Polly). Simples de escrever. Descartamos porque a mensagem ficaria presa ao consumidor durante
  a espera, ocupando uma das 10 vagas de mensagens não confirmadas que ele pode ter ao mesmo
  tempo (o prefetch), e a nova tentativa se perderia se o processo reiniciasse no meio da
  espera. Seria aceitável para esperas curtíssimas, sem risco de reinício.

## Consequências

**Positivas:**

- Bom, porque temos metade dos programas de uma topologia totalmente separada, sem abrir mão do
  isolamento entre Lançamentos e Consolidado.
- Bom, porque a nova tentativa sobrevive a um reinício do consumidor: o estado fica no broker,
  não na memória.
- Bom, porque separar depois é simples: mover o trabalho em segundo plano para um processo
  próprio não muda o evento nem a lógica do domínio, só onde o código roda.

**Negativas:**

- Ruim, porque um bug no consumidor pode degradar a API de consulta do mesmo serviço (mesmo
  processo, mesma memória, mesmas threads).
- Ruim, porque as esperas e o número de níveis ficam gravados nas filas quando elas são criadas
  (o TTL de cada fila de espera). Reiniciar o consumidor com valores novos não basta: o RabbitMQ
  recusa recriar uma fila com outro TTL (`inequivalent arg 'x-message-ttl'`), e o consumidor fica
  sem consumir. É preciso parar o consumidor, apagar as filas de espera vazias e religá-lo
  (`docs/07-runbooks.md` §8).

## Trade-offs

Ver "Trade-off aceito" na Decisão: é o trade-off central desta decisão, por isso está junto
dela, e não repetido aqui.

## Failure modes

- **Consumidor em crash loop:** a API de consulta do mesmo processo cai junto. Mitigação: o
  container reinicia sozinho, e a verificação de prontidão (`/health/ready`) nunca depende do
  broker.
- **Publisher do Outbox em crash loop:** mesma mitigação, em Lançamentos. O registro só para se o
  processo inteiro cair, não por uma falha isolada do publisher: as exceções dele são capturadas
  e registradas no log, sem derrubar o processo.
- **Falha temporária mais longa do que os três níveis cobrem** (por exemplo, o banco do
  Consolidado fora do ar por uma hora): depois de subir os dois primeiros níveis, a mensagem volta
  ao último nível a cada 120 s enquanto a falha durar, em vez de ir para a DLQ.

## Quando revisitar

Separar o publisher e o consumidor em programas próprios quando os recursos que usam divergirem
muito dos da API, quando for preciso implantá-los em ritmo diferente, ou quando a queda da API
junto com o consumidor deixar de ser aceitável para o SLA (Service Level Agreement, compromisso
de disponibilidade com o cliente) daquele serviço. Revisitar as filas de espera se o número de
níveis precisar mudar sem reiniciar o consumidor, ou se o volume de novas tentativas justificar o
plugin de mensagens atrasadas.

## Rastreabilidade

OBJ-02 (critério de avaliação do PDF: processos e responsabilidades isolados ou integrados);
OBJ-03 (critério de avaliação do PDF: escalabilidade); NFR-001; NFR-005 (o Consolidado fica no
máximo alguns segundos atrás); NFR-010 (nenhuma mensagem pendente expira sem aviso). Decisões
relacionadas: [ADR-001](ADR-001-estilo-arquitetural.md),
[ADR-002](ADR-002-integracao-assincrona.md), [ADR-003](ADR-003-transactional-outbox.md).
Diagramas: `docs/diagrams/structurizr/workspace.dsl` (as visões de containers e de implantação,
da entrega e do alvo, mostram os dois pares no mesmo processo, nunca quatro programas) e
`docs/diagrams/08-sequence-dlq-replay.md`.
