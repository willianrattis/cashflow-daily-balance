# ADR-001 — Dois serviços independentes, cada um dono dos próprios dados

- **Status:** Aceita
- **Data:** 2026-10-19

## Contexto

O desafio pede duas coisas: registrar lançamentos financeiros (créditos e débitos) e consultar
o saldo diário consolidado. O requisito mais rígido é o NFR-A (requisito não funcional do PDF:
"O serviço de controle de lançamento não deve ficar indisponível se o sistema de consolidado
diário cair"). As cargas também diferem: o registro é uma escrita curta; a consulta soma
valores por período e recebe 50 requisições por segundo nos picos. Precisamos separar as duas
partes para que a falha de uma não derrube a outra, sem criar mais serviços do que o problema
pede.

## Decisão

Decidimos criar dois serviços, implantados separadamente e cada um com o próprio banco, porque
essa é a menor divisão que impede uma falha no Consolidado de parar o registro (NFR-A).

- **Lançamentos (`CashFlowEntryService`)** valida o pedido, impede lançamentos duplicados (ADR-004) e
  grava o lançamento. Na mesma transação, grava numa tabela chamada Outbox a mensagem que vai
  avisar o Consolidado (ADR-003). Também permite consultar um lançamento registrado.
- **Consolidado (`ConsolidationService`)** recebe essas mensagens e soma cada lançamento ao
  total do dia. Só confirma o recebimento ao broker — o ACK (acknowledgement) — depois de
  gravar no próprio banco. Responde à consulta do saldo por intervalo de datas.
- Os dois conversam só por mensagens, por um broker (o RabbitMQ, servidor que guarda as
  mensagens até o destino processá-las). O broker transporta; a fonte da verdade é a tabela de
  lançamentos.
- Nenhum serviço chama o outro nem lê o banco do outro. No ambiente local, os dois bancos ficam
  no mesmo servidor PostgreSQL, mas separados e com usuários separados (ADR-006).
- Dentro de cada serviço, o código é organizado por funcionalidade (uma pasta por endpoint, em
  `Features/`), não por camada técnica. Isso organiza o código; não cria serviços.

## Alternativas consideradas

- **Dois serviços independentes (escolhida).** Ganhamos o isolamento de falhas que o NFR-A
  exige e a possibilidade de escalar cada lado pela própria carga. Pagamos com um broker para
  operar e com um Consolidado alguns segundos atrás dos Lançamentos (ADR-005).
- **Monolito** (um único programa com as duas partes, em módulos ou não). Seria mais simples de
  implantar. Descartamos pelo isolamento de falhas: uma queda, um deploy com erro ou um
  vazamento de memória na consulta derrubaria o processo que registra lançamentos — o que o
  NFR-A proíbe. E, num programa só, não dá para escalar a consulta sem escalar o registro
  (`docs/06-arquitetura-alvo.md` §6). Seria a escolha certa se a consulta pudesse cair junto
  com o registro e a carga fosse baixa.
- **Serverless** (cada endpoint vira uma função na nuvem, executada só quando chamada). Não
  teríamos servidores para gerenciar. Descartamos porque o publisher do Outbox precisa de um
  processo sempre ligado, que consulta o banco e espera entre tentativas (ADR-003); porque a
  primeira chamada a uma função recém-criada é lenta (cold start), no caminho do registro, que
  hoje responde em milissegundos (`docs/06` §6); e porque pagar por execução a 50 requisições
  por segundo, por horas, tende a custar mais que poucas cópias sempre ligadas. Seria a escolha
  certa para cargas raras e irregulares.
- **SOA (Service-Oriented Architecture) com ESB (Enterprise Service Bus)**: sistemas que
  conversam por um barramento central que roteia, transforma e orquestra as mensagens. Ganharíamos
  governança central dos contratos. Descartamos porque o barramento vira uma peça compartilhada
  e um ponto único de falha, para resolver o que um broker e um evento com versão já resolvem
  (ADR-002). É mais operação do que três endpoints e cinco filas justificam: a principal, três
  de espera e a DLQ (Dead Letter Queue, a fila das mensagens que não puderam ser processadas).
  Seria a escolha certa numa empresa com dezenas de sistemas que precisam conversar.
- **Mais microsserviços** (por exemplo, um para validar, outro para publicar, outro para
  consultar). Cada serviço a mais traz mais chamadas de rede, mais um deploy e mais um ponto que
  pode cair. O desafio tem duas responsabilidades com necessidades de disponibilidade diferentes
  — registrar e consultar — e dois serviços bastam para separá-las. Seria a escolha certa se uma
  parte precisasse escalar sozinha ou fosse mantida por outro time.
- **Event Sourcing** (guardar apenas os eventos e calcular o estado atual a partir deles). Já
  temos o principal benefício sem esse custo: os lançamentos nunca são alterados nem apagados,
  então formam o histórico completo, e o consolidado pode ser reconstruído a partir deles
  (`scripts/ops/rebuild-consolidation.sh` republica o evento de cada lançamento, guardado no
  Outbox). Seria a escolha certa se o negócio precisasse ver o estado de qualquer registro em
  qualquer momento do passado.

## Consequências

**Positivas:**

- Bom, porque o Consolidado parado ou atrasado não impede novos lançamentos (NFR-A).
- Bom, porque cada serviço escala conforme a própria carga.
- Bom, porque fica claro o que é a fonte da verdade (a tabela `cash_flow_entries`) e o que é derivado e
  pode ser reconstruído (os totais do Consolidado).
- Bom, porque dá para testar a recuperação do Consolidado e reconstruir os totais sem tocar em
  nenhum lançamento.

**Negativas:**

- Ruim, porque o Consolidado fica alguns segundos atrás (consistência eventual) e há mais peças
  para operar: atraso, novas tentativas, mensagens na DLQ (Dead Letter Queue, fila das
  mensagens que não puderam ser processadas) e saúde dos serviços precisam ser monitorados
  (ADR-002, ADR-010).
- Ruim, porque o formato do evento precisa continuar compatível entre os dois serviços.
- Ruim, porque o ambiente local fica um pouco maior (Docker Compose com broker); em troca, todo
  o fluxo roda na máquina de quem avalia.

## Trade-offs

Aceitamos a consistência eventual e mais peças móveis — broker, Outbox e Inbox (tabela do
Consolidado com os eventos já aplicados) — para que nenhuma falha do Consolidado atinja o
registro. A troca só vale porque o desafio prioriza uma escrita sempre disponível sobre uma
consulta atualizada no mesmo instante.

## Failure modes

- **Consolidado parado:** os lançamentos continuam sendo aceitos; as mensagens se acumulam na
  fila e são processadas quando ele volta.
- **Broker fora do ar:** a transação local grava o lançamento e a mensagem no Outbox mesmo
  assim; o publisher publica mais tarde.
- **Consolidado reiniciado depois de gravar e antes de confirmar:** o broker entrega a mensagem
  de novo; a Inbox e a unicidade do `entryId` fazem a segunda entrega ser ignorada (ADR-004).
- **Falha ao somar:** a mensagem não recebe o ACK e vai para nova tentativa ou para a DLQ; os
  totais podem ser reconstruídos a partir dos eventos guardados no banco de Lançamentos.

## Quando revisitar

Se a consulta pudesse depender diretamente da escrita, a carga fosse baixa e não houvesse
exigência de isolar a disponibilidade, um monolito modular seria mais simples. Se o produto
exigisse reprocessar eventos continuamente, muitos consumidores em grande escala ou análises em
tempo real, reavaliar o broker e por quanto tempo os eventos ficam guardados.

## Rastreabilidade

FR-001, FR-003 e FR-007 (registrar, consultar por intervalo e reconstruir o Consolidado);
NFR-001 (o Consolidado fora do ar não derruba Lançamentos; refina o NFR-A); NFR-002 (50
requisições por segundo no Consolidado); NFR-005 (Consolidado até alguns segundos atrás: 95% dos
eventos aplicados em até 5 s); NFR-009 (os totais podem ser reconstruídos); NFR-014 (metas de tempo de recuperação).
Texto completo em [`docs/01-requisitos-e-assumptions.md`](../01-requisitos-e-assumptions.md).
Diagramas: C1 (contexto) e C2 (containers) da entrega em
[`docs/diagrams/README.md`](../diagrams/README.md); o de containers mostra o publisher do
Outbox e o consumidor no mesmo processo da API ([ADR-011](ADR-011-topologia-de-processos.md)),
e a topologia de produção está em `docs/06-arquitetura-alvo.md`. Testes: registrar com o
Consolidado parado; publicar e consumir depois da recuperação; repetir a mesma mensagem;
reconstruir os totais.
