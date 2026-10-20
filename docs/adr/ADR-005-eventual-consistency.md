# ADR-005 — Consolidado como totais por dia, atualizados a cada evento (consistência eventual)

- **Status:** Aceita
- **Data:** 2026-10-19

## Contexto

O PDF pede "um relatório que disponibilize o saldo diário consolidado" (BR-02, requisito de
negócio do PDF). O Consolidado não pode travar o registro (NFR-A, requisito não funcional do PDF:
Lançamentos não pode cair se o Consolidado cair), mas pode ficar alguns segundos atrás. A tabela
de lançamentos é a fonte da verdade; o Consolidado é uma visão calculada a partir dela, para
responder rápido a consultas por período. Um total único do período não bastaria: o relatório
precisa do saldo de cada dia.

## Decisão

Decidimos manter, no banco do Consolidado, uma tabela com os totais de cada dia, atualizada a
cada evento recebido, porque assim a consulta lê totais prontos, sem recalcular o histórico, e o
registro nunca espera por ela.

- **A tabela.** `daily_consolidations` tem uma linha por dia (`business_date`), com créditos,
  débitos e variação do dia. É uma projeção: dado derivado dos lançamentos, que pode ser apagado
  e recalculado.
- **A atualização.** Cada evento soma seu valor à linha do dia: um crédito de R$ 100,00 em
  15/03/2025 soma 10000 centavos aos créditos de 15/03/2025, criando a linha se preciso. Como só
  há somas, a ordem de chegada não muda o resultado. A Inbox (tabela dos eventos já aplicados)
  garante que cada evento é somado uma vez só (ADR-004).
- **A consulta.** `GET /consolidation?fromDate=…&toDate=…` retorna, para o intervalo, o saldo de
  abertura, os créditos, os débitos, a variação e o saldo de fechamento; e a lista `days[]`, com
  esses cinco valores para cada dia, inclusive os sem movimento. O cálculo fica em
  `ConsolidationCalculator`, código de domínio com testes unitários. O intervalo vai até 366 dias
  (A-015, premissa sobre o limite da consulta); regras em
  [`docs/03-contratos.md`](../03-contratos.md#consultar-consolidado-por-intervalo).
- **Leitura coerente.** As leituras de uma consulta rodam numa única transação
  `REPEATABLE READ` e enxergam a mesma fotografia dos dados. Sem isso, o saldo de abertura
  poderia vir de um momento e os totais do período, de outro.
- **Quão atualizado está.** Toda resposta traz `asOfUtc` (última atualização da tabela),
  `lastAppliedEventRecordedAtUtc` (quando foi registrado o último lançamento somado) e
  `lastAppliedLagSeconds` (quanto ele levou para ser somado); definições em
  [`docs/03-contratos.md`](../03-contratos.md#como-medir-o-atraso-do-consolidado-nfr-005).
- **Reconstrução.** `scripts/ops/rebuild-consolidation.sh`, rodado à mão, apaga a tabela e a
  recalcula republicando os eventos guardados no banco de Lançamentos (o Outbox, ADR-003).

## Alternativas consideradas

- **Totais por dia, atualizados a cada evento (escolhida).** Consultas rápidas — p95 (o valor
  abaixo do qual ficam 95% das medições) de 6,1 ms a 50 requisições por segundo
  (`docs/evidence/load/2026-10-19-consolidation.md`) — sem pôr o Consolidado no caminho do
  registro.
- **Saldo calculado na hora, dentro de Lançamentos** (somar a tabela `launches` a cada
  consulta). Ganharíamos um saldo sempre atual. Descartamos porque misturaria leitura e escrita
  no mesmo serviço e banco, e uma consulta pesada poderia afetar o registro — o que o NFR-A
  proíbe. Seria a escolha certa com pouco volume e sem exigência de isolar falhas.
- **Recalcular todo o histórico a cada consulta** (somar todos os lançamentos recebidos a cada
  pedido). Simples de entender. Descartamos porque o tempo da consulta cresceria com o histórico,
  sem limite, e não atenderia à latência sob carga. Seria aceitável com pouco histórico e pouco
  acesso.
- **Event Sourcing** (guardar apenas os eventos e calcular o estado a partir deles). Descartamos
  porque é mais complexo do que precisamos: os lançamentos nunca mudam e já servem de base para
  reconstruir os totais (ADR-001). Seria a escolha certa se o negócio precisasse ver o estado em
  qualquer momento do passado.

## Consequências

**Positivas:**

- Bom, porque as consultas leem totais prontos, sem recalcular o histórico.
- Bom, porque os totais podem ser reconstruídos sem depender de o broker guardar mensagens.
- Bom, porque `days[]` entrega o saldo de cada dia, como pede o BR-02, numa única chamada.

**Negativas:**

- Ruim, porque o usuário pode ver um saldo alguns segundos atrasado; o atraso é medido e
  monitorado (ADR-010).
- Ruim, porque perder a tabela não perde dinheiro, mas exige reconstruí-la: o RTO (Recovery Time
  Objective, tempo máximo para voltar a funcionar) do Consolidado é de até 60 minutos.

## Trade-offs

Aceitamos que a consulta fique alguns segundos atrás do registro — metas iniciais: p95 até 5 s
e p99 (o valor abaixo do qual ficam 99% das medições) até 30 s — para nunca pôr o registro no
caminho da consulta. Calcular na hora resolveria o atraso, mas traria de volta o acoplamento de
disponibilidade que o ADR-001 (decisão sobre dois serviços independentes) rejeita.

## Failure modes

- Se a soma de um evento falhar, a mensagem não recebe o ACK (acknowledgement, confirmação de
  recebimento) e vai para nova tentativa ou para a DLQ (Dead Letter Queue, fila das mensagens que
  não puderam ser processadas). Os lançamentos não são afetados.
- Um consumidor parado aparece em `lastAppliedLagSeconds` e nos alertas, não na falta de vendas:
  a métrica mede o tempo entre registrar e somar (`applied_at − recorded_at`), e um dia sem
  vendas não é atraso.

## Quando revisitar

Se a consulta de períodos longos passar da meta de latência mesmo com os totais prontos, há duas
saídas que não mudam a fonte da verdade:

- **Saldo gravado em datas fixas (snapshot, uma fotografia do saldo em um momento),** por exemplo
  no fechamento de cada mês. Hoje o saldo de abertura soma todos os dias desde o primeiro
  lançamento; com a fotografia, somaria só os dias depois dela.
- **Réplicas de leitura (read replicas):** cópias do banco do Consolidado, atualizadas
  continuamente, que só atendem consultas e dividem a carga com o banco principal (ADR-008,
  caminho de escala do PostgreSQL).

Se o RTO de 60 minutos deixar de ser aceitável, revisitar a forma de reconstrução.

## Rastreabilidade

FR-003 (consultar por intervalo); FR-006 (período sem movimento retorna zeros); FR-007 e NFR-009
(o Consolidado pode ser reconstruído); BR-02; NFR-001 (o Consolidado fora do ar não derruba
Lançamentos); NFR-005 (Consolidado até alguns segundos atrás); A-015; [contrato do
Consolidado](../03-contratos.md#consolidationservice-api).
