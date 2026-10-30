# ADR-009 — .NET 10 com Minimal APIs e Entity Framework Core

- **Status:** Aceita
- **Data:** 2026-10-19

## Contexto

O PDF deixa a linguagem livre ("Pode ser feito na linguagem que você domina", MR-05, requisito
obrigatório do PDF). A plataforma não precisa ser a melhor em tudo; precisa ser adequada ao
escopo e permitir mostrar, no prazo do desafio, os pontos de arquitetura pedidos: idempotência,
Outbox, consumo sem duplicatas e observabilidade.

## Decisão

Decidimos usar .NET 10 com Minimal APIs e Entity Framework Core porque é uma plataforma que
dominamos (MR-05) e porque o acesso a banco dela aproveita bem as restrições do PostgreSQL de
que a solução depende (ADR-008, decisão sobre o PostgreSQL).

- **.NET 10, versão LTS (Long-Term Support: correções garantidas até 14/11/2028).** Uma API
  financeira não deveria ficar presa a uma versão STS (Standard-Term Support), de suporte mais
  curto. O `global.json` fixa a versão estável — GA (General Availability) — do SDK (Software
  Development Kit, o compilador e as ferramentas do .NET) em `10.0.402`, com
  `rollForward: latestFeature`.
- **Minimal APIs do framework web do .NET (ASP.NET Core), organizadas por funcionalidade**
  (`Features/RecordEntry`, `Features/GetEntry`, `Features/GetConsolidation`), e não por
  camadas técnicas com controllers. Com três endpoints no total, camadas espalhariam cada
  mudança de regra por três ou quatro pastas, sem ganho real de reaproveitamento.
- **Entity Framework Core (EF Core) 10 com o driver Npgsql** para acessar o PostgreSQL. Cada
  tabela tem configuração explícita em código (`IEntityTypeConfiguration<T>`), em vez de
  atributos espalhados pelas classes.
- **Trabalho em segundo plano dentro do próprio serviço.** O publisher do Outbox e o consumidor
  são `BackgroundService`s: uma classe do .NET para um trabalho contínuo que roda no mesmo
  processo da API. Por isso não existe um terceiro programa para implantar; cada serviço é um
  único processo com a API e o seu trabalho em segundo plano (o ADR-011 justifica essa escolha).
- **Testcontainers** (biblioteca que sobe PostgreSQL e RabbitMQ reais, em containers, durante os
  testes) nos testes de integração, em vez de simular banco e broker; **xUnit** como framework de
  testes; **OpenAPI/Swagger** (documentação interativa da API) só no ambiente `Development`.

## Alternativas consideradas

- **.NET 10 com ASP.NET Core e EF Core (escolhida).** Ganhamos acesso a banco maduro, migrações
  versionadas com o código e testes contra infraestrutura real, no mesmo ecossistema.
- **Go** (linguagem compilada, leve, com concorrência nativa). Ganharíamos menos memória e
  partida mais rápida. Descartamos porque as bibliotecas que mapeiam objetos para tabelas — o
  ORM (Object-Relational Mapper) —, as de migração e o suporte a Testcontainers são menos
  maduros que os do EF Core para as restrições declarativas deste domínio (ADR-008). Seria a
  escolha certa para um processo de segundo plano isolado e de altíssimo volume.
- **Java com Spring Boot.** Ecossistema tão maduro quanto o do .NET. Descartamos porque o Spring
  Boot traz mais estrutura do que três endpoints pedem, e a JVM (Java Virtual Machine) parte
  mais devagar e usa mais memória por cópia — pior para várias cópias pequenas de um serviço com
  um `BackgroundService` simples. Seria a escolha certa numa empresa padronizada em Java.
- **Node.js com TypeScript.** Bom para serviços que passam a maior parte do tempo esperando rede
  e disco. Descartamos porque combinar tipagem forte, ORM transacional e Testcontainers é menos
  direto do que com EF Core e Npgsql. Seria a escolha certa para uma equipe de JavaScript.

## Consequências

**Positivas:**

- Bom, porque as Minimal APIs reduzem o código repetitivo para três endpoints, sem perder
  regras de autorização, limite de requisições por política e documentação OpenAPI.
- Bom, porque as migrações do EF Core versionam o schema dos dois bancos junto com o código e
  aceitam SQL puro quando necessário (o trigger de imutabilidade).
- Bom, porque o `BackgroundService` dispensa um processo e uma implantação a mais só para o
  publisher e o consumidor nesta escala.

**Negativas:**

- Ruim, porque, com muitos endpoints por grupo, Minimal APIs ficam mais difíceis de navegar que
  controllers. Mitigamos com um arquivo de endpoint por funcionalidade.
- Ruim, porque o EF Core traduz para SQL as consultas LINQ (Language Integrated Query), escritas
  em C#. A consulta do Consolidado foi escrita com cuidado, mas é preciso acompanhar o plano de
  execução gerado.

## Trade-offs

Aceitamos uma plataforma mais pesada que Go (partida mais lenta e mais memória por cópia) em
troca de restrições declarativas e migrações maduras o bastante para não reimplementar em código
o que o banco já garante (ADR-008). Neste domínio, essa garantia pesa mais do que a eficiência da
plataforma.

## Failure modes

- **SDK mais novo na máquina:** com `rollForward: latestFeature`, o `dotnet` usa o SDK 10.0 mais
  novo instalado que seja igual ou posterior a `10.0.402`, inclusive de uma faixa de versões
  (feature band) mais nova, como 10.0.5xx, sem travar o build. Os containers usam o SDK fixado nos
  Dockerfiles (`sdk:10.0.402`); para mudar a versão de referência, atualize o `global.json` e os
  dois Dockerfiles juntos.
- **SDK do Dockerfile diferente do `global.json`:** mitigado por um comentário nos dois
  Dockerfiles que aponta para o `global.json`.

## Quando revisitar

Se a equipe que mantiver o sistema não dominar .NET, ou se uma parte específica (por exemplo, um
processo de segundo plano de altíssimo volume) justificar uma plataforma mais leve só para ela.
O publisher do Outbox e o consumidor são pequenos o bastante para serem reescritos sozinhos, sem
afetar o resto.

## Rastreabilidade

MR-04 (requisito obrigatório do PDF: justificar ferramentas e tecnologias); MR-05; MR-06
(requisito obrigatório do PDF: testes); OBJ-06 (critério de avaliação do PDF: equilibrar
simplicidade e flexibilidade). Decisões relacionadas: [ADR-001](ADR-001-estilo-arquitetural.md),
[ADR-008](ADR-008-postgresql.md), [ADR-011](ADR-011-topologia-de-processos.md).
