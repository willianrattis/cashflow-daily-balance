# ADR-008 — PostgreSQL como banco relacional dos dois serviços

- **Status:** Aceita
- **Data:** 2026-10-19

## Contexto

Os dois serviços precisam de mais do que "guardar dados":

- Lançamentos precisa gravar o lançamento e a mensagem a publicar (o Outbox) como uma decisão
  única, tudo ou nada (ADR-003, decisão sobre o Outbox).
- Os dois precisam impedir duplicatas — pela `Idempotency-Key` no registro e pelo `eventId` no
  consumo — mesmo com requisições simultâneas, sem depender de uma consulta prévia do código
  (ADR-004, decisão sobre idempotência).
- O Consolidado precisa somar valores na linha de cada dia com segurança, mesmo com eventos
  chegando fora de ordem ou entregues de novo.

## Decisão

Usamos PostgreSQL nos dois serviços porque ele garante, no próprio banco, o que a solução
precisa: gravar o lançamento e a mensagem juntos (tudo ou nada), impedir lançamentos duplicados
e permitir que duas cópias do publisher trabalhem ao mesmo tempo sem pegar a mesma mensagem.
Cada serviço tem o próprio banco e o próprio usuário de banco, sem privilégio de administrador
(ADR-006), e nenhum compartilha tabelas. O acesso é feito pelo Entity Framework Core (EF Core)
com o driver Npgsql.

Os mecanismos, um por um:

- **Tudo ou nada.** O lançamento e a mensagem do Outbox são gravados na mesma transação local;
  se qualquer parte falhar, nada é gravado. São as garantias ACID (Atomicidade, Consistência,
  Isolamento e Durabilidade) de uma transação de banco.
- **Sem duplicatas.** Uma restrição `UNIQUE` na `Idempotency-Key` (e, no Consolidado, a chave
  primária `event_id` e uma restrição `UNIQUE` no `launch_id` da Inbox) faz o banco aceitar só a
  primeira de duas gravações simultâneas;
  a segunda recebe um erro, e o código responde com o resultado da primeira (ADR-004).
- **Regras de domínio no banco.** Restrições `CHECK` valem mesmo que o código tenha um bug.
  Exemplo: um lançamento de valor zero ou negativo é recusado pelo banco
  (`amount_in_cents > 0`), e a natureza só pode ser `Credit` ou `Debit`.
- **Soma segura no mesmo dia.** Cada evento soma seu valor à linha do dia com um único comando,
  que cria a linha se ela não existe ou soma à existente — inserir ou atualizar (upsert); no
  PostgreSQL, `INSERT … ON CONFLICT … DO UPDATE`. Duas mensagens do mesmo dia processadas ao
  mesmo tempo não perdem nenhuma soma.
- **Várias cópias do publisher.** Cada cópia reserva um lote de mensagens que nenhuma outra
  pode pegar ao mesmo tempo (no PostgreSQL, `SELECT … FOR UPDATE SKIP LOCKED`), sem
  coordenação externa (ADR-003).
- **Lançamento imutável.** Um trigger (código que o banco executa automaticamente a cada
  gravação) recusa qualquer `UPDATE` ou `DELETE` em `launches`. Assim, a regra de que um
  lançamento nunca muda (INV-001, invariante: lançamento confirmado é imutável e não pode ser
  excluído) não depende só de a API não oferecer `PUT` ou `DELETE`.

## Alternativas consideradas

- **PostgreSQL, banco relacional de código aberto (escolhida).** Todos os mecanismos acima, sem
  custo de licença, igual no `docker compose` e em serviços gerenciados.
- **Banco de documentos** (por exemplo, MongoDB, que guarda cada registro como um documento
  JSON). Ganharíamos flexibilidade de formato. Descartamos porque as restrições declarativas
  (`CHECK`, `UNIQUE`) são menos maduras e não há equivalente direto ao
  `SELECT … FOR UPDATE SKIP LOCKED`; e nenhum serviço tem dados aninhados que justifiquem a
  troca. Seria a escolha certa para dados de formato variável e muito aninhados.
- **Banco chave-valor** (por exemplo, Redis ou DynamoDB: cada valor é lido e gravado por uma
  chave). Excelente para ler um resultado pronto. Descartamos para a fonte da verdade porque a
  gravação do lançamento precisa de `CHECK`, `UNIQUE` e da transação local com o Outbox. Seria a
  escolha certa para um cache de leitura, possível no futuro (`docs/06-arquitetura-alvo.md` §7).
