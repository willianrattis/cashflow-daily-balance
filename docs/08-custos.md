# Estimativa de custos — infraestrutura e licenças

Este documento estima quanto custaria, por mês, rodar a Arquitetura Alvo
([`docs/06-arquitetura-alvo.md`](06-arquitetura-alvo.md)) em produção e nos ambientes de teste,
e quanto se paga de licença de software. Ele responde ao DF-02, diferencial do PDF: "Estimativa de
custos com infraestrutura e licenças". É um modelo **ilustrativo**: o código não depende de nenhum
provedor de nuvem, mas uma estimativa precisa de preços concretos. Por isso, este documento fixa
um provedor e uma região **como premissa**, e não como decisão de arquitetura.

## Como ler esta estimativa

- **Cenários.** *Base* (baseline) é um dia normal, com o mínimo de cópias para alta
  disponibilidade. *Crescimento* (growth) é um volume 10 vezes maior, avaliado só no papel. *Pico*
  (peak) é a carga máxima que os requisitos definem por alguns minutos (§3).
- **Faixas, e não valores exatos.** Cada linha carrega a própria incerteza; somar faixas dá uma
  faixa ainda mais larga (§7).
- **"(a validar)"** marca um preço que não veio da página oficial do fornecedor e precisa ser
  conferido na calculadora oficial antes de qualquer compromisso de orçamento (§1).
- **Nomes de produtos e planos.** O provedor de nuvem chama de SKU (Stock Keeping Unit) o nome de
  cada plano ou tamanho de serviço — por exemplo, `WAF_v2` ou `D2ds_v5`. Cada nome é explicado na
  linha em que aparece.
- **Unidades.** vCPU é um processador virtual; vCore, a mesma ideia no banco gerenciado; GiB e
  GB (gigabyte) medem memória e disco; "/h" é o preço por hora de uso.

## 1. Premissas

