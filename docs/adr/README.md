# Registros de decisão de arquitetura

## Finalidade

Cada ADR (Architecture Decision Record, registro de decisão de arquitetura) registra uma decisão
importante: o problema, o que decidimos e por quê, as alternativas descartadas, os trade-offs e
as consequências. Os ADRs não substituem os requisitos nem os diagramas; ligam um ao outro.
Siglas, códigos (como NFR-A ou A-021) e termos técnicos estão explicados no
[glossário](../11-glossario.md).

## Estado de uma decisão

- Proposta
- Aceita
- Substituída
- Rejeitada

## Índice

| ADR | O que foi decidido | Estado |
|---|---|---|
| [ADR-001](ADR-001-estilo-arquitetural.md) | Dois serviços separados — Lançamentos e Consolidado —, cada um com o próprio banco, para que o Consolidado fora do ar nunca impeça o registro de lançamentos. | Aceita |
| [ADR-002](ADR-002-integracao-assincrona.md) | Os dois serviços se comunicam só por mensagens, através do RabbitMQ; Lançamentos nunca espera resposta do Consolidado. | Aceita |
| [ADR-003](ADR-003-transactional-outbox.md) | O lançamento e a mensagem a publicar são gravados na mesma transação (o Outbox) e publicados depois, para que nenhum lançamento confirmado fique sem evento. | Aceita |
| [ADR-004](ADR-004-processamento-idempotente.md) | Uma restrição de unicidade do banco impede lançamento duplicado (pela `Idempotency-Key`) e soma duplicada no Consolidado (pelo `eventId` e pelo `launchId`). | Aceita |
| [ADR-005](ADR-005-eventual-consistency.md) | O Consolidado guarda uma linha de totais por dia, atualizada a cada evento, e pode ficar alguns segundos atrás dos Lançamentos. | Aceita |
| [ADR-006](ADR-006-isolamento-de-dados.md) | Cada serviço acessa o PostgreSQL com um usuário próprio, sem privilégio de administrador, que só conecta ao próprio banco; uma instalação atende um único comerciante. | Aceita |
| [ADR-007](ADR-007-identity-provider-externo.md) | Um provedor de identidade externo emite os tokens, e cada serviço valida o token e o scope a cada requisição. | Aceita |
| [ADR-008](ADR-008-postgresql.md) | PostgreSQL nos dois serviços, porque o próprio banco garante transação tudo ou nada, ausência de duplicatas, imutabilidade dos lançamentos e publicação segura com várias cópias. | Aceita |
| [ADR-009](ADR-009-dotnet-minimal-apis.md) | .NET 10 (versão com suporte longo), com Minimal APIs e Entity Framework Core para o acesso ao banco. | Aceita |
| [ADR-010](ADR-010-observabilidade.md) | Métricas com OpenTelemetry e Prometheus, um painel no Grafana, logs em JSON com `correlationId` e nove alertas, tudo rodando localmente. | Aceita |
| [ADR-011](ADR-011-topologia-de-processos.md) | O publisher do Outbox e o consumidor rodam dentro do processo da API do próprio serviço: dois programas para implantar, e não quatro. | Aceita |
| [ADR-012](ADR-012-borda-gateway-waf-cache.md) | Gateway, WAF (Web Application Firewall) e balanceador de carga ficam na Arquitetura Alvo; localmente, cada serviço limita as requisições por chamador; não usamos cache por ora. | Aceita |