- **Outro banco relacional, comercial (SQL Server).** Mesmas garantias. Descartamos por custo e
  portabilidade, não por capacidade: a licença é menos favorável a uma demonstração que qualquer
  pessoa possa reproduzir. Seria a escolha certa numa empresa que já tem SQL Server e uma equipe
  que o opera.
- **Banco de eventos (event store).** Ganharíamos gravação e leitura de eventos em ordem. Só
  faria sentido com Event Sourcing (guardar apenas os eventos e calcular o estado a partir
  deles), que o ADR-001 descarta para este escopo.

## Consequências

**Positivas:**

- Bom, porque nenhuma dessas garantias precisa de código de coordenação: o banco decide, mesmo
  com operações simultâneas.
- Bom, porque um único tipo de banco simplifica a operação local sem enfraquecer o isolamento
  (ADR-006).
- Bom, porque há ferramentas maduras de migração (EF Core), backup e monitoramento.

**Negativas:**

- Ruim, porque a escrita escala aumentando a máquina (escala vertical), até certo limite; o
  PostgreSQL não divide os dados entre vários servidores sozinho (sharding).
- Ruim, porque o banco de Lançamentos é um ponto de dependência física se não for replicado
  (`docs/06-arquitetura-alvo.md` §4).
- Ruim, porque o EF Core acrescenta uma camada sobre o SQL. Mitigamos com a configuração
  explícita de cada tabela (`IEntityTypeConfiguration`) e com migrações em SQL puro quando o EF
  Core não oferece o recurso (o trigger de imutabilidade).

## Trade-offs

Aceitamos escalar a escrita aumentando a máquina, sem divisão nativa entre servidores, para que
as garantias de concorrência venham do próprio banco, e não de código da aplicação. Se for
preciso escalar, o caminho é incremental: primeiro uma máquina maior; depois réplicas de leitura
(read replicas, cópias do banco que só atendem consultas) para o Consolidado; depois dividir as
tabelas por `business_date` (particionamento); só então um banco SQL distribuído, de operação
cara demais sem evidência de necessidade.

## Failure modes

- **Banco de Lançamentos fora do ar:** o registro é recusado com erro explícito; não há plano B
  silencioso (`docs/07-runbooks.md` §6).
- **Banco do Consolidado fora do ar:** o consumidor trata como falha temporária (ADR-002) e
  tenta de novo indefinidamente, sem mandar para a DLQ (Dead Letter Queue, fila das mensagens
  que não puderam ser processadas); Lançamentos não é afetado (NFR-001, requisito: o
  Consolidado fora do ar não derruba Lançamentos).
- **Impasse entre transações (deadlock) ou conflito de serialização (códigos `40P01` e `40001`
  do PostgreSQL):** o `ConsumerFailureClassifier` trata como falha temporária, e a nova
  tentativa resolve.
- **Tabela do Consolidado corrompida:** reconstrução manual a partir do Outbox
  (`scripts/ops/rebuild-consolidation.sh`, FR-007); `launches` nunca é alterada.

## Quando revisitar

Se o volume de escrita constante passar do que uma máquina maior com réplicas de leitura
suporta, ou se surgir a exigência de operar em várias regiões com RPO (Recovery Point Objective,
perda máxima aceitável de dados) próximo de zero nas duas — algo que o desafio hoje não pede.

## Rastreabilidade

MR-04 (requisito obrigatório do PDF: justificar a escolha de ferramentas e tecnologias); FR-001
(registrar um lançamento); FR-004 (impedir duplicata por nova tentativa); FR-007 e NFR-009 (o
Consolidado pode ser reconstruído); NFR-004 (lançamento confirmado nunca se perde); A-009
(lançamento e Outbox na mesma transação); A-010 (o Consolidado grava Inbox e totais antes de
confirmar a mensagem); A-013 (pico de 50 lançamentos por segundo). Decisões relacionadas:
[ADR-003](ADR-003-transactional-outbox.md), [ADR-004](ADR-004-processamento-idempotente.md),
[ADR-006](ADR-006-isolamento-de-dados.md).
