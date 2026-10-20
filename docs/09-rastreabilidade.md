# Matriz de rastreabilidade — desafio × repositório

Esta matriz liga cada item do PDF do desafio (desafio-arquiteto-solucao-jun25) ao lugar do
repositório que o atende. **Cada código aponta para um trecho do PDF, citado palavra por palavra
na coluna "Texto do PDF".** O PDF não numera os seus itens: os códigos foram criados por nós, só
para que a documentação possa citar cada trecho sem repeti-lo. Nenhuma linha fica sem resposta:
quando algo deliberadamente não foi implementado, a linha diz isso e aponta o documento que
registra a decisão.

## Como ler a matriz

**Os códigos.** Cada família corresponde a uma seção do PDF:

| Família | Seção do PDF de onde vem o trecho | Exemplo |
|---|---|---|
| BR (requisito de negócio, business requirement) | "Requisitos de negócio" e "Descritivo da Solução" | BR-01: "Serviço que faça o controle de lançamentos" |
| MR (requisito obrigatório, mandatory requirement) | "Requisitos obrigatórios" — se não forem atendidos, o teste é descartado | MR-06: "Testes" |
| NFR-A, NFR-B, NFR-C (requisitos não funcionais do PDF) | "Requisitos não funcionais", um parágrafo dividido em três frases | NFR-C: "com no máximo 5% de perda de requisições" |
| DF (diferencial) | "Requisitos diferenciais" — opcionais, contam pontos | DF-02: "Estimativa de custos com infraestrutura e licenças" |
| OBJ (critério de avaliação, objetivo) | "Objetivo do Desafio" — o que o avaliador observa — e um trecho de "Papel do Arquiteto de Soluções" (OBJ-10) | OBJ-05: segurança |
| RS (restrição) e OB (observação) | O lembrete em itálico do "Objetivo do Desafio" (RS-01) e a seção "Observações" (OB-01, OB-02) | RS-01: as premissas não precisam estar todas no código |