- **Provedor e região de referência: Azure, região Brazil South.** É um exemplo: o desafio não
  define o ambiente da empresa, e essa é a questão em aberto OQ-001 (qual é o ambiente de destino
  e quais são as restrições da empresa, em
  [`docs/01-requisitos-e-assumptions.md`](01-requisitos-e-assumptions.md#questões-abertas)). Trocar
  de provedor muda os números, e não a arquitetura: cada componente de `docs/06` tem um equivalente
  na AWS (Amazon Web Services) e no GCP (Google Cloud Platform). Por exemplo: RDS (Relational
  Database Service, o banco gerenciado da AWS) ou Cloud SQL no lugar do PostgreSQL gerenciado;
  Amazon MQ (o RabbitMQ gerenciado da AWS) ou CloudAMQP para o RabbitMQ; ALB (Application Load
  Balancer, o balanceador da AWS) com o WAF (Web Application Firewall, filtro que bloqueia ataques
  conhecidos) da AWS, ou o Cloud Armor do Google, para o gateway.
- **Moeda: dólar americano (US$), a moeda em que as fontes publicam os preços.** A conversão para
  real e os descontos de contratos corporativos com a Microsoft — EA (Enterprise Agreement) e
  CSP (Cloud Solution Provider) — são outras questões em aberto, não estimadas aqui.
- **De onde vêm os preços.** Cada linha traz a fonte e a data da consulta (2026-10-19, salvo
  indicação). As páginas oficiais de preço da Azure (`azure.microsoft.com/.../pricing/details/...`)
  montam a tabela no navegador, com JavaScript, e as duas leituras automáticas feitas para este
  documento só receberam marcadores vazios (`$-`). Elas confirmam, porém, que Brazil South e as
  moedas dólar e real existem para cada produto. Nesses casos, o preço vem de um site de terceiros
  que cita a tabela oficial da Microsoft, e a linha leva a marca **(a validar)**, com o link para
  conferência. Quando a página do próprio fornecedor publica o preço — por exemplo, a CloudAMQP —,
  a linha não leva a marca.
- **Fora da estimativa:** suporte comercial além do SLA (Service Level Agreement, o compromisso de
  disponibilidade que o provedor já dá no preço padrão), o custo das pessoas que operam o sistema,
  as taxas para tirar dados de um provedor e levar para outro, e a integração com sistemas da
  empresa — por exemplo, o login único corporativo, SSO (Single Sign-On), ou o sistema de análise
  de eventos de segurança, SIEM (Security Information and Event Management). Tudo isso depende de
  decisões que o desafio não fornece.

## 2. Dimensionamento por componente (produção)

Todo componente de [`docs/06-arquitetura-alvo.md` §1](06-arquitetura-alvo.md#1-visão-geral)
aparece abaixo. O balanceador de carga e o gateway com WAF são o mesmo produto neste modelo: em
`docs/06`, o API Gateway já escolhe o destino de cada requisição pela camada de aplicação, aplica
o WAF e encerra a conexão criptografada — TLS (Transport Layer Security), o "S" do HTTPS —, tudo
no mesmo componente. Não há uma segunda camada de balanceamento a precificar.

| Componente (docs/06 §1) | Produto ilustrativo (Azure, Brazil South) | Dimensionamento | Preço unitário | Fonte e data | Estimativa mensal |
|---|---|---|---|---|---|
| `CashFlowEntryService` (API e publisher do Outbox) | Azure Container Apps, plano Dedicated (máquinas reservadas para o ambiente, pagas enquanto existirem), num ambiente próprio | 2 cópias (réplicas) × 0,5 vCPU / 1 GiB, em pelo menos 2 zonas de disponibilidade — AZ (Availability Zone, um data center isolado dentro da região) —, como pede `docs/06` §4 | vCPU US$0,0571/h; memória US$0,0050/h; gestão do ambiente ~US$73/mês | [devzero.io, "Azure Container Apps Pricing (2026)"](https://www.devzero.io/blog/azure-container-apps-pricing), citando a tabela da Microsoft — 2026-10-19 **(a validar na calculadora oficial)** | ~US$122 |
| `ConsolidationService` (API e consumidor) | Azure Container Apps, plano Dedicated, num ambiente próprio | 2 cópias × 0,5 vCPU / 1 GiB | A mesma tabela acima | Idem | ~US$122 |
| `Cash Flow Entry DB` (banco de Lançamentos) | Azure Database for PostgreSQL Flexible Server, linha General Purpose (uso geral), com alta disponibilidade — HA (High Availability) — entre zonas, que a Azure chama de **Zone-redundant HA**: uma cópia síncrona em outra zona, a decisão da §4 de `docs/06` | `D2ds_v5` (2 vCore / 8 GB), servidor principal + cópia síncrona (standby) | ~US$130/mês por instância (processamento) | [bytebase.com/dbcost, "Azure Flexible Server Pricing"](https://www.bytebase.com/dbcost/azure-flexible-server-pricing/), "updated as of September 28, 2026" **(a validar para Brazil South — o preço de referência é global)** | ~US$260 (processamento) + US$20–50 de armazenamento e backup **(a validar)** |
| `Consolidation DB` (banco do Consolidado) | Azure Database for PostgreSQL Flexible Server, General Purpose, com uma **réplica de leitura assíncrona** (read replica) — a decisão da §4 de `docs/06`; veja a nota abaixo | `D2ds_v5`, servidor principal + 1 réplica de leitura | ~US$130/mês por instância | Idem | ~US$260 (processamento) + US$15–35 de armazenamento e backup **(a validar)** |
| RabbitMQ (cluster de 3 nós, quorum queues) | CloudAMQP, plano dedicado "Big Bunny": 3 nós em zonas diferentes, testado pelo fornecedor a 1.000 mensagens por segundo — cerca de 20 vezes os 50 por segundo medidos em `docs/evidence/load/` | 1 plano | US$297/mês | [cloudamqp.com/plans](https://www.cloudamqp.com/plans/) — preço publicado na própria página, 2026-10-19 | US$297 |
| API Gateway (TLS, WAF, limite de requisições, balanceamento) | Azure Application Gateway v2, SKU `WAF_v2` (o gateway com o WAF incluído) | Gateway fixo + 2 unidades de capacidade — CU (capacity unit), a unidade em que a Azure cobra o volume processado pelo gateway — no cenário base | Fixo US$0,443/h; US$0,0144 por CU por hora | [openappsec.io, "Understanding Azure WAF Pricing"](https://www.openappsec.io/post/azure-waf-pricing-guide), citando a tarifa de referência da região East US — 2026-10-19 **(a validar para Brazil South)** | ~US$344 |
| Observabilidade — logs | Azure Monitor, área de trabalho do Log Analytics, faixa Analytics Logs (a de consulta completa); recebe os logs dos dois serviços | ~5–10 GB por dia recebidos (suposição de volume, e não medição), ou seja, 150–300 GB por mês | US$2,30/GB (depois de 5 GB grátis por mês: (150 − 5) × 2,30 a (300 − 5) × 2,30) | [monitoringcost.com, "Azure Monitor Cost 2026"](https://monitoringcost.com/azure-monitor-cost) — 2026-10-19 **(a validar contra o volume real depois da implantação)** | ~US$335–680 |
| Observabilidade — métricas (Prometheus instalado pela equipe) | O mesmo Prometheus desta entrega (ADR-010, decisão sobre observabilidade), num container único: busca as métricas das duas APIs, ou as recebe por envio contínuo (remote-write), e avalia as 9 regras de alerta | 1 cópia (sem alta disponibilidade) × 0,5 vCPU / 1 GiB, no mesmo ambiente Dedicated do `ConsolidationService` | A mesma tabela de processamento acima; sem taxa de gestão a mais (o ambiente já existe) | — | ~US$25 |
| Observabilidade — painéis (Grafana) | O mesmo Grafana do `docker-compose.yml` (ADR-010), num container único | 1 cópia (sem alta disponibilidade) × 0,5 vCPU / 1 GiB, no mesmo ambiente Dedicated do `ConsolidationService` | A mesma tabela de processamento acima; sem taxa de gestão a mais | — | ~US$25 |
| Tráfego de saída para a internet (egress) | Saída de Brazil South, que a Azure cobra na "Zona 3" (a faixa de preço de tráfego em que fica a região) | Primeiros 100 GB por mês grátis (free tier); o cenário base fica abaixo disso | US$0,181/GB depois da parte grátis | [egresscost.com, "Azure Bandwidth & Egress Pricing"](https://egresscost.com/azure/), citando a tabela da Azure — 2026-10-19 **(a validar)** | ~US$0 no cenário base; um risco no cenário de crescimento (§3) |
| Backups extras (cópias dos volumes do broker e do Grafana) | Azure Blob Storage | Volume pequeno, guardado por pouco tempo | Faixa típica de Blob Storage, sem cotação específica | Sem fonte **(a validar)** | US$5–15 |
| Provedor de identidade (OIDC) | Externo — não entra neste modelo | Depende do provedor que a empresa escolher: Entra External ID, Auth0, Okta ou um provedor interno que já exista | Varia conforme o fornecedor, em geral por MAU (Monthly Active Users, usuários ativos por mês) | Não se aplica — decisão fora do escopo do desafio e desta estimativa (a mesma lacuna do ADR-007, decisão sobre o provedor de identidade) | **Fora do total** |

**Nota sobre o `Consolidation DB`.** A §4 de `docs/06` pede, para este banco, uma cópia assíncrona
com troca manual. No Azure Flexible Server, o produto equivalente é a **réplica de leitura**
(read replica): recebe as gravações com alguns segundos de atraso e pode ser promovida a
principal, à mão, se o principal se perder. Existe uma opção ainda mais barata: nenhuma réplica,
só backup automático e reconstrução a partir do Outbox (A-012, premissa: as mensagens publicadas
ficam guardadas no Outbox, sem prazo). A tabela usa a réplica de leitura porque ela traduz a
decisão de `docs/06` mais diretamente. A opção "só backup e reconstrução" reduziria esta linha a
~US$130, só o servidor principal, e está registrada como uma forma de economizar (§6).

## 3. Cenários

O raciocínio de capacidade — quando criar mais cópias, como frear quem envia mais do que o
consumidor processa e os números medidos — está em
[`docs/06-arquitetura-alvo.md` §6](06-arquitetura-alvo.md#6-escalabilidade). Aqui, ele só vira
custo.

- **Base (baseline), um dia normal:** a soma da tabela da §2 — o mínimo de cópias para alta
  disponibilidade (pelo menos 2 de cada serviço), sem crescer com a carga. **US$1.830–2.235 por
  mês.**
- **Crescimento (growth), 10 vezes o volume da A-013:** um cenário **só no papel, não validado**.
  A premissa A-013 (pico de escrita de 50 lançamentos por segundo durante 15 minutos) deixa claro
  que 10 vezes esse volume não é um requisito atual. Dobrar as cópias das APIs (por exemplo, de 2
  para 4 por serviço), subir um tamanho nos dois bancos e subir o plano do broker — CloudAMQP
  "Happy Hare", US$597/mês, ou "Roaring Rabbit", US$897/mês, conforme a vazão real — dobra, mais ou
  menos, a maioria das linhas. O volume de logs pode crescer mais rápido que o tráfego, se cada
  evento gerar logs proporcionais. A faixa é larga de propósito, porque a incerteza é grande:
  **US$3.600–6.900 por mês.** Antes de orçar esse cenário de verdade, é preciso rodar
  `scripts/load/run.sh entries 15m` (ou um teste mais longo) contra um ambiente dimensionado para
  10 vezes o volume, e medir em vez de estimar.
- **Pico (peak): 50 consultas por segundo no Consolidado e 50 lançamentos por segundo durante 15
  minutos** (NFR-002, requisito: o Consolidado aguenta 50 requisições por segundo; A-013). Os
  testes de carga (`docs/evidence/load/2026-10-19-*.md`) mostram que o ambiente aguenta 50
  requisições por segundo durante 2 minutos, com p95 e p99 (os tempos abaixo dos quais ficam 95% e
  99% das respostas) de poucos milissegundos, numa única cópia de desenvolvimento, com o Outbox e
  a fila vazios no fim. Ou seja, o pico já cabe na folga do cenário base, com pelo menos 2 cópias
  — a mesma conclusão de `docs/06` §6. O pico é absorvido pela criação automática de cópias, ou
  por cópias criadas antes do horário, e pela fila do RabbitMQ, que guarda o excesso
  temporariamente — sem pagar capacidade extra o tempo todo. No plano Dedicated, que já está pago,
  um pico de 15 minutos custa **~US$0** a mais. O que pesa no orçamento é o cenário base com alta
  disponibilidade, e não o pico.

## 4. Ambientes

| Ambiente | O que muda em relação à produção (cenário base) | Estimativa mensal |
|---|---|---|
| **Desenvolvimento** | Plano Consumption do Container Apps (paga só pelo uso e desliga sozinho quando ocioso, sem taxa fixa de gestão) em vez de Dedicated; PostgreSQL na linha Burstable (`B1ms`, uma máquina pequena para uso intermitente), sem alta disponibilidade, uma instância por banco; RabbitMQ no plano gratuito da CloudAMQP, "Little Lemur", ou no compartilhado "Tough Tiger" (US$19/mês); sem gateway nem WAF próprios (as requisições chegam direto aos serviços); logs dentro dos 5 GB grátis por mês na maior parte do tempo | US$50–150 |
| **Homologação** | Plano Dedicated com 1 cópia por serviço (e não 2); PostgreSQL General Purpose `D2ds_v5` sem alta disponibilidade (sem cópia de espera nem réplica); broker no plano dedicado de entrada da CloudAMQP ("Sassy Squirrel", ~US$50/mês) ou num compartilhado; gateway e WAF menores, ou nenhum, se o ambiente não for exposto à internet; logs guardados por menos tempo; desligar o ambiente fora do horário comercial reduz ainda mais o custo de processamento | US$400–700 |
| **Produção** | A topologia completa da §2, com alta disponibilidade em todos os componentes que guardam dados | US$1.830–2.235 (base) · até US$3.600–6.900 no cenário de crescimento, só no papel (§3) |

**Desligar fora do horário (desligamento agendado)** não vale para produção: Lançamentos precisa
ficar no ar mesmo quando o Consolidado não está (NFR-001, requisito: o Consolidado fora do ar não
derruba Lançamentos). Mas é o que mais economiza em desenvolvimento e homologação: um ambiente
usado só em horário comercial (cerca de 40 das 168 horas da semana) corta perto de 75% do custo de
processamento e de banco, se o desligamento for automático. Esse valor não entra nas faixas acima,
porque depende de uma política de horários que a empresa ainda não definiu.

## 5. Licenças

Todo o software deste repositório é de código aberto — OSS (Open Source Software) —, e o custo de
licença é **zero**. O que aparece na §2 é o custo de **infraestrutura gerenciada** (o provedor
cobra para operar o serviço, e não pela licença do software) ou de **suporte comercial
opcional**. São categorias de custo diferentes, e por isso ficam separadas.

As licenças citadas: MIT (MIT License) e BSD (Berkeley Software Distribution License) são
permissivas, quase sem obrigações; Apache 2.0 também; MPL (Mozilla Public License) obriga a
publicar as alterações feitas nos arquivos do próprio software; AGPL (Affero General Public
License) obriga a publicar o código alterado também quando o software é oferecido pela rede. Essa
obrigação de publicar as alterações se chama copyleft.

| Componente | Licença | Custo de licença | Observação |
|---|---|---|---|
| .NET 10 e o framework web do .NET (ASP.NET Core) | MIT | US$0 | O ambiente de execução e o SDK (Software Development Kit, o compilador e as ferramentas), sem custo em nenhum ambiente. |
| PostgreSQL | PostgreSQL License (permissiva, no estilo MIT e BSD) | US$0 | O custo na §2 é 100% infraestrutura gerenciada (processamento, armazenamento e alta disponibilidade do Azure Database for PostgreSQL), e não licença do banco. |
| RabbitMQ | MPL 2.0 | US$0 | O custo na §2 (CloudAMQP) é a operação gerenciada do cluster, e não licença do RabbitMQ. Instalar o RabbitMQ em máquinas virtuais próprias — VM (Virtual Machine) — eliminaria essa linha e manteria a licença em zero; sobraria só o custo das máquinas (§6). |
| Prometheus | Apache 2.0 | US$0 | — |
| Grafana, na edição de código aberto | AGPLv3 (desde 2021) | US$0 para instalar e operar por conta própria | Copyleft, e não cobrança: a AGPL obriga a publicar as alterações feitas no código se o Grafana modificado for oferecido como serviço, mas não impede o uso interno gratuito. O Grafana Cloud — o mesmo produto oferecido como serviço pago (SaaS, Software as a Service) pela Grafana Labs — é uma alternativa que este modelo não usa: escolhemos instalar e operar o Grafana (§2). |
| k6 | AGPLv3 | US$0 | Usado só em `scripts/load/`; não é um componente de produção. |
| Suporte comercial (opcional) | Não se aplica | Sob consulta ou contrato | Por exemplo, o suporte Enterprise de um fornecedor de PostgreSQL ou RabbitMQ gerenciado, ou um contrato de suporte da Microsoft além do SLA padrão. Não entra em nenhuma estimativa acima; só importa se a empresa exigir um compromisso contratual além do que o provedor gerenciado já oferece. |

## 6. Formas de economizar e o que cada uma custa

- **Serviço gerenciado ou instalado pela equipe — a escolha mais direta do modelo.** O broker é o
  exemplo mais claro. O plano "Big Bunny" da CloudAMQP custa US$297/mês e inclui a operação do
  cluster: atualizações, monitoramento do broker e troca automática para outro nó (failover).
  Instalar o RabbitMQ em 3 máquinas virtuais próprias na Azure (por exemplo, `D2s_v5`, ~US$70–90
  por mês cada, pelas faixas típicas de mercado, sem fonte aqui) custaria algo como US$210–270 por
  mês só em máquinas — perto do preço da CloudAMQP, ou menos. Mas a equipe passaria a manter o
  cluster, aplicar correções de segurança e responder aos incidentes do broker, algo que o
  `docker-compose.yml` local não testa, porque roda um único container. Qual lado vale mais
  depende de quanto a empresa já paga por equipe de operação de infraestrutura, uma informação que
  o desafio não fornece.
- **A alta disponibilidade é o que mais pesa em cada componente.** Na §2, o `Cash Flow Entry DB` com alta
  disponibilidade custa cerca de 2 vezes uma instância sem ela (US$260 contra ~US$130). O mesmo
  acontece, em menor grau, com as cópias das APIs (2 em vez de 1). Cortar alta disponibilidade é a
  economia de maior impacto por linha; por isso, desenvolvimento e homologação (§4) cortam primeiro
  ali, e não na observabilidade ou no broker.
- **Quanto tempo guardar os logs, e em que faixa.** A linha do Log Analytics (~US$335–680/mês no
  cenário base) depende mais de decisões de operação do que do tamanho da infraestrutura. Mover
  os logs volumosos e pouco consultados — por exemplo, os registros brutos de cada acesso — da
  faixa "Analytics Logs" (US$2,30/GB) para a faixa "Basic Logs" (US$0,50/GB, mesma fonte), mais
  barata e com consulta limitada, reduz essa linha de 4 a 5 vezes. A cobertura dos alertas não
  muda: os logs que precisam de consulta completa continuam na faixa "Analytics Logs".
- **Reservar capacidade por 1 a 3 anos** (reserved instances e savings plans). Compromissos de 1 a
  3 anos costumam reduzir de 20% a 40% o custo de processamento em relação a pagar pelo uso, sem
  compromisso (pay-as-you-go), nas nuvens públicas. É um padrão visto em outros produtos e
  provedores, e não um número cotado para Brazil South. Só faz sentido depois que o cenário base
  for confirmado com tráfego real por um tempo. Comprometer capacidade com uma estimativa ainda
  não medida — e com a OQ-001 em aberto — é o oposto do raciocínio baseado em evidência de
  `docs/06` §6.

## 7. Total mensal por cenário e ambiente

| | Desenvolvimento | Homologação | Produção — base | Produção — crescimento (10 vezes, só no papel) |
|---|---|---|---|---|
| **Faixa mensal (US$)** | US$50–150 | US$400–700 | US$1.830–2.235 | US$3.600–6.900 |

São faixas, e não uma precisão falsa. Cada linha da §2 tem a própria incerteza (com fonte ou
"a validar"), e a soma de faixas dá uma faixa mais larga que qualquer linha. É o esperado, e é
melhor do que apresentar um número único sem base.

## Rastreabilidade

Responde ao DF-02 (diferencial do PDF: "Estimativa de custos com infraestrutura e licenças").
O dimensionamento segue [`docs/06-arquitetura-alvo.md`](06-arquitetura-alvo.md) — todo componente
da §1 de lá aparece na §2 daqui — e as principais fontes de custo de
[`docs/05-aspectos-transversais.md` §4](05-aspectos-transversais.md#4-capacidade-custos-e-transição).
Evidência de capacidade: `docs/evidence/load/2026-10-19-*.md`. Questão em aberto citada: OQ-001
(ambiente de destino e restrições da empresa). Premissa citada: A-013 (volume de pico, base para
os testes e os custos, e não um requisito atual).
