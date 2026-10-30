/*
 * Modelo C4 do Sistema de Fluxo de Caixa (Structurizr DSL).
 * Um único modelo gera todas as visões: contexto (C1), containers da entrega e da
 * arquitetura alvo (C2), transição, deployment local e deployment alvo.
 *
 * Convenções:
 *   tag "Alvo"      -> existe só na arquitetura alvo (não roda no docker compose)
 *   tag "Entrega"   -> relação que só existe na entrega local (sem gateway)
 *   tag "Transicao" -> existe só no cenário hipotético de migração de legado
 *
 * Abrir localmente:
 *   docker run -it --rm -p 8080:8080 -v "$PWD/docs/diagrams/structurizr:/usr/local/structurizr" structurizr/structurizr local
 *   -> http://localhost:8080
 */
workspace "Fluxo de Caixa" "Controle de lançamentos e consolidado diário de um comerciante." {

    model {
        comerciante = person "Comerciante" "Registra os créditos e débitos do dia e consulta o saldo diário consolidado."

        idp = softwareSystem "Identity Provider (OIDC) (alvo)" "Autentica pessoas e sistemas e emite os tokens de acesso (JWT), assinados com par de chaves (RS256). Publica as chaves públicas (JWKS). Na entrega local, é substituído por tokens de desenvolvimento assinados com uma chave secreta compartilhada (HS256)." "Externo,Alvo"

        legado = softwareSystem "Sistema legado de caixa (transição)" "Sistema que registrava os lançamentos antes. Hipotético: só existe no cenário de transição." "Externo,Transicao"

        cashflow = softwareSystem "Sistema de Fluxo de Caixa" "Registra os lançamentos, que são a fonte da verdade, e mantém os totais de cada dia, calculados a partir deles." {

            gateway = container "API Gateway + WAF (alvo)" "Porta de entrada única: encerra o HTTPS (TLS), filtra ataques conhecidos (WAF), limita as requisições de cada cliente e escolhe, pelo caminho HTTP, para qual cópia de cada serviço enviar." "Azure Application Gateway WAF_v2 (ilustrativo)" "Alvo"

            entryService = container "CashFlowEntryService" "Valida o pedido, impede lançamentos duplicados (Idempotency-Key) e grava o lançamento e a mensagem a publicar (Outbox) na mesma transação. O publisher do Outbox roda no mesmo processo e publica os eventos." ".NET 10, ASP.NET Core Minimal APIs, EF Core"

            cashFlowEntryDb = container "Cash Flow Entry DB" "Fonte da verdade: cash_flow_entries (só aceita linhas novas, um trigger recusa UPDATE e DELETE) e outbox_messages (as mensagens a publicar)." "PostgreSQL 17" "Database"

            broker = container "RabbitMQ" "A exchange cashflow.events entrega à fila consolidation.cashflow-entry-recorded.v1. Filas de espera retry.1 a retry.3, com tempo de espera (TTL), e a fila das mensagens que não puderam ser processadas (DLQ), consolidation.cashflow-entry-recorded.v1.dlq." "RabbitMQ 4.1, AMQP 0-9-1" "Broker"

            consolidationService = container "ConsolidationService" "Consome CashFlowEntryRecorded v1: grava a Inbox e os totais do dia na mesma transação e só confirma a mensagem (ACK) depois do commit. Responde à consulta do saldo diário." ".NET 10, ASP.NET Core Minimal APIs, EF Core"

            consolidationDb = container "Consolidation DB" "Totais por dia, calculados a partir dos eventos e reconstruíveis: daily_consolidations, inbox_messages (eventos já aplicados) e projection_metadata." "PostgreSQL 17" "Database"

            prometheus = container "Prometheus" "Coleta as métricas e avalia as regras de alerta: Outbox parado, Consolidado atrasado, mensagem na DLQ, fila crescendo, erros, lentidão e serviço sem resposta." "Prometheus 3" "Observabilidade"

            grafana = container "Grafana" "Painel de operação." "Grafana 11" "Observabilidade"

            backfill = container "Job de importação (backfill) (transição)" "Programa em lote, fora da API pública: grava o lançamento e a mensagem do Outbox numa transação por lançamento, com a data e o instante do legado e a Idempotency-Key legacy-<id>. Usa um usuário de banco próprio, ligado só durante a janela de migração." "A definir" "Transicao"

            shadow = container "Comparação sombra (transição)" "Compara, dia a dia, o saldo do legado com o do novo Consolidado durante a validação. Decide a troca definitiva (cutover) ou a volta ao legado (rollback)." "A definir" "Transicao"
        }

        # Visão de contexto (C1): relações no nível do sistema, declaradas antes das de container
        # para que o Structurizr não gere relações implícitas duplicadas.
        comerciante -> cashflow "Registra lançamentos e consulta o saldo diário consolidado" "HTTPS/JSON"
        cashflow -> idp "Valida os tokens com as chaves públicas do provedor (JWKS)" "HTTPS" "Alvo"
        cashflow -> legado "Importa histórico e compara saldos (só na transição)" "A definir" "Transicao"

        # Entrega local: o comerciante (ou um cliente HTTP) chama as APIs diretamente.
        comerciante -> entryService "Registra lançamento (Idempotency-Key)" "HTTP/JSON, JWT Bearer (sem TLS no compose)" "Entrega"
        comerciante -> consolidationService "Consulta o saldo diário consolidado" "HTTP/JSON, JWT Bearer (sem TLS no compose)" "Entrega"

        # Arquitetura alvo: tudo passa pelo gateway; tokens vêm do IdP.
        comerciante -> gateway "Registra lançamentos e consulta o consolidado" "HTTPS/JSON" "Alvo"
        comerciante -> idp "Autentica-se" "OIDC" "Alvo"
        gateway -> entryService "Encaminha registros e consultas de lançamento" "HTTPS, JWT Bearer" "Alvo"
        gateway -> consolidationService "Encaminha consultas do consolidado" "HTTPS, JWT Bearer" "Alvo"
        entryService -> idp "Busca as chaves públicas (JWKS), guardadas em memória, para validar a assinatura do token" "HTTPS" "Alvo"
        consolidationService -> idp "Busca as chaves públicas (JWKS), guardadas em memória, para validar a assinatura do token" "HTTPS" "Alvo"

        # Núcleo: mesmo mecanismo na entrega e na arquitetura alvo — só o protocolo de rede muda
        # (sem TLS no compose local x TLS no alvo; ver diagrams/README.md "Convenções").
        entryService -> cashFlowEntryDb "Grava o lançamento e a mensagem do Outbox na mesma transação. O publisher reserva um lote de mensagens pendentes (FOR UPDATE SKIP LOCKED)" "SQL/TCP (sem TLS no compose)" "Entrega"
        entryService -> cashFlowEntryDb "Grava o lançamento e a mensagem do Outbox na mesma transação. O publisher reserva um lote de mensagens pendentes (FOR UPDATE SKIP LOCKED) [alvo]" "PostgreSQL (TLS)" "Alvo"
        entryService -> broker "Publica CashFlowEntryRecorded v1 e só a marca como publicada depois da confirmação do broker (mandatory + publisher confirms)" "AMQP 0-9-1 (sem TLS no compose)" "Entrega"
        entryService -> broker "Publica CashFlowEntryRecorded v1 e só a marca como publicada depois da confirmação do broker (mandatory + publisher confirms) [alvo]" "AMQPS" "Alvo"
        consolidationService -> broker "Consome CashFlowEntryRecorded v1 e confirma (ACK) só depois do commit. As falhas vão para as filas de espera retry.1 a retry.3 ou para a DLQ" "AMQP 0-9-1 (sem TLS no compose)" "Entrega"
        consolidationService -> broker "Consome CashFlowEntryRecorded v1 e confirma (ACK) só depois do commit. As falhas vão para as filas de espera retry.1 a retry.3 ou para a DLQ [alvo]" "AMQPS" "Alvo"
        consolidationService -> consolidationDb "Grava a Inbox e os totais do dia na mesma transação. A consulta lê uma visão fixa dos dados (REPEATABLE READ)" "SQL/TCP (sem TLS no compose)" "Entrega"
        consolidationService -> consolidationDb "Grava a Inbox e os totais do dia na mesma transação. A consulta lê uma visão fixa dos dados (REPEATABLE READ) [alvo]" "PostgreSQL (TLS)" "Alvo"

        # Observabilidade.
        prometheus -> entryService "Coleta /metrics" "HTTP"
        prometheus -> consolidationService "Coleta /metrics" "HTTP"
        prometheus -> broker "Coleta métricas das filas" "HTTP (plugin rabbitmq_prometheus)"
        grafana -> prometheus "Consulta métricas" "HTTP/PromQL"

        # Transição (hipotética).
        backfill -> legado "Lê lançamentos históricos" "A definir"
        backfill -> cashFlowEntryDb "Importa histórico com a data do legado (job de importação, só na transição)" "SQL/TCP"
        shadow -> legado "Lê o saldo diário do legado" "A definir"
        shadow -> consolidationService "Lê o saldo diário do novo Consolidado" "HTTPS/JSON"

        local = deploymentEnvironment "Local" {
            deploymentNode "Máquina do avaliador" "" "Docker Compose" {
                deploymentNode "postgres" "Um servidor, dois bancos e um usuário de banco por serviço" "postgres:17.6-alpine3.21 (porta 5433)" {
                    containerInstance cashFlowEntryDb
                    containerInstance consolidationDb
                }
                deploymentNode "rabbitmq" "" "rabbitmq:4.1.1-management-alpine (5672, painel 15672, métricas 15692)" {
                    containerInstance broker
                }
                deploymentNode "cashflowentryservice" "" "Imagem .NET 10 (porta 5101)" {
                    containerInstance entryService
                }
                deploymentNode "consolidationservice" "" "Imagem .NET 10 (porta 5102)" {
                    containerInstance consolidationService
                }
                deploymentNode "prometheus" "" "prom/prometheus:v3.0.1 (porta 9090)" {
                    containerInstance prometheus
                }
                deploymentNode "grafana" "" "grafana/grafana:11.4.0 (porta 3000)" {
                    containerInstance grafana
                }
            }
        }

        alvo = deploymentEnvironment "Alvo" {
            deploymentNode "Identity Provider" "Externo à solução" "OIDC" {
                softwareSystemInstance idp
            }
            deploymentNode "Azure — Brazil South" "Ilustrativo; ver docs/08-custos.md" "Região com 3 zonas" {
                deploymentNode "Application Gateway" "" "WAF_v2, multi-zona" {
                    containerInstance gateway
                }
                deploymentNode "Container Apps (Dedicated) — Lançamentos" "Ambiente próprio de Lançamentos" "Azure Container Apps" {
                    deploymentNode "cashflowentryservice" "Cresce com mais cópias: pelo menos 2 réplicas, em zonas diferentes" "Container" "" 2 {
                        entryServiceAlvo = containerInstance entryService
                    }
                }
                deploymentNode "Container Apps (Dedicated) — Consolidado" "Ambiente próprio do Consolidado; também roda a observabilidade" "Azure Container Apps" {
                    deploymentNode "consolidationservice" "Cresce com mais cópias: pelo menos 2 réplicas, em zonas diferentes" "Container" "" 2 {
                        consolidationServiceAlvo = containerInstance consolidationService
                    }
                    deploymentNode "prometheus" "Uma cópia, sem alta disponibilidade" "Container" {
                        containerInstance prometheus
                    }
                    deploymentNode "grafana" "Uma cópia, sem alta disponibilidade" "Container" {
                        containerInstance grafana
                    }
                }
                logAnalytics = infrastructureNode "Serviço gerenciado de logs (alvo)" "Recebe e guarda os logs JSON dos dois serviços; permite buscá-los, por exemplo pelo correlationId" "Azure Monitor Log Analytics"
                deploymentNode "PostgreSQL Flexible Server — CashFlowEntry" "Alta disponibilidade entre zonas: cópia de espera síncrona, para nenhum lançamento confirmado se perder (RPO 0)" "Azure Database for PostgreSQL" {
                    cashFlowEntryDbPrimary = deploymentNode "primary" "" "D2ds_v5" {
                        containerInstance cashFlowEntryDb
                    }
                    cashFlowEntryDbStandby = infrastructureNode "Cópia de espera síncrona (standby)" "Em outra zona. Vira o servidor principal automaticamente se o principal cair (failover)" "PostgreSQL, D2ds_v5"
                }
                deploymentNode "PostgreSQL Flexible Server — Consolidation" "Totais reconstruíveis a partir do Outbox" "Azure Database for PostgreSQL" {
                    consolidationDbPrimary = deploymentNode "primary" "" "D2ds_v5" {
                        containerInstance consolidationDb
                    }
                    consolidationDbReplica = infrastructureNode "Réplica de leitura assíncrona (read replica)" "Troca manual. Perder alguns segundos de dados é aceitável, porque os totais podem ser reconstruídos" "PostgreSQL, D2ds_v5"
                }
            }
            cashFlowEntryDbPrimary -> cashFlowEntryDbStandby "Copia cada gravação de forma síncrona: o commit só termina depois que a cópia confirma" "Streaming replication"
            consolidationDbPrimary -> consolidationDbReplica "Copia as gravações de forma assíncrona, com alguns segundos de atraso" "Streaming replication"
            entryServiceAlvo -> logAnalytics "Envia os logs" "HTTPS"
            consolidationServiceAlvo -> logAnalytics "Envia os logs" "HTTPS"

            deploymentNode "CloudAMQP" "Cluster dedicado, com nós em zonas diferentes" "RabbitMQ, quorum queues" "" 3 {
                containerInstance broker
            }
        }
    }

    views {
        systemContext cashflow "C1-Contexto" "Quem usa o sistema e de quais sistemas externos ele depende." {
            include *
            exclude "element.tag==Transicao"
            autolayout lr
        }

        container cashflow "C2-Entrega" "O que roda no docker compose e como as partes se comunicam: por chamada, esperando a resposta, ou por mensagem." {
            include *
            exclude "element.tag==Alvo" "element.tag==Transicao" "relationship.tag==Alvo"
            autolayout lr
        }

        container cashflow "C2-Alvo" "Arquitetura Alvo: gateway com filtro de ataques (WAF) na entrada e provedor de identidade real. O núcleo é o mesmo da entrega." {
            include *
            exclude "element.tag==Transicao" "relationship.tag==Entrega"
            autolayout lr
        }

        container cashflow "Transicao" "Migração de um sistema legado hipotético: o job de importação grava direto no Cash Flow Entry DB, e a comparação em paralelo (leitura sombra) segue até a troca definitiva (cutover)." {
            include legado backfill shadow entryService cashFlowEntryDb broker consolidationService consolidationDb
            exclude "relationship.tag==Alvo"
            autolayout lr
        }

        deployment cashflow "Local" "Deployment-Local" "Containers e portas do docker-compose.yml." {
            include *
            exclude "relationship.tag==Alvo"
            autolayout lr
        }

        deployment cashflow "Alvo" "Deployment-Alvo" "Cópias, zonas de disponibilidade e troca automática (failover) na nuvem. Ilustrativo." {
            include *
            exclude "relationship.tag==Entrega"
            autolayout lr
        }

        styles {
            element "Person" {
                color #ffffff
                shape Person
                background #08427b
            }
            element "Software System" {
                color #ffffff
                background #1168bd
            }
            element "Container" {
                color #ffffff
                background #438dd5
            }
            element "Database" {
                shape Cylinder
            }
            element "Broker" {
                shape Pipe
            }
        }
    }
}