Outros códigos citados nas colunas — FR (requisito funcional), NFR-001 a NFR-014 (requisitos não
funcionais com metas medíveis), INV (regra que nunca pode ser violada) e A (premissa) — foram
derivados por nós a partir do PDF e estão em
[`docs/01-requisitos-e-assumptions.md`](01-requisitos-e-assumptions.md). Todas as famílias estão
explicadas no [glossário](11-glossario.md#famílias-de-códigos).

**As colunas.**

- **Texto do PDF:** o trecho original, entre aspas. Reticências (`...`) marcam partes omitidas.
- **Status:**
  - `Implementado` — roda localmente neste repositório, com evidência no código, nos testes ou
    nos dois;
  - `Documentado` — o item pede uma decisão, um mapeamento ou um registro, e o próprio documento
    é a entrega; não há código a apontar, por natureza, e não por falta;
  - `Documentado na arquitetura alvo` — a decisão e o desenho estão registrados, mas não rodam
    localmente: o item é condicional, pede infraestrutura de produção que este ambiente não tem,
    ou é por definição uma evolução futura.
- **Onde é respondido:** a seção da documentação ou o ADR (Architecture Decision Record, registro
  de decisão de arquitetura) que trata do item.
- **Evidência de código:** os arquivos ou diretórios que implementam o item.
- **Evidência de teste:** o teste automatizado, o script ou o relatório que prova o
  comportamento.

## Requisitos de negócio (BR)

| ID | Texto do PDF | Status | Onde é respondido | Evidência de código | Evidência de teste |
|---|---|---|---|---|---|
| BR-01 | "Serviço que faça o controle de lançamentos" | Implementado | `docs/01-requisitos-e-assumptions.md` (FR-001, FR-002, INV-001 a INV-007); `docs/02-capacidades-e-dominios.md` (contexto Launch Management); `docs/03-contratos.md` (API de registro e consulta) | `src/LaunchService/Features/RecordLaunch/`, `src/LaunchService/Features/GetLaunch/`, `src/LaunchService/Domain/Launch.cs` | `tests/LaunchService.IntegrationTests/RecordLaunchTests.cs`, `tests/LaunchService.UnitTests/Domain/LaunchTests.cs` |
| BR-02 | "Serviço do consolidado diário" (seção "Requisitos de negócio"); "também precisa de um relatório que disponibilize o saldo diário consolidado" (seção "Descritivo da Solução") | Implementado | `docs/01-requisitos-e-assumptions.md` (FR-003, FR-006); `docs/03-contratos.md` (`days[]`, um item por dia do intervalo) | `src/ConsolidationService/Features/GetConsolidation/`, `src/ConsolidationService/Domain/` (totais por dia) | `tests/ConsolidationService.IntegrationTests/GetConsolidationTests.cs`, `tests/ConsolidationService.UnitTests/Domain/ConsolidationCalculatorTests.cs` |

## Requisitos obrigatórios (MR)

| ID | Texto do PDF | Status | Onde é respondido | Evidência de código | Evidência de teste |
|---|---|---|---|---|---|
| MR-01 | "Mapeamento de domínios funcionais e capacidades de negócio" | Documentado | `docs/02-capacidades-e-dominios.md` (mapa de capacidades, contextos delimitados, mapa de contextos, criticidade e valor) | — (documento de arquitetura, sem código correspondente) | — |
| MR-02 | "Refinamento do Levantamento de requisitos funcionais e não funcionais" | Documentado | `docs/01-requisitos-e-assumptions.md` (FR-001 a FR-007, NFR-001 a NFR-014, INV-001 a INV-007, A-001 a A-029, glossário, fora do escopo) | — | — |
| MR-03 | "Desenho da solução completo (Arquitetura Alvo)" | Documentado | `docs/06-arquitetura-alvo.md` (componentes, alta disponibilidade, perda máxima de dados e tempo de recuperação, escalabilidade, cache, segurança, transição); `docs/diagrams/`: o modelo C4 (forma padronizada de desenhar arquitetura em quatro níveis de zoom), feito no Structurizr, com 6 visões | — | — |
| MR-04 | "Justificativa na decisão/escolha de ferramentas/tecnologias e de tipo de arquitetura" | Documentado | `docs/adr/ADR-001` (tipo de arquitetura), `ADR-002` (broker), `ADR-008` (PostgreSQL), `ADR-009` (.NET, Minimal APIs e Entity Framework Core), `ADR-010` (observabilidade), `ADR-011` (onde rodam o publisher e o consumidor) | — | — |
| MR-05 | "Pode ser feito na linguagem que você domina" | Implementado | `global.json` (.NET 10), `*.csproj` (C#) | `src/LaunchService/`, `src/ConsolidationService/` | `dotnet build CashFlow.sln` |
| MR-06 | "Testes" | Implementado | `README.md` ("10. Testes automatizados e de carga") | `tests/LaunchService.UnitTests/`, `tests/LaunchService.IntegrationTests/`, `tests/LaunchService.OutboxPublisherTests/`, `tests/ConsolidationService.UnitTests/`, `tests/ConsolidationService.IntegrationTests/` | `dotnet test CashFlow.sln` |
| MR-07 | "Readme com instruções claras de como a aplicação funciona, e como rodar localmente" | Implementado | `README.md` (arquitetura e decisões; pré-requisitos; execução local; token; testes pelo Swagger, pelo Postman e pelo terminal; testes automatizados e de carga; limitações) | — | — |
| MR-08 | "Hospedar em repositório publico (GitHub)" | Implementado (falta só publicar) | `.gitignore` (nada impede a publicação) | `.git/`, `.gitignore` | — (só verificável depois da publicação, fora deste repositório local) |
| MR-09 | "Todas as documentações de projeto devem estar no repositório" | Implementado | `docs/`, `docs/adr/`, `docs/diagrams/` | `docs/`, `docs/adr/`, `docs/diagrams/` | — |

## Requisitos não funcionais explícitos (NFR-A/B/C)

| ID | Texto do PDF | Status | Onde é respondido | Evidência de código | Evidência de teste |
|---|---|---|---|---|---|
| NFR-A | "O serviço de controle de lançamento não deve ficar indisponível se o sistema de consolidado diário cair" | Implementado | `docs/01-requisitos-e-assumptions.md` (NFR-001); `docs/adr/ADR-001`, `ADR-002`; `docs/diagrams/07-sequence-outage-recovery.md` | `src/LaunchService/` nunca referencia `ConsolidationService`; o `/health/ready` de Lançamentos só depende do próprio banco (`src/LaunchService/Infrastructure/Observability/`) | `tests/LaunchService.IntegrationTests/` roda com o broker inacessível (porta fechada no ambiente de teste); `scripts/e2e/smoke.sh`, passo `nfr001_consolidation_down` |
| NFR-B | "Em dias de picos, o serviço de consolidado diário recebe 50 requisições por segundo" | Implementado | `docs/01-requisitos-e-assumptions.md` (NFR-002 e NFR-011, com a nota sobre a ambiguidade: 50 consultas ou 50 lançamentos por segundo) | `src/ConsolidationService/Features/GetConsolidation/` | `tests/load/consolidation-50rps.js`, relatório `docs/evidence/load/2026-10-19-consolidation.md`; janela completa de 15 minutos (A-013, NFR-011) em `docs/evidence/load/2026-10-20-launches.md` |
| NFR-C | "com no máximo 5% de perda de requisições" | Implementado | `docs/01-requisitos-e-assumptions.md` (NFR-003 e a premissa A-004: perda é requisição que falha temporariamente, nunca dado financeiro perdido) | O Outbox e a Inbox garantem que nenhum lançamento confirmado se perde, mesmo que uma requisição falhe para o cliente (`src/LaunchService/Infrastructure/Outbox/`, `src/ConsolidationService/Infrastructure/Messaging/`) | `tests/load/consolidation-50rps.js` (limite do k6, a ferramenta de teste de carga: `http_req_failed < 5%`), relatório `docs/evidence/load/2026-10-19-consolidation.md` (0% medido) |

## Requisitos diferenciais (DF)

| ID | Texto do PDF | Status | Onde é respondido | Evidência de código | Evidência de teste |
|---|---|---|---|---|---|
| DF-01 | "Desenho da solução da Arquitetura de Transição (se necessária, considerando uma migração de legado)" | Documentado na arquitetura alvo | `docs/06-arquitetura-alvo.md` §10 (estratégia condicional: cópia do histórico, leitura em paralelo para comparar, troca definitiva e volta atrás); `docs/01-requisitos-e-assumptions.md` (A-007) | — (condicional; o desafio não fornece um sistema legado, A-007) | — |
| DF-02 | "Estimativa de custos com infraestrutura e licenças" | Documentado | `docs/08-custos.md` (custo por componente; cenários base, de crescimento e de pico; ambientes; licenças) | — | — |
| DF-03 | "Monitoramento e Observabilidade" | Implementado | `docs/05-aspectos-transversais.md` §3; `docs/adr/ADR-010` | `src/LaunchService/Infrastructure/Observability/`, `src/ConsolidationService/Infrastructure/Observability/`, `observability/prometheus/`, `observability/grafana/` | `docs/10-roteiro-de-testes.md` (cenário 12, "Observabilidade": verificação manual das métricas, do painel e dos alertas no Prometheus e no Grafana) |
| DF-04 | "Critérios de segurança para consumo (integração) de serviços" | Implementado | `docs/05-aspectos-transversais.md` §1 (ameaças e controles); `docs/06-arquitetura-alvo.md` §8 | `src/LaunchService/Infrastructure/Auth/`, `src/ConsolidationService/Infrastructure/Auth/`, `scripts/postgres/init-databases.sh` (um usuário de banco por serviço) | `tests/LaunchService.IntegrationTests/JwtAuthenticationTests.cs`, `tests/ConsolidationService.IntegrationTests/JwtAuthenticationTests.cs`, `tests/LaunchService.IntegrationTests/DatabaseIntegrityTests.cs` |

## Expectativas de avaliação (OBJ)

| ID | Texto do PDF | Status | Onde é respondido | Evidência de código | Evidência de teste |
|---|---|---|---|---|---|
| OBJ-01 | "Compreensão dos Requisitos de Negócios: ... entender profundamente os requisitos funcionais e não funcionais ... capacidades necessárias para gerar valor" | Documentado | `docs/01-requisitos-e-assumptions.md`; `docs/02-capacidades-e-dominios.md` (criticidade e valor de cada capacidade) | — | — |
| OBJ-02 | "Arquitetura Corporativa: ... definir processos, etapas e responsabilidades de forma isolada ou integrada" | Documentado | `docs/02-capacidades-e-dominios.md` (contextos delimitados, mapa de contextos); `docs/adr/ADR-001`, `ADR-011`; a separação aparece no código como dois serviços, `src/LaunchService/` e `src/ConsolidationService/` | — | — |
| OBJ-03 | "Escalabilidade: ... dimensionamento horizontal, balanceamento de carga e estratégias de cache" | Implementado | `docs/06-arquitetura-alvo.md` §6 (mais cópias, criação automática de cópias, sinal para desacelerar) e §7 (cache: a decisão de não usar por ora, e como seria) | Cada cópia do publisher reserva um lote de mensagens que nenhuma outra pode pegar ao mesmo tempo (`SELECT ... FOR UPDATE SKIP LOCKED`, em `src/LaunchService/Infrastructure/Outbox/OutboxPublisherService.cs`); isso permite várias cópias | `tests/LaunchService.OutboxPublisherTests/OutboxThroughputTests.cs`, `docs/evidence/load/2026-10-19-launches.md` |
| OBJ-04 | "Resiliência: ... redundância, failover, monitoramento proativo e estratégias de recuperação" | Implementado | `docs/06-arquitetura-alvo.md` §4 (alta disponibilidade), §9 (falhas); `docs/07-runbooks.md`; `docs/05-aspectos-transversais.md` §2 | `scripts/ops/reconcile.sh`, `scripts/ops/replay-dlq.sh`, `scripts/ops/rebuild-consolidation.sh`; `observability/prometheus/alerts.yml` | `scripts/e2e/smoke.sh --recovery`, `tests/ConsolidationService.IntegrationTests/ConsumerTests.cs` |
| OBJ-05 | "Segurança: ... autenticação, autorização, criptografia e mecanismos de proteção contra ataques" | Implementado | `docs/05-aspectos-transversais.md` §1; `docs/06-arquitetura-alvo.md` §8; `docs/adr/ADR-006`, `ADR-007`, `ADR-012` | `src/LaunchService/Infrastructure/Auth/`, `src/*/Infrastructure/RateLimiting/`, o trigger que impede alterar lançamentos (migração `AddAppendOnlyTrigger`) | `tests/*/JwtAuthenticationTests.cs`, `tests/LaunchService.IntegrationTests/RateLimitingTests.cs`, `tests/LaunchService.IntegrationTests/DatabaseIntegrityTests.cs` |
| OBJ-06 | "Padrões Arquiteturais: ... Considere trade-offs entre simplicidade e flexibilidade" | Documentado | `docs/adr/ADR-001` (dois serviços, comparados a monolito, serverless, arquitetura orientada a serviços com barramento central, mais microsserviços e Event Sourcing, cada alternativa com o caso em que seria a escolha certa) | — | — |
| OBJ-07 | "Integração: ... protocolos, formatos de mensagem e ferramentas de integração" | Implementado | `docs/03-contratos.md`: APIs REST (Representational State Transfer, estilo de API sobre HTTP) com JSON, o evento `LaunchRecorded.v1` com versão e as regras de compatibilidade; `docs/adr/ADR-002` | `src/LaunchService/Infrastructure/Messaging/`, `src/ConsolidationService/Infrastructure/Messaging/CashFlowTopology.cs` | `tests/LaunchService.OutboxPublisherTests/`, `tests/ConsolidationService.IntegrationTests/ConsumerTests.cs` |
| OBJ-08 | "Requisitos Não-Funcionais: ... desempenho, disponibilidade e confiabilidade. Defina métricas e metas claras" | Implementado | `docs/01-requisitos-e-assumptions.md` (NFR-005, NFR-012, NFR-013, NFR-014); `docs/05-aspectos-transversais.md` §3 (métricas) | `src/*/Infrastructure/Observability/` | `docs/evidence/load/2026-10-19-*.md` (p95 e p99, os tempos abaixo dos quais ficam 95% e 99% das respostas; atraso do Consolidado, `consumer_lag_seconds`, medido) |
| OBJ-09 | "Documentação: Registre decisões arquiteturais, diagramas e fluxos de dados" | Documentado | `docs/adr/` (12 ADRs); `docs/diagrams/` (modelo C4 no Structurizr, com 6 visões; 5 diagramas de sequência; mapa de capacidades em `docs/02`); `docs/06-arquitetura-alvo.md` §3 (fluxos de dados) | — | — |
| OBJ-10 | "tornar essas soluções escaláveis, reutilizáveis e flexíveis" (seção "Papel do Arquiteto de Soluções") | Documentado | `docs/adr/ADR-002` (o broker atrás de uma interface), `docs/03-contratos.md` (evento com versão); `README.md` (nota sobre o código repetido de propósito nos dois serviços, em "14. Documentação e estrutura do repositório") | `src/LaunchService/Infrastructure/Messaging/IEventPublisher.cs` (interface que esconde o broker) | — |

## Observações e restrições (RS/OB)

| ID | Texto do PDF | Status | Onde é respondido | Evidência de código | Evidência de teste |
|---|---|---|---|---|---|
| RS-01 | "Lembrando que não é necessário que todas essas premissas sejam apresentadas na codificação, mas nas decisões e representações arquiteturais do projeto" | Documentado | `docs/01-requisitos-e-assumptions.md` (premissas, cada uma com motivo, impacto e a pergunta que faríamos ao negócio, mesmo sem código); `docs/06-arquitetura-alvo.md` (decisões de produção não implementadas localmente, registradas mesmo assim) | — | — |
| OB-01 | "Use o teste para demonstrar sua habilidade em tomar decisões sobre o que é importante durante a definição de soluções para o problema de negócio" | Documentado | `docs/01-requisitos-e-assumptions.md` (tabela de premissas e "Fora do escopo"); `docs/adr/` (a seção "Alternativas consideradas" de cada ADR) | — | — |
| OB-02 | "Também são bem vindas descrições sobre o que você gostaria de ter implementado ou evoluções futuras para o sistema proposto" | Documentado na arquitetura alvo | `README.md` ("13. Limitações conhecidas e evoluções futuras"); `docs/06-arquitetura-alvo.md`, com o que fica para produção e não roda localmente: conexões criptografadas, filtro de ataques na entrada, tokens assinados por um provedor de identidade real, cluster do RabbitMQ e cópia síncrona do banco | — | — |

## Critérios de aceitação do desafio (enunciado "Caso os requisitos técnicos obrigatórios não sejam minimamente atendidos, o teste será descartado")

Os critérios de aceitação abaixo decorrem das regras e dos requisitos de
`docs/01-requisitos-e-assumptions.md` (INV-001, INV-005, NFR-001, BR-02):

| Critério | Evidência de teste |
|---|---|
| Repetir a requisição depois de um timeout devolve o mesmo `launchId`, sem um segundo lançamento | `tests/LaunchService.IntegrationTests/RecordLaunchTests.cs` (repetição com a mesma `Idempotency-Key`) |
| Reusar a chave com outro conteúdo devolve `409` | `tests/LaunchService.IntegrationTests/RecordLaunchTests.cs` (`idempotency_payload_mismatch`) |
| O lançamento continua aceito com o `ConsolidationService` parado | `scripts/e2e/smoke.sh` (`nfr001_consolidation_down`) |
| Uma mensagem entregue de novo pelo broker não é somada duas vezes | `tests/ConsolidationService.IntegrationTests/ConsumerTests.cs` |
| Um evento inválido chega à DLQ (Dead Letter Queue, fila das mensagens que não puderam ser processadas) sem perder o lançamento | `tests/ConsolidationService.IntegrationTests/ConsumerTests.cs` |
| A consulta calcula corretamente o saldo de abertura, os créditos, os débitos, a variação e o saldo de fechamento, inclusive num período sem movimento, e recusa uma data futura | `tests/ConsolidationService.IntegrationTests/GetConsolidationTests.cs`, `tests/ConsolidationService.UnitTests/Domain/ConsolidationCalculatorTests.cs` |
| `dotnet test` e o fluxo local documentado rodam sem nenhum serviço externo pago | `CashFlow.sln`, `docker-compose.yml` (só imagens de código aberto), `README.md` ("10. Testes automatizados e de carga") |

## Verificação

Todo caminho de arquivo citado nesta matriz foi conferido com:

```bash
grep -oE '`[a-zA-Z0-9_./-]+\.(cs|md|sh|js|yml)`' docs/09-rastreabilidade.md | tr -d '`' | sort -u | while read -r p; do [ -e "$p" ] || echo "MISSING: $p"; done
```

Os diretórios citados sem extensão — por exemplo, `src/LaunchService/Features/RecordLaunch/` —
foram conferidos à mão, com `ls`, porque o comando acima só cobre arquivos.
