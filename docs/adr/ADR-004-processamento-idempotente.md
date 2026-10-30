# ADR-004 — Idempotência no registro e no consumo

- **Status:** Aceita
- **Data:** 2026-10-19

## Contexto

Repetir uma mensagem não pode repetir dinheiro. Isso pode acontecer em dois pontos:

- **No registro:** o cliente envia um lançamento de R$ 100,00, o banco grava, mas a resposta se
  perde na rede. O cliente reenvia. Sem proteção, haveria dois lançamentos de R$ 100,00.
- **No consumo:** o broker (o RabbitMQ) entrega o evento ao Consolidado, que soma os R$ 100,00
  ao dia e cai antes de confirmar o recebimento. O broker entrega de novo e o dia mostraria
  R$ 200,00.

As duas operações precisam ser idempotentes: repetidas, têm o mesmo efeito de uma vez só (FR-004,
requisito nosso: impedir a criação duplicada por nova tentativa).

## Decisão

Decidimos que quem impede a duplicata é uma restrição de unicidade do próprio banco, e não uma
verificação do código, porque só o banco decide com segurança quando duas requisições iguais
chegam ao mesmo tempo. Uma restrição do banco (constraint) é uma regra que o PostgreSQL aplica a
toda gravação: `UNIQUE` (o valor não pode se repetir), `CHECK` (o valor obedece a uma condição,
como ser maior que zero) e `NOT NULL` (o campo é obrigatório).

**No registro:**

- Cada pedido traz uma chave escolhida pelo cliente no cabeçalho `Idempotency-Key` (por exemplo,
  `venda-0001`), repetida em cada reenvio. A coluna da chave é `UNIQUE`.
- Guardamos o fingerprint do pedido: um resumo (hash SHA-256) do valor, da natureza e da
  descrição. Exemplo: `venda-0001` chega com R$ 100,00, crédito, "Venda balcão". Se chegar de
  novo igual, o fingerprint é o mesmo e a resposta é `200`, com o lançamento original e o
  cabeçalho `Idempotency-Replayed: true`. Se chegar com R$ 150,00, o fingerprint muda e a
  resposta é `409`; nada é gravado.
- O código procura a chave antes de gravar, mas isso não basta por causa de uma condição de
  corrida (race condition: erro que só acontece quando duas operações rodam ao mesmo tempo).
  Exemplo: duas requisições com `venda-0001` chegam no mesmo milissegundo; as duas procuram,
  nenhuma encontra e as duas tentam gravar. A restrição `UNIQUE` deixa só uma gravar; a outra
  recebe o erro de unicidade do PostgreSQL (código `23505`), lê o lançamento vencedor e responde
  como uma repetição normal. Respostas completas em
  [`docs/03-contratos.md`](../03-contratos.md#comportamento-de-idempotência-e-conflitos).

**No consumo:** antes de confirmar o recebimento — o ACK (acknowledgement) —, o Consolidado
grava o `eventId` e o `entryId` na Inbox (tabela `inbox_messages`), na mesma transação em que
soma o valor. A chave primária (`event_id`) e uma restrição `UNIQUE` (`entry_id`) impedem gravar
o mesmo evento duas vezes: numa nova entrega, a gravação falha, a transação é desfeita, nada é
somado, e a mensagem é confirmada e descartada.

## Alternativas consideradas

- **Restrição de unicidade do banco como árbitro (escolhida).** Vale para qualquer caminho de
  gravação, mesmo com requisições simultâneas, sem peças novas.
- **Identidade de negócio na origem** (o identificador da venda no sistema do cliente, em vez de
  uma chave técnica). Protegeria também contra o mesmo pedido reenviado com outra chave.
  Descartamos porque o desafio não pede essa garantia e ela obrigaria o cliente a enviar essa
  identidade; a `Idempotency-Key` já cobre a nova tentativa técnica. Seria a escolha certa ao
  integrar um sistema que reenvia a mesma venda com chaves diferentes (ver "Quando revisitar").
- **Trava distribuída** (distributed lock: uma trava fora da aplicação, por exemplo no Redis,
  que cada requisição pega antes de gravar, para que só uma por chave grave de cada vez).
  Evitaria a corrida antes do banco. Descartamos porque acrescenta uma peça e não substitui a
  restrição do banco: se a trava expirar ou o Redis cair, a duplicata volta a ser possível. Seria
  a escolha certa para proteger um recurso fora de um banco transacional.
- **Entrega "exatamente uma vez" (exactly-once) pelo broker** (cada mensagem chegaria uma única
  vez). Dispensaria a Inbox. Descartamos porque a garantia, onde existe, para no consumidor e não
  chega ao banco do Consolidado: se o consumidor cair depois de somar e antes de confirmar, a
  mensagem volta. Prometê-la seria enganoso. Só seria a escolha certa se o resultado fosse
  gravado no próprio broker, como nas transações do Kafka.

## Consequências

**Positivas:**

- Bom, porque a nova tentativa do cliente não cria um segundo lançamento, mesmo com requisições
  simultâneas (FR-004).
- Bom, porque uma nova entrega do broker nunca soma o mesmo evento duas vezes.

**Negativas:**

- Ruim, porque é preciso guardar o fingerprint e o suficiente para repetir a resposta original.
- Ruim, porque as chaves precisam ficar guardadas enquanto o cliente puder tentar de novo; no
  consumo, a Inbox fica guardada por no mínimo 30 dias (A-012, premissa de retenção).

## Trade-offs

Aceitamos guardar o fingerprint e o resultado de cada chave (mais armazenamento e uma restrição a
mais por gravação) para nunca depender de uma consulta prévia do código. Consultar e depois
gravar deixaria uma janela de corrida justamente onde a garantia mais importa.

## Failure modes

- Com requisições simultâneas, uma perde a corrida na restrição e lê o lançamento vencedor, em
  vez de falhar.
- Uma falha depois de gravar e antes de responder é segura: a nova tentativa encontra o
  resultado gravado.
- Reusar uma `Idempotency-Key` com outro pedido retorna `409` e nunca altera o lançamento
  existente, que é imutável (ADR-001).

## Quando revisitar

Se o desafio passar a exigir eliminar duplicatas por origem — por exemplo, na integração com um
POS (Point of Sale, a maquininha ou o sistema de caixa da loja) ou uma adquirente que reenvia a
mesma venda com uma chave técnica diferente a cada vez —, acrescentar uma identidade de negócio
por origem, além da `Idempotency-Key` (`docs/01-requisitos-e-assumptions.md` § "Fora do escopo
desta entrega").

## Rastreabilidade

FR-004; INV-005 (invariante: a `Idempotency-Key` é única); NFR-004 (lançamento confirmado nunca
se perde); [contrato de idempotência](../03-contratos.md#comportamento-de-idempotência-e-conflitos).
