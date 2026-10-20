# ADR-012 — Borda: gateway, WAF (Web Application Firewall), balanceamento e cache

- **Status:** Aceita
- **Data:** 2026-10-19

## Contexto

O PDF pede "mecanismos de proteção contra ataques" e cita "dimensionamento horizontal,
balanceamento de carga e estratégias de cache" entre as premissas não funcionais. Mas também diz
que "não é necessário que todas essas premissas sejam apresentadas na codificação" (RS-01,
restrição do PDF): são decisões de arquitetura a documentar, não necessariamente a implementar
no ambiente local. Esta entrega roda num único Docker Compose, com uma cópia de cada serviço;
não há uma entrada pública para proteger nem cópias entre as quais distribuir a carga.

## Decisão

Decidimos colocar a proteção e a distribuição de carga na entrada do sistema só na Arquitetura
Alvo, e manter na entrega local apenas um limitador de requisições dentro de cada serviço,
porque, sem cópias nem acesso público, um gateway local não teria o que proteger nem o que
balancear.

**Arquitetura Alvo.** Na entrada do sistema fica um API Gateway com WAF (filtro que bloqueia
ataques conhecidos, como injeção de SQL). Ele faz quatro coisas: encerra o HTTPS, isto é, a
conexão criptografada por TLS (Transport Layer Security) termina nele; aplica as regras do WAF;
limita quantas requisições cada cliente pode fazer; e escolhe para qual cópia do serviço enviar
cada requisição, olhando o caminho HTTP e evitando as cópias que não respondem à verificação de
saúde. Cada serviço roda com pelo menos 2 cópias (réplicas). Detalhes em
`docs/06-arquitetura-alvo.md` §§1, 7 e 8. Não adotamos cache por ora: a consulta do Consolidado
já lê totais prontos, e o teste de carga mostra latência muito abaixo da meta sem cache: 95% das
consultas responderam em até 6,1 ms, e a meta é 500 ms (`docs/06` §6).

**Entrega local.** Não há container de gateway. Cada serviço usa o limitador de requisições
que já vem no framework web do .NET (ASP.NET Core): ele conta as requisições por chamador
autenticado (o campo `sub` do token) e responde `429` acima do limite. É uma segunda barreira para
tráfego autenticado, não um substituto do gateway: uma requisição sem token recebe `401` antes de
chegar ao limitador, e conter uma enxurrada delas é papel do gateway e do WAF na Arquitetura Alvo.
Os limites, e por que têm esses valores, estão em `docs/05-aspectos-transversais.md` §1.

## Alternativas consideradas

- **Limitador de requisições dentro de cada serviço, na entrega local (escolhida).** Contém um
  cliente autenticado descontrolado ou malicioso mesmo sem gateway, sem custo e sem pacote novo
  (já vem no framework). Não faz o que o gateway faz: HTTPS, WAF e distribuição de carga; e vale
  por serviço, não para a entrada inteira.
- **API Gateway local** (porta de entrada única que encerra o HTTPS e encaminha cada requisição
  ao serviço certo). Ganharíamos um ponto único para HTTPS e roteamento. Não filtra conteúdo
  malicioso nem guarda respostas. Descartamos na entrega local porque, com uma cópia de cada
  serviço, não há o que rotear entre cópias. É a escolha da Arquitetura Alvo.
- **WAF local.** Ganharíamos o bloqueio de padrões de ataque conhecidos antes dos serviços. Não
  substitui a validação de entrada feita pelos próprios serviços. Descartamos na entrega local
  porque é um serviço gerenciado ou licenciado, sem equivalente útil fora de um ambiente de
  produção. É a escolha da Arquitetura Alvo.
- **Balanceador de carga local** (distribui as requisições entre as cópias de um serviço). Não
  limita o abuso de um cliente específico. Descartamos na entrega local porque há uma única cópia
  de cada serviço. É a escolha da Arquitetura Alvo.
- **Cache** (guardar respostas para não consultar o banco de novo). Reduziria a carga no banco
  num pico de consultas repetidas, mas cria o problema de invalidar a resposta guardada quando o
  dado muda. Descartamos porque a latência já tem folga sem cache (`docs/06` §6). Seria a escolha
  certa se a evidência de carga mostrasse a consulta perto da meta.

## Consequências

**Positivas:**

- Bom, porque o limitador contém o abuso de um chamador autenticado mesmo sem nenhum componente
  de entrada local (OBJ-05, critério de avaliação do PDF: proteção contra ataques).
- Bom, porque a Arquitetura Alvo documenta a proteção e o balanceamento reais sem forçar uma
  implementação local que nenhuma cópia extra justificaria.

**Negativas:**

- Ruim, porque, sem gateway local, não há ponto único de HTTPS nem WAF nesta entrega: o tráfego
  interno roda na rede do Docker, como registrado em `docs/01-requisitos-e-assumptions.md`
  § "Fora do escopo desta entrega".
- Ruim, porque o limitador não protege contra um ataque distribuído, feito com muitos chamadores
  diferentes; isso é papel do WAF e do gateway na Arquitetura Alvo.

## Trade-offs

Aceitamos documentar gateway, WAF e balanceador só na Arquitetura Alvo, sem subir nenhum deles
localmente, para não acrescentar componentes que uma topologia de cópia única não exercitaria de
verdade. O limitador cobre a parte do requisito ("proteção contra ataques") que faz sentido
demonstrar sem uma entrada pública.

## Failure modes

- Um ataque distribuído por muitos chamadores autenticados não é contido pelo limitador: por
  desenho, ele conta por chamador, não pela entrada inteira.
- Adotar cache cedo demais, sem pressão real de carga, trocaria um problema que não existe por
  outro (a invalidação). Reavaliar só se a evidência de carga (`docs/06` §6) mudar.

## Quando revisitar

Quando a implantação passar a ter pelo menos 2 cópias de cada serviço ou receber tráfego externo
direto, subir o gateway, o WAF e o balanceador da Arquitetura Alvo, em vez de depender só do
limitador. Revisitar o cache apenas se a evidência de carga mudar.

Gatilho objetivo para a proteção contra ataques distribuídos por muitos chamadores (Failure modes
acima): antes do primeiro ambiente com mais de um cliente ou credencial exposto à internet, ou no
primeiro incidente real de `429` em mais de um chamador ao mesmo tempo — o que vier primeiro.
Até lá, o limite por chamador é defesa suficiente para o tráfego autenticado de uma única
instalação de avaliação.

## Rastreabilidade

OBJ-05; NFR-011 (perfil de escrita assumido: 50 lançamentos por segundo);
`docs/05-aspectos-transversais.md` §1; `docs/06-arquitetura-alvo.md` §§1, 6, 7 e 8.
