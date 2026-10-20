# Requisitos, premissas e escopo

Este documento diz o que a solução precisa fazer (requisitos funcionais), com que qualidade
(requisitos não funcionais, com metas medíveis), que regras nunca podem ser violadas
(invariantes), que decisões tomamos onde o PDF do desafio não diz nada (premissas) e o que ficou
fora do escopo. Cada item tem um código — FR-001, NFR-001, INV-001, A-001 — usado no resto da
documentação. O [glossário](11-glossario.md#famílias-de-códigos) explica cada família de código.

## Convenções

A coluna "Origem" diz como cada requisito nasceu:

| Origem | Significado |
|---|---|
| Explícito | O PDF do desafio pede diretamente. |
| Derivado | Decorre de um pedido explícito do PDF, sem estar escrito nele. |
| Explícito/Ambíguo | O PDF pede, mas o texto admite mais de uma leitura; a linha diz qual adotamos. |
| Premissa | Decisão nossa onde o PDF não diz nada, ainda sem confirmação de alguém do negócio. |

A coluna "Origem no desafio" aponta o item do PDF de onde o requisito vem, com um resumo do texto
(o texto completo está em [`docs/09-rastreabilidade.md`](09-rastreabilidade.md)). O PDF não
numera os seus itens; os códigos que demos a eles são: BR (requisito de negócio), MR (requisito obrigatório), NFR-A a NFR-C
(requisitos não funcionais escritos no PDF), DF (diferencial) e OBJ (critério de avaliação). Um
"—" quer dizer que o PDF não fala do assunto e o requisito é uma decisão nossa. É o caso, por
exemplo, de idempotência (repetir uma requisição não cria um segundo lançamento), imutabilidade,
fuso horário, moeda, metas de recuperação — RPO (Recovery Point Objective, quanto dado se aceita
perder) e RTO (Recovery Time Objective, quanto tempo o serviço pode ficar fora) —, metas de tempo
de resposta e volume de escrita.

Cada premissa registra por que a tomamos, o impacto, o estado e a pergunta que faríamos a alguém
do negócio num projeto real.

## Glossário

Os termos do domínio usados neste documento. O glossário completo, com siglas e termos técnicos,
está em [`docs/11-glossario.md`](11-glossario.md).

| Termo | Significado neste desafio |
|---|---|
| **Lançamento** (Launch) | Um crédito ou um débito registrado. Depois de confirmado, nunca é alterado nem apagado. |
| **Natureza** | `Credit` (crédito, aumenta o saldo) ou `Debit` (débito, reduz o saldo). |
| **BusinessDate** (data do negócio) | O dia do calendário, no fuso `America/Sao_Paulo`, ao qual o lançamento pertence. Quem define é o servidor, no momento do registro; o cliente não informa. |
| **Consolidado** | Os totais de cada dia — créditos, débitos e variação —, calculados a partir dos lançamentos e guardados na tabela `daily_consolidations`. A consulta soma esses totais para um intervalo de datas. |
| **Fonte da verdade** | O dado oficial, que vale quando há divergência. Aqui, é a tabela de lançamentos; nem o broker (o servidor de mensagens, RabbitMQ) nem o Consolidado são fonte da verdade. |
| **Outbox** (Transactional Outbox) | Tabela onde Lançamentos grava a mensagem a publicar, na mesma transação do lançamento. Depois, o publisher (uma tarefa em segundo plano) envia essa mensagem ao broker. |
| **Inbox** | Tabela onde o Consolidado registra cada evento já aplicado. Serve para descartar cópias repetidas antes de confirmar o recebimento ao broker com um ACK (acknowledgement, confirmação de recebimento). |
| **Idempotency-Key** | Identificador que o cliente envia em cada registro. Repetir a mesma chave com o mesmo conteúdo devolve o lançamento original, sem criar outro. |
| **Atraso do Consolidado** (freshness) | Tempo entre o registro de um lançamento e o momento em que ele aparece na consulta do Consolidado. |
| **RPO e RTO** | Metas de recuperação: quanto dado se aceita perder numa falha (RPO) e quanto tempo o serviço pode ficar fora (RTO). |

## Contexto do problema

Um comerciante precisa controlar seu fluxo de caixa: registrar lançamentos de crédito e de débito
e consultar o saldo consolidado por dia ou por período. O registro de lançamentos precisa
continuar funcionando mesmo quando o Consolidado estiver fora do ar.

## Atores

| Ator | O que faz |
|---|---|
| Pessoa ou sistema com a permissão (scope) `cashflow.launches.write` no token de acesso | Registra lançamentos |
| Pessoa ou sistema com a permissão `cashflow.launches.read` ou `cashflow.consolidation.read` | Consulta lançamentos ou o Consolidado |
| Sistema de origem, por exemplo o sistema de caixa da loja | Envia lançamentos por uma integração autenticada |
| Provedor de identidade (Identity Provider) | Autentica pessoas e sistemas e emite os tokens de acesso. Nesta entrega, um script de desenvolvimento faz esse papel (ADR-007, decisão sobre o provedor de identidade externo). |

## Requisitos funcionais

| ID | Requisito | Origem | Origem no desafio | Critério de aceitação inicial |
|---|---|---|---|---|
| FR-001 | Registrar um lançamento financeiro. | Explícito | BR-01 (requisito de negócio: "Serviço que faça o controle de lançamentos") | Uma requisição válida grava exatamente um lançamento e devolve o identificador dele (`launchId`). |
| FR-002 | Classificar cada lançamento como crédito (`Credit`) ou débito (`Debit`). | Explícito/Derivado | BR-01 (controle de lançamentos) | Só essas duas naturezas são aceitas. |
| FR-003 | Consultar o Consolidado por um intervalo de datas, incluindo o primeiro e o último dia. | Explícito/Derivado | BR-02 (requisito de negócio: "relatório que disponibilize o saldo diário consolidado") | A resposta traz, para o intervalo, o saldo de abertura, os créditos, os débitos, a variação e o saldo de fechamento. Traz também `days[]`, com esses mesmos cinco valores para cada dia do calendário no intervalo, inclusive os dias sem movimento. |
| FR-004 | Não criar um lançamento duplicado quando o cliente repete a requisição. | Premissa | — | A mesma `Idempotency-Key` devolve o lançamento já gravado. |
| FR-005 | Aceitar operações de pessoas e de integrações. | Derivado | OBJ-05 (critério de avaliação: segurança, com autenticação e autorização) | A autorização verifica a permissão (scope) do token; não há perfis diferentes para pessoas (A-001). |
| FR-006 | Responder corretamente a períodos sem movimento. | Derivado | BR-02 (relatório do saldo diário) | Uma consulta válida devolve totais zerados para o período, sem mudar o intervalo pedido. |
| FR-007 | Reconstruir o Consolidado do zero. | Premissa | OBJ-04 (critério de avaliação: resiliência e estratégias de recuperação) | Um procedimento de operação (`scripts/ops/rebuild-consolidation.sh`) recalcula todos os totais a partir dos lançamentos registrados, republicando o evento de cada um, guardado no Outbox. |

## Requisitos não funcionais

| ID | Requisito | Origem | Origem no desafio | Métrica ou decisão inicial |
|---|---|---|---|---|
| NFR-001 | Se o Consolidado cair, o registro de lançamentos continua funcionando. | Explícito | NFR-A (PDF: o serviço de lançamentos não pode ficar indisponível se o consolidado cair) | O Consolidado fica fora do caminho do registro: Lançamentos nunca o chama. |
| NFR-002 | O Consolidado aguenta um pico de 50 requisições por segundo. | Explícito | NFR-B (PDF: nos dias de pico, o consolidado recebe 50 requisições por segundo) | Teste reproduzível a 50 requisições por segundo durante 2 minutos (`scripts/load/run.sh consolidation`); resultado em `docs/evidence/load/`. |
| NFR-003 | No máximo 5% das requisições ao Consolidado podem falhar. | Explícito/Ambíguo | NFR-C (PDF: no máximo 5% de perda de requisições) | Lemos "perda" como consulta que falha, nunca como dado financeiro perdido (A-004). Medido como `http_req_failed` (a taxa de requisições com erro) no teste de carga de NFR-002: 0% na última execução. |
| NFR-004 | Nenhum lançamento confirmado ao cliente pode se perder. | Premissa | — | RPO igual a zero para operações confirmadas. |
| NFR-005 | O Consolidado pode ficar alguns segundos atrás dos lançamentos (consistência eventual). | Premissa | OBJ-08 (critério de avaliação: métricas e metas claras de desempenho e disponibilidade) | Em condições normais, 95% dos lançamentos aparecem na consulta em até 5 s e 99% em até 30 s, ou seja, p95 ≤ 5 s e p99 ≤ 30 s (p95 e p99 são os valores abaixo dos quais ficam 95% e 99% das medições). |
| NFR-006 | Toda operação financeira pode ser auditada. | Derivado | — | Cada lançamento guarda quem o registrou (`sub`, o identificador do chamador no token), o instante do registro e o identificador de correlação (`correlationId`). |
| NFR-007 | As APIs autenticam e autorizam pessoas e sistemas. | Explícito/Derivado | OBJ-05 (segurança) | Cada requisição precisa de um token JWT (JSON Web Token, token de acesso assinado) válido, e cada operação exige uma permissão (scope). Login e emissão de tokens em produção ficam num provedor de identidade externo. |
| NFR-008 | A documentação e a execução podem ser reproduzidas por qualquer pessoa. | Explícito | MR-07 e MR-09 (requisitos obrigatórios: README com instruções claras para rodar localmente; toda a documentação no repositório) | README, diagramas, ADRs (Architecture Decision Records, registros de decisão de arquitetura), testes e instruções ficam no repositório. |
| NFR-009 | Os totais do Consolidado podem ser recuperados se forem perdidos ou corrompidos. | Explícito/Derivado | OBJ-04 (resiliência e recuperação) | Perder ou corromper os totais não afeta os lançamentos, e a reconstrução (FR-007), feita só em caso excepcional, corrige o problema. |
| NFR-010 | Quanto tempo guardar os dados de operação (Outbox, Inbox, mensagens) é configurável. | Premissa | — | Nenhuma mensagem financeira pendente, nem parada na DLQ (Dead Letter Queue, fila das mensagens que não puderam ser processadas), expira sem aviso. |
| NFR-011 | A plataforma aguenta o volume de escrita que assumimos (A-013). | Premissa | — | Teste reproduzível de 50 lançamentos por segundo: 2 minutos por padrão e 15 minutos sob demanda (`scripts/load/run.sh launches 15m`). A arquitetura foi avaliada, só no papel, para 10 vezes esse volume. O teste do pico completo de 15 minutos está em [`docs/evidence/load/2026-10-20-launches.md`](evidence/load/2026-10-20-launches.md): 45001 iterações, 0% de falha, nenhuma requisição deixada de enviar (`dropped_iterations` = 0), p95 de 5,75 ms e p99 de 8,07 ms. |
| NFR-012 | As APIs cumprem metas de disponibilidade. | Premissa | OBJ-08 (métricas e metas claras) | Meta de 99,9% de disponibilidade por mês. No teste de pico, o Consolidado precisa de pelo menos 95% de sucesso: foram 100% na última execução (`docs/evidence/load/`). |
| NFR-013 | As APIs cumprem metas de tempo de resposta. | Premissa | OBJ-08 (métricas e metas claras) | Em condições normais: Lançamentos com p95 até 300 ms e p99 até 1 s; Consolidado com p95 até 500 ms e p99 até 2 s. Medido no teste de carga (`docs/evidence/load/`). |
| NFR-014 | Os serviços cumprem metas de recuperação. | Premissa | OBJ-04 (resiliência e recuperação) | Lançamentos volta em até 15 minutos (RTO) sem perder nada (RPO igual a zero); o Consolidado volta em até 60 minutos. |

### Nota: o que quer dizer "o Consolidado recebe 50 requisições por segundo" (NFR-002)

O PDF diz: "o serviço de consolidado diário **recebe** 50 requisições por segundo". "Recebe"
admite duas leituras, e cobrimos as duas:

1. **Consultas.** O Consolidado responde a 50 `GET /consolidation` por segundo. É a leitura de
   NFR-002 e NFR-003, testada contra `GET /consolidation` (`docs/evidence/load/`).
2. **Eventos recebidos.** O Consolidado consome 50 eventos `LaunchRecorded.v1` por segundo, isto
   é, 50 lançamentos por segundo registrados em Lançamentos e entregues pelo broker. É a leitura
   de A-013 e NFR-011. O teste de carga de lançamentos também mede quanto o Consolidado demora para
   aplicar cada evento (`consumer_lag_seconds`, em `docs/evidence/load/2026-10-19-launches.md`).

Neste desafio não há como perguntar ao negócio qual leitura é a certa; a pergunta fica registrada
em A-013.

## Invariantes e regras de negócio

Regras que nunca podem ser violadas, em nenhum caminho do sistema:

| ID | Regra |
|---|---|
| INV-001 | Um lançamento confirmado nunca é alterado nem apagado. Um trigger do banco (código que o banco executa a cada gravação) rejeita qualquer `UPDATE` ou `DELETE` na tabela `launches`. |
| INV-002 | O valor é um inteiro positivo em centavos de real (BRL), e a natureza é `Credit` ou `Debit`. Exemplo: R$ 125,00 é enviado como `12500`. |
| INV-003 | O servidor define a `BusinessDate` no momento do registro; o cliente não a informa. |
| INV-004 | Não se aceitam lançamentos com data passada nem futura: todo lançamento pertence ao dia em que foi registrado. |
| INV-005 | Uma `Idempotency-Key` nunca se repete na tabela de lançamentos. |
| INV-006 | O Consolidado nunca altera os lançamentos. Os dados só correm num sentido: de Lançamentos para o Consolidado. |
| INV-007 | O saldo pode ficar negativo. Um débito maior que o saldo disponível é registrado normalmente, sem bloqueio e sem forçar o saldo a zero. |

## Premissas (assumptions)

Decisões nossas onde o PDF não diz nada. Todas estão aceitas para esta entrega; a última coluna
traz a pergunta que faríamos ao negócio para confirmá-las.

| ID | Premissa | Por quê | Impacto | Estado | Pergunta ao negócio |
|---|---|---|---|---|---|
| A-001 | Um comerciante pode ter várias pessoas operando o mesmo fluxo de caixa. | Comerciante e usuário são coisas diferentes. | Cada lançamento guarda quem o registrou (`sub` do token). Não há perfis diferentes para pessoas: a autorização usa só as permissões (scopes). | Aceita | Como funcionam hoje os usuários e perfis do comerciante? |
| A-002 | O fuso horário do negócio é `America/Sao_Paulo`. | Define sem ambiguidade a que dia pertence cada lançamento. | Os instantes técnicos ficam em UTC; a data do negócio é calculada a partir deles, no fuso de São Paulo. | Aceita | Há comerciantes fora do fuso de São Paulo? |
| A-003 | A moeda é o real (BRL), sem conversão de câmbio. | O PDF não menciona outras moedas. | Regras e totais mais simples. | Aceita | Outras moedas estão nos planos? |
| A-004 | Os "5% de perda" do PDF são requisições que falham temporariamente, e não dados perdidos para sempre. | Perder um movimento financeiro é inaceitável neste domínio. | Exige novas tentativas, reenvio de mensagens e gravação durável. | Aceita | Essa leitura está certa, ou perder até 5% dos dados seria aceitável? |
| A-005 | A tabela de lançamentos, que nunca muda, é a fonte da verdade; o Consolidado é uma tabela de totais calculada a partir dela. | Permite reconstruir o Consolidado e separar os dois serviços. | Exige processamento idempotente (repetir uma operação não duplica o efeito) e um procedimento de reconstrução. | Aceita | Essa separação atende ao modelo de auditoria e de contabilidade esperado? |
| A-006 | Identidade e emissão de tokens ficam num provedor de identidade externo. | Evita construir gestão de identidade, que não é o problema do desafio. | As APIs só validam tokens: são "resource servers", no vocabulário do OAuth 2.0, o padrão de autorização por tokens. | Aceita | Qual provedor de identidade da empresa deve ser integrado, e com qual protocolo? |
| A-007 | Não há informações concretas sobre um sistema legado. | O PDF só cita uma arquitetura de transição como diferencial. | A estratégia de migração é condicional (`docs/06-arquitetura-alvo.md` §10). | Aceita | Existe um sistema legado a migrar? Com que volume e formato de dados? |
| A-008 | O broker transporta mensagens; não é a fonte da verdade. | Evita depender de o broker guardar mensagens para sempre. | Depois de aplicados no banco do Consolidado, os eventos podem sair do broker. | Aceita | É preciso guardar os eventos por mais tempo do que o necessário para descartar cópias repetidas? |
| A-009 | O lançamento e a mensagem do Outbox são gravados na mesma transação local. | Elimina o intervalo entre gravar o lançamento e registrar que ele precisa ser publicado. | O cliente só recebe sucesso depois que o banco confirma os dois (commit). | Aceita | O volume de escrita esperado cabe nessa transação única? |
| A-010 | O Consolidado grava a Inbox e os totais antes de confirmar a mensagem ao broker. | Evita perder um evento e evita somá-lo duas vezes quando o broker o entrega de novo. | O ACK só acontece depois do commit no banco do Consolidado. | Aceita | Um RTO de 60 minutos para o Consolidado é aceitável para o negócio? |
| A-011 | Lançamentos nunca são apagados automaticamente enquanto o sistema existir. | Preserva a fonte da verdade enquanto não houver uma política jurídica de retenção. | Pode exigir arquivamento no futuro. | Aceita | Já existe uma política jurídica ou fiscal de retenção? |
| A-012 | As mensagens já publicadas do Outbox ficam guardadas, sem prazo, porque são a fonte da reconstrução do Consolidado; a Inbox fica guardada por pelo menos 30 dias. | A reconstrução (`scripts/ops/rebuild-consolidation.sh`, FR-007) republica a partir do Outbox; apagá-lo cedo quebraria a única forma implementada de reconstruir. Uma limpeza do Outbox publicado é evolução futura e exigiria gerar os eventos a partir de `launches`, sem depender do histórico do Outbox. | Não há tarefa de limpeza nesta entrega: a tabela Outbox cresce sem limite enquanto o sistema operar. | Aceita | Quando priorizar uma tarefa de limpeza do Outbox? |
| A-013 | O pico de escrita é de 50 lançamentos por segundo durante 15 minutos. | O PDF não dá volume de escrita, e precisamos de uma base para testes e custos. Também cobre a segunda leitura de "o Consolidado recebe 50 requisições por segundo" (ver a nota em NFR-002). | A arquitetura precisa crescer horizontalmente, com mais cópias de cada serviço, e explicar o cenário de 10 vezes esse volume, sem tratá-lo como requisito atual. | Aceita | Qual é o volume real de escrita esperado em produção? |
| A-014 | As metas de disponibilidade e de tempo de resposta são objetivos iniciais. | O PDF não define um SLO (Service Level Objective, meta interna de nível de serviço). | Permite discutir custo e capacidade sem prometer nada em contrato. | Aceita | Existe um SLA (Service Level Agreement, acordo com o cliente que prevê penalidade) que deva substituir essas metas? |
| A-015 | A consulta do Consolidado aceita no máximo 366 dias entre `fromDate` e `toDate`. | O PDF não define um limite; sem ele, uma única consulta poderia exigir um `days[]` de qualquer tamanho. | Um intervalo maior recebe `422 date_range_too_large`; quem precisa de mais histórico faz várias consultas. | Aceita | Esse limite atende aos relatórios históricos do comerciante? |
| A-016 | Localmente, cada serviço roda com uma única cópia. | Rodar várias cópias no `docker compose` não foi testado, embora o código já esteja pronto (dois publishers ao mesmo tempo são testados contra um PostgreSQL real com Testcontainers, em `Two_concurrent_publisher_instances_publish_each_event_exactly_once`): cada cópia do publisher reserva um lote de mensagens que nenhuma outra cópia pode pegar ao mesmo tempo (no PostgreSQL, `SELECT ... FOR UPDATE SKIP LOCKED`; ADR-003, decisão sobre o Outbox), e várias cópias do consumidor dividem a mesma fila com segurança. | A capacidade com várias cópias foi argumentada em `docs/06-arquitetura-alvo.md` §6, não medida. | Aceita | Vale testar várias cópias localmente antes de produção? |
| A-017 | A `description` entra no fingerprint, o resumo (hash) do conteúdo da requisição usado para comparar repetições. | Incluir todos os campos relevantes deixa o cálculo simples. | Mudar só a `description` numa repetição com a mesma `Idempotency-Key` resulta em `409 idempotency_payload_mismatch`. Exemplo: a chave `venda-0001` enviada com "Venda balcão" e depois com "Venda balcão 2" recebe `409` na segunda vez. | Aceita | A `description` deve continuar no fingerprint, ou pode mudar livremente entre repetições? |
| A-018 | Uma única chave secreta assina os tokens das duas APIs com HS256 (assinatura em que quem assina e quem valida usam a mesma chave), e as duas aceitam o mesmo `audience` (`cashflow-api`, o campo do token que diz para qual API ele vale). | Simplicidade no ambiente local, sem um provedor de identidade real (ADR-007). | A autorização de cada endpoint vem da permissão (scope), e não de uma chave ou de um `audience` por serviço. A chave é compartilhada e documentada como só de desenvolvimento. | Aceita | Quando passar para RS256 (assinatura com par de chaves privada e pública), com as chaves públicas publicadas num JWKS (JSON Web Key Set), como prevê `docs/06-arquitetura-alvo.md` §8? |
| A-019 | O servidor define `recorded_at_utc` logo antes de confirmar a transação (commit), e não no instante exato do commit. | Calcular o instante dentro da mesma operação que grava o lançamento evita ler o relógio de novo depois da escrita. | Um lançamento recebido às 23:59:59,9 cujo commit termina depois da meia-noite fica com a `BusinessDate` e o `recorded_at_utc` do momento em que foi processado. A diferença é de milissegundos, nunca de um dia inteiro. | Aceita | Para a auditoria do comerciante, importa a diferença entre o instante em que a data é definida e o instante do commit? |
| A-020 | Para corrigir um lançamento, registra-se um novo lançamento de natureza oposta, com uma descrição; nada é alterado nem apagado. Exemplo: um crédito de R$ 100,00 lançado por engano é anulado por um débito de R$ 100,00 com a descrição "Correção do lançamento de 15/03". | O PDF não pede um estorno dedicado, e a tabela de lançamentos só aceita linhas novas (INV-001): corrigir só pode ser registrar um fato novo. | Não há vínculo formal, como uma FK (Foreign Key, chave estrangeira), entre a correção e o lançamento original. A correção é um lançamento comum, identificável só pela `description`. | Aceita | A correção deveria ter um campo que aponte para o lançamento original, em vez de depender do texto livre? |
| A-021 | Cada instalação atende um único comerciante. | O PDF diz "Um comerciante precisa controlar o seu fluxo de caixa diário", no singular, sem falar de vários comerciantes nem de separar os dados de um e de outro. | O código não tem nenhum campo, índice, dado no token ou filtro para separar comerciantes; cada instalação é dedicada a um só. | Aceita | Haverá uma oferta com vários comerciantes na mesma instalação? Ela exigiria o isolamento descrito no ADR-006 (decisão sobre isolamento de dados), seção "Quando revisitar". |
| A-022 | A `Idempotency-Key` é única na instalação inteira, e não por usuário. | A instalação atende um só comerciante (A-021), e um espaço único de chaves basta para os chamadores previstos. | Se dois usuários da mesma instalação usarem o mesmo valor de `Idempotency-Key`, eles colidem: com o mesmo conteúdo, o segundo recebe `200` com o `launchId` do primeiro, e não um lançamento novo; com conteúdo diferente, recebe `409`. | Aceita | Vale tornar a chave única por usuário, isto é, pelo par (`sub`, `Idempotency-Key`), já que um comerciante pode ter vários usuários (A-001)? |
| A-023 | O broker tem uma única credencial de administrador, usada pelos dois serviços, pelo script de operação que reenvia a DLQ (`scripts/ops/replay-dlq.sh`) e pelo painel de administração do RabbitMQ. | Simplicidade no Docker Compose — o mesmo padrão já aceito para o usuário administrador do banco (ADR-006). | Quem tiver essa credencial controla todo o broker, com as filas dos dois serviços, e não só o que cada script precisa. Risco registrado em `docs/06-arquitetura-alvo.md` §8. | Aceita | Vale criar, antes de produção, uma credencial de broker por serviço, só com as permissões necessárias? |
| A-024 | `launch_svc`, o usuário de banco de Lançamentos, é dono do schema onde ficam as suas tabelas (`public`, no banco `launch_db`) e, em tese, poderia desligar o trigger que impede alterar lançamentos. | As migrações automáticas (`ApplyMigrationsOnStartup`) exigem que o usuário da conexão seja dono do schema que cria e altera (`scripts/postgres/init-databases.sh`); não há um usuário só para migrações nesta entrega. | Uma credencial de Lançamentos roubada poderia, em tese, remover ou alterar o trigger (`DROP`/`ALTER`). Risco registrado em `docs/06-arquitetura-alvo.md` §8, que já separa esse papel na Arquitetura Alvo. | Aceita | Vale criar, antes de produção, um usuário de banco só para implantação e migrações? |
| A-025 | As esperas entre as novas tentativas do consumidor (5 s, 30 s e 120 s) são configuradas só em `appsettings.json` (`RabbitMq:RetryDelaysMs`) e não fazem parte do contrato da API. | São ajustes de operação das filas de espera (ADR-002, decisão sobre integração assíncrona), e não uma garantia dada a quem chama a API. | Mudar os valores não exige migração de banco nem mudança de contrato, mas num broker que já tem as filas exige apagar as três filas de espera vazias antes de religar o consumidor: o RabbitMQ recusa recriar uma fila com outro TTL (`inequivalent arg 'x-message-ttl'`). O procedimento está em `docs/07-runbooks.md` §8. | Aceita | Essas esperas deveriam virar um compromisso de operação documentado, por exemplo em `docs/07-runbooks.md`? |
| A-026 | O exportador de métricas do OpenTelemetry para o Prometheus está numa versão de pré-lançamento (`1.20.0-beta.1`). | Era a versão compatível com o SDK (Software Development Kit, o conjunto de bibliotecas) do OpenTelemetry `1.20.0`, fixado em `Directory.Packages.props` quando o código foi escrito. | Uma versão de pré-lançamento pode trazer mudanças incompatíveis numa atualização pequena, sem a garantia usual do versionamento semântico. | Aceita | Vale esperar uma versão estável do exportador antes de uma implantação real? |
| A-027 | O Docker Compose local roda com `ASPNETCORE_ENVIRONMENT=Development`, o que também liga a página Swagger e as migrações automáticas (`ApplyMigrationsOnStartup`). | Facilita a vida do avaliador: nenhuma migração manual, e a API pode ser explorada no navegador. | São facilidades de desenvolvimento que uma implantação real não deve herdar. Ficam registradas aqui para não serem confundidas com a configuração de produção. | Aceita | Não se aplica: fora de `Development`, a página Swagger e as migrações automáticas ficam desligadas por padrão, e o serviço se recusa a iniciar com a chave de desenvolvimento (`ApiContractExtensions.cs`, `PersistenceExtensions.cs` e `JwtAuthenticationExtensions.cs` dos dois serviços; alvo em `docs/06-arquitetura-alvo.md` §8). |
| A-028 | `lastAppliedEventRecordedAtUtc` mostra o `recordedAtUtc` do último evento *aplicado*, na ordem de processamento, e não o maior `recordedAtUtc` já visto. Se os eventos chegarem fora de ordem, o valor pode voltar para trás. | Os totais por dia são somas e ficam corretos em qualquer ordem de chegada (ADR-005, decisão sobre o Consolidado com consistência eventual; ADR-008, decisão sobre o PostgreSQL). Manter o campo como "o último aplicado vence", sem `GREATEST()`, preserva o significado literal de "último aplicado", sem criar um segundo significado, sutilmente diferente ("maior instante já visto"). | `lastAppliedLagSeconds` pode, por alguns instantes, parecer maior que o atraso real. Exemplo: o evento B, registrado às 10:00:01, falha e espera numa fila; o evento A, registrado às 10:00:05, é aplicado antes. Quando B é aplicado, às 10:00:40, o campo volta para 10:00:01 e o atraso mostrado é de 39 s. Os totais (`creditsInCents`, `debitsInCents`, `days[]`) nunca são afetados. | Aceita | Vale trocar para um `GREATEST()`, que nunca volta para trás, ao custo de o campo deixar de significar literalmente "o último evento aplicado"? |
| A-029 | Cada serviço abre no máximo 40 conexões com o PostgreSQL (`Maximum Pool Size=40` na connection string, em `docker-compose.yml` e em `appsettings.Development.json`). | Localmente, os dois serviços dividem um único servidor PostgreSQL, que aceita 100 conexões (`max_connections`, o padrão), 3 delas reservadas ao administrador. Sem limite, o Npgsql abre até 100 conexões por serviço; numa rajada de consultas, o servidor recusava conexões e o Consolidado respondia `503`. Com 40 + 40 = 80, sobram 17 conexões para migrações, para os scripts de `scripts/ops/` e para um `psql` de diagnóstico. | Num pico maior que 40 consultas simultâneas, a requisição espera uma conexão livre (até 15 s) em vez de falhar. Medido: 1.500 consultas, 100 em paralelo, todas com `200` (`docs/05-aspectos-transversais.md` §2). Na Arquitetura Alvo cada serviço tem o próprio servidor PostgreSQL (`docs/06-arquitetura-alvo.md` §1), e o pool é dimensionado pelo número de cópias: conexões por cópia × número máximo de cópias, abaixo do `max_connections` daquele servidor, com folga para migrações e operação. Exemplo: com no máximo 4 cópias e um servidor que aceita 200 conexões, até 40 por cópia (160 no total). | Aceita | Não se aplica: é um ajuste de operação, sem efeito no contrato da API. Revisar quando o número de cópias ou o tamanho do servidor mudar. |

## Política inicial de retenção

| Dado | Por quanto tempo fica guardado | Observação |
|---|---|---|
| Lançamentos | Enquanto o sistema existir | Sujeito a uma futura política jurídica, fiscal e de conformidade. |
| Mensagens do Outbox pendentes ou com falha | Até serem publicadas ou até alguém intervir | Nunca expiram sem aviso. |
| Mensagens do Outbox já publicadas | Enquanto forem a fonte da reconstrução do Consolidado (sem prazo) | Uma tarefa de limpeza é evolução futura e exigiria gerar os eventos a partir de `launches`, sem depender do histórico do Outbox (ver `scripts/ops/rebuild-consolidation.sh` e A-012). |
| Mensagem esperando consumo | Até o ACK | Precisa sobreviver a uma parada do consumidor. |
| DLQ | Até ser resolvida ou arquivada explicitamente | Gera alerta (`observability/prometheus/alerts.yml`) e tem procedimento de operação (`docs/07-runbooks.md` §4). |
| Inbox | Pelo menos 30 dias | Precisa durar mais que qualquer janela de reenvio. A tabela guarda as duas proteções contra duplicatas — por `event_id` (chave primária) e por `launch_id` (único) —, então apagar linhas da Inbox remove as duas juntas: não são camadas independentes. |
| Consolidado | Enquanto estiver em uso | Pode ser reconstruído a partir dos lançamentos. |

## Fora do escopo

Itens fora do escopo do produto, nesta entrega e na Arquitetura Alvo:

| Item | Por que fica fora |
|---|---|
| Interface gráfica para o comerciante | O PDF pede serviços e um relatório, não telas. Cada API tem uma página Swagger para testes. |
| Cadastro de usuários, login, recuperação de senha ou um provedor de identidade de produção | Identidade fica num provedor externo (A-006). Nesta entrega, um script gera tokens de desenvolvimento (ADR-007). |
| Várias moedas e conversão de câmbio | O PDF não menciona outras moedas (A-003). |
| Várias contas ou estabelecimentos por comerciante | O PDF descreve um único fluxo de caixa por comerciante (A-021). |
| Lançamentos com data passada ou futura | A data é sempre a do dia do registro, definida pelo servidor (INV-003, INV-004). |
| Editar ou apagar lançamentos | Um lançamento nunca muda (INV-001); a correção é um novo lançamento de natureza oposta (A-020). |
| Implantação em produção, em várias regiões ou em Kubernetes | O PDF pede instruções para rodar localmente (MR-07); o desenho de produção está em `docs/06-arquitetura-alvo.md`. |
| Migração concreta de um sistema legado | Não há dados de origem para migrar (A-007); a estratégia condicional está em `docs/06-arquitetura-alvo.md` §10. |
| Política definitiva de retenção exigida por lei ou regulação | Exige a participação das áreas jurídica e de conformidade; a política inicial está na seção anterior. |

## Fora do escopo desta entrega

Itens que o PDF do desafio não pede e que, por isso, foram compreendidos e não implementados
nesta entrega. A "interface gráfica para o comerciante" da seção anterior é um produto para o
comerciante final, como um painel de fluxo de caixa; as páginas de teste abaixo seriam só
ferramentas para o desenvolvedor.

| Item | Por que não foi implementado (o que o PDF pede) | Como seria na arquitetura alvo |
|---|---|---|
| Páginas de teste servidas por cada serviço e um painel local de apoio ao desenvolvedor | O PDF não pede. Seriam ferramentas para explorar a API sem terminal, sem papel no negócio do comerciante; a página Swagger de cada serviço e os exemplos com `curl` do README cumprem esse papel. | Um frontend separado, se existisse, usaria só o contrato REST (Representational State Transfer, o estilo comum de API sobre HTTP) documentado; a arquitetura alvo não depende de nenhuma ferramenta de desenvolvimento. |
| Estorno dedicado (endpoint `/reversal`, campo `reversalOfLaunchId`, regra "não se estorna um estorno") | O PDF não pede. A tabela `launches` já só aceita linhas novas (INV-001), e uma correção — um novo lançamento de natureza oposta, com `description` (A-020) — resolve o caso sem uma operação nem um vínculo dedicados. | Vínculo explícito entre o estorno e o lançamento original, garantido pelo banco (chave estrangeira composta por `merchant_id` e `launch_id`). |
| Eliminar duplicatas pelo identificador da transação no sistema de origem (`ExternalTransactionId`/`SourceSystem`) | O PDF não pede. A `Idempotency-Key` já impede que a repetição de uma mesma requisição crie dois lançamentos, que é o caso que precisamos cobrir. | Ao integrar maquininhas — POS (Point of Sale, ponto de venda) — e adquirentes reais, cada um com seu próprio identificador de transação, eliminar duplicatas também por esse identificador, além da `Idempotency-Key`. Exemplo: se a maquininha reenviar a venda 98765 com uma nova `Idempotency-Key`, só o identificador da transação na origem mostra que é a mesma venda. |
| Vários comerciantes na mesma instalação (multi-tenancy: identificação do comerciante no token, `merchant_id` no início de toda chave e índice, filtro de acesso por comerciante, limites de requisições e métricas por comerciante) | O PDF descreve um único comerciante ("Um comerciante precisa controlar o seu fluxo de caixa diário") e não pede atender vários na mesma instalação. | O provedor de identidade informa o comerciante no token; `merchant_id` no início de toda chave e índice de negócio; RLS (Row-Level Security, filtro de linhas aplicado pelo próprio PostgreSQL) como defesa em profundidade, isto é, uma segunda barreira; limites de requisições e métricas por comerciante. |
| Perfis de usuário (`Operator`/`Viewer`/`Administrator`), `audience` por API e um provedor de identidade real | A permissão (scope) já distingue o que cada chamador pode fazer; o PDF não pede perfis diferentes para pessoas nem um provedor de identidade de produção. | Provedor de identidade real com OIDC (OpenID Connect, padrão de login sobre o OAuth 2.0), emitindo tokens RS256 com as chaves públicas num JWKS; um `audience` por API como segunda barreira (um token válido para um serviço seria rejeitado pelo outro); perfis de usuário convertidos em permissões (scopes). |

## Questões abertas

| ID | Questão | Por que muda a arquitetura |
|---|---|---|
| OQ-001 | Qual é o ambiente de destino e quais são as restrições da empresa? | Muda a escolha dos serviços de nuvem e a estimativa de custos (ver `docs/08-custos.md`). |
| OQ-002 | Que restrições de ambiente, de plataforma e de tecnologia da empresa precisam ser respeitadas? | Pode mudar detalhes de implantação, custos e bibliotecas; as fronteiras entre os serviços e os contratos já estão definidos. |

## Rastreabilidade inicial

| Necessidade | Consequência na arquitetura | Decisão |
|---|---|---|
| O Consolidado não pode derrubar Lançamentos | Tirar o Consolidado do caminho do registro | ADR-001 (dois serviços independentes) e ADR-002 (integração por mensagens) |
| Um lançamento confirmado não pode se perder | Gravação durável e publicação que pode ser retomada depois de uma falha | ADR-003 (Outbox) |
| O cliente pode repetir uma requisição | API idempotente e restrições de unicidade no banco | ADR-004 (processamento idempotente) |
| O broker pode entregar a mesma mensagem mais de uma vez | Consumidor idempotente | ADR-004 |
| O Consolidado aceita alguns segundos de atraso | Consistência eventual e processamento assíncrono | ADR-002 e ADR-005 (Consolidado com consistência eventual) |
| Pessoas e sistemas acessam as APIs | Provedor de identidade externo, validação do token JWT e permissões (scopes) | ADR-007 (provedor de identidade externo) |
| O Consolidado é uma tabela de totais que pode ser descartada e recalculada | Reconstrução a partir dos lançamentos, feita por um operador; o broker não é um arquivo permanente | ADR-005 |

A matriz completa — requisito do desafio → documento ou ADR → código → teste ou evidência — está
em [`docs/09-rastreabilidade.md`](09-rastreabilidade.md).
