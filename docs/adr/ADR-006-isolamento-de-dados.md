# ADR-006 — Isolamento de dados: um banco por serviço, usuários de banco com o mínimo de permissão e um único comerciante

- **Status:** Aceita
- **Data:** 2026-10-19

## Contexto

O PDF diz "Um comerciante precisa controlar o seu fluxo de caixa diário" — no singular. Uma
instalação desta solução atende um único comerciante (A-021, premissa nossa), e não há exigência
de separar vários comerciantes entre si. O que continua obrigatório é outra separação: **entre os
dois serviços** (ADR-001, decisão sobre dois serviços independentes). Mesmo com os dois bancos
no mesmo servidor PostgreSQL no ambiente local, nenhum serviço pode ler ou gravar o banco do
outro — e isso não pode depender só de o código se comportar.

## Decisão

Decidimos que cada serviço se conecta ao PostgreSQL com um usuário de banco próprio (uma role),
sem privilégio de administrador e com acesso só ao banco do próprio serviço, porque assim o
isolamento é garantido pelo banco, e não pela disciplina do código.

- `cashflow_entry_svc` é dono de `cashflow_entry_db` (Lançamentos); `consolidation_svc` é dono de
  `consolidation_db` (Consolidado).
- Por padrão, o PostgreSQL deixa qualquer usuário se conectar a qualquer banco. O script
  `scripts/postgres/init-databases.sh` retira essa permissão dos dois bancos e a devolve só ao
  usuário de cada serviço (`REVOKE CONNECT … FROM PUBLIC` e `GRANT CONNECT`). Na prática,
  Lançamentos não consegue nem abrir uma conexão com o banco do Consolidado, e vice-versa.
- O usuário administrador (`cashflow`, superusuário) fica só para os scripts de operação
  (`scripts/ops/*`), que um operador roda à mão, e para a criação dos bancos
  (`scripts/postgres/*`).
- Como há um único comerciante por instalação (A-021), nenhuma tabela tem coluna de
  comerciante, e nenhuma consulta ou consumidor filtra por ela.

## Alternativas consideradas

- **Um usuário próprio por serviço, sem privilégio de administrador e sem acesso ao banco do
  outro (escolhida).** Ganhamos isolamento garantido pelo banco: um bug ou uma senha vazada num
  serviço não alcança os dados do outro.
- **Um único usuário compartilhado pelos dois serviços.** Seria mais simples de configurar.
  Descartamos porque um bug ou uma senha vazada num serviço daria leitura e escrita no banco do
  outro, contradizendo a separação em dois serviços. Seria aceitável se os dois fossem, na
  prática, um só sistema.
- **Usuário administrador para os serviços.** Nenhuma permissão faltaria. Descartamos porque o
  dia a dia dos serviços não precisa desse poder; ele fica para os scripts de operação, que
  precisam ler os dois bancos num mesmo script. Não há cenário em que um serviço devesse rodar
  como administrador.

## Consequências

**Positivas:**

- Bom, porque um bug de aplicação, sozinho, não alcança o banco do outro serviço: a barreira é
  do banco (permissão de conexão), não só disciplina de código.
- Bom, porque as migrações continuam simples: um schema por banco, sem divisão por comerciante.

**Negativas:**

- Ruim, porque, sem separação entre comerciantes, cada instalação atende exatamente um; atender
  vários exigiria revisitar esta decisão.

## Trade-offs

Aceitamos não separar comerciantes (o PDF descreve um só) para manter o schema e as consultas
mais simples. O isolamento que continua obrigatório, entre os dois serviços, não depende dessa
escolha.

## Failure modes

- A senha vazada de um serviço não abre conexão com o banco do outro: o que foi retirado é a
  permissão de conectar ao banco, não só a de ler tabelas.
- Os scripts de operação, com o usuário administrador, são o único caminho que lê os dois
  bancos. Um script comprometido teria esse alcance maior, documentado no cabeçalho de cada
  script.

## Quando revisitar

Se a solução precisar atender vários comerciantes na mesma instalação (multi-tenant), seria
preciso:

- identificar o comerciante dentro do próprio token, num campo emitido pelo IdP (Identity
  Provider, o provedor de identidade);
- pôr o identificador do comerciante na frente de toda chave e de todo índice de negócio, não só
  da chave primária. Exemplo, com a coluna `merchant_id`: o índice único da `Idempotency-Key`
  passaria a ser (comerciante, chave). Só com (chave), dois comerciantes que usassem a mesma
  chave `venda-0001` colidiriam, e o segundo receberia o lançamento do primeiro. Da mesma forma,
  o índice por data passaria a ser (comerciante, data), para que a consulta de um comerciante
  leia só as linhas dele, e não as de todos naquela data;
- filtrar pelo comerciante em toda consulta e em todo consumidor;
- usar RLS (Row-Level Security, recurso do PostgreSQL que filtra no próprio banco as linhas que
  cada conexão pode ver) como segunda barreira, nunca a única;
- dividir por comerciante os limites de requisições e as métricas.

Isso estenderia o modelo atual, não o substituiria.

## Rastreabilidade

A-021 e [capacidades e domínios](../02-capacidades-e-dominios.md).
