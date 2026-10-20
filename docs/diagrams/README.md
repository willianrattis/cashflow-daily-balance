# Diagramas

Esta pasta reúne todos os diagramas da solução: a arquitetura no modelo C4 (forma padronizada de
desenhar arquitetura em quatro níveis de zoom), em imagens SVG (Scalable Vector Graphics), e os
diagramas de sequência, que mostram passo a passo os mecanismos internos. O índice abaixo diz que
pergunta cada diagrama responde.

## Índice

| Diagrama | Pergunta respondida | Fonte |
|---|---|---|
| C1 (nível 1 do C4, contexto) | Quem usa o sistema e de quais sistemas externos ele depende? | [svg/C1-Contexto.svg](svg/C1-Contexto.svg) |
| C2 (nível 2 do C4, containers) — Entrega | O que roda no `docker compose` e como as partes se comunicam? | [svg/C2-Entrega.svg](svg/C2-Entrega.svg) |
| C2 — Alvo | Como a Arquitetura Alvo — gateway com filtro de ataques na entrada e provedor de identidade real — muda o núcleo da entrega? | [svg/C2-Alvo.svg](svg/C2-Alvo.svg), também em [docs/06 §1](../06-arquitetura-alvo.md#1-visão-geral) |
| Transição | Como migrar de um sistema legado hipotético sem interromper o registro? | [svg/Transicao.svg](svg/Transicao.svg), também em [docs/06 §10](../06-arquitetura-alvo.md#10-arquitetura-de-transição-df-01) |
| Implantação — local | Quais containers, com quais imagens e portas, o avaliador sobe com `docker compose`? | [svg/Deployment-Local.svg](svg/Deployment-Local.svg) |
| Implantação — alvo | Como zonas de disponibilidade, cópias dos serviços, cópia de espera do banco, troca automática (failover), observabilidade e o serviço gerenciado de logs se distribuem na nuvem ilustrativa? | [svg/Deployment-Alvo.svg](svg/Deployment-Alvo.svg) |
| Sequência — registro | Como funciona o fluxo normal, sem falhas: registro → Outbox → consumo → totais do dia? | [05-sequence-register-outbox.md](05-sequence-register-outbox.md) |
| Sequência — timeout e nova tentativa | Como uma nova tentativa do cliente, com a mesma `Idempotency-Key`, evita um segundo lançamento? | [06-sequence-timeout-idempotency.md](06-sequence-timeout-idempotency.md) |
| Sequência — indisponibilidade | Como o registro continua com o Consolidado fora do ar, e como as mensagens acumuladas são processadas quando ele volta? | [07-sequence-outage-recovery.md](07-sequence-outage-recovery.md) |
| Sequência — DLQ (Dead Letter Queue) e reenvio | O que acontece quando o consumidor falha de forma inesperada: filas de espera `retry.1` a `retry.3`, depois a DLQ, a fila das mensagens que não puderam ser processadas, e o reenvio manual? | [08-sequence-dlq-replay.md](08-sequence-dlq-replay.md) |
| Sequência — falha temporária | Por que o banco do Consolidado fora do ar nunca manda uma mensagem para a DLQ, e o que o publisher faz com o broker fora do ar? | [09-sequence-transient-retry.md](09-sequence-transient-retry.md) |
| Mapa de capacidades | Que capacidades de negócio pertencem a cada domínio e serviço, e o que é fonte da verdade e o que é calculado a partir dela? | [docs/02-capacidades-e-dominios.md](../02-capacidades-e-dominios.md) |

## Convenções

- **Um modelo C4, uma ferramenta.** Os diagramas C4 — contexto, containers e implantação — saem de
  um único modelo, escrito na DSL (Domain-Specific Language, a linguagem própria) do Structurizr
  ([`structurizr/workspace.dsl`](structurizr/workspace.dsl)) e convertido em SVG. O nível C3 (nível
  3 do C4, componentes) não foi desenhado: os diagramas de sequência já mostram os mecanismos
  internos de cada container.
- **Sequências em Mermaid.** Os diagramas de sequência e o mapa de capacidades
  ([docs/02](../02-capacidades-e-dominios.md)) são escritos em Mermaid, dentro de arquivos
  Markdown; o GitHub os desenha sem nenhuma ferramenta extra.
- **Só o nome diz o que é alvo ou transição.** Todo sistema ou container que só existe na
  Arquitetura Alvo, ou só no cenário hipotético de transição, termina o nome com "(alvo)" ou
  "(transição)". Exemplos: `API Gateway + WAF (alvo)`, `Identity Provider (OIDC) (alvo)`,
  `Sistema legado de caixa (transição)` e `Comparação sombra (transição)`. Esse sufixo é o único
  sinal: nas SVGs, cor, borda e estilo de seta não distinguem alvo, transição e entrega local. Um
  elemento sem sufixo roda no `docker compose`.
- **A legenda mostra só os tipos de elemento.** Cada SVG tem uma linha de legenda por tipo de
  elemento do C4 (por exemplo, `person`, `system` e `container`). Quem diz se um elemento é alvo ou
  transição é o nome, nunca a legenda.
- **Cada diagrama existe numa só forma.** Nenhum diagrama é mantido em duas ferramentas.

## Abrir o modelo localmente

```sh
docker run -it --rm -p 8080:8080 \
  -v "$PWD/docs/diagrams/structurizr:/usr/local/structurizr" \
  structurizr/structurizr local
# -> http://localhost:8080
```

Só o `structurizr/workspace.dsl` é editado à mão; os `.svg` de `svg/` são gerados a partir dele.

## Gerar as SVGs de novo

```sh
scripts/diagrams/render.sh
```

O script valida o modelo, converte cada visão para PlantUML (`plantuml/c4plantuml`) e desenha o
SVG com imagens Docker fixadas por digest (o identificador único de uma versão da imagem):
`structurizr/structurizr` e `plantuml/plantuml`. Não é preciso instalar Java, Structurizr nem
PlantUML. Rodar o script duas vezes sobre o mesmo `workspace.dsl` gera SVGs idênticos, sem
diferença no `git diff`.
