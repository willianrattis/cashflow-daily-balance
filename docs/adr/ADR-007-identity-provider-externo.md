# ADR-007 — Identity Provider externo e validação do JWT (JSON Web Token) em cada serviço

- **Status:** Aceita
- **Data:** 2026-10-19

## Contexto

O sistema precisa aceitar pessoas e integrações, e o PDF avalia "autenticação, autorização,
criptografia e mecanismos de proteção contra ataques" (OBJ-05, critério de avaliação do PDF).
Login, emissão de credenciais e recuperação de conta não são o foco do desafio; construir um
provedor de identidade aumentaria muito o código sem mostrar nenhuma decisão relevante.

## Decisão

Decidimos que um provedor de identidade externo — o IdP (Identity Provider) — emite os tokens de
acesso e que cada serviço valida o token sozinho, a cada requisição, porque assim nenhum serviço
implementa login e nenhum depende de outro componente para aceitar uma chamada.

- **O token** é um JWT: um texto assinado que diz quem chama (`sub`), para qual API vale (`aud`,
  a audience), até quando vale (`exp`) e o que o chamador pode fazer (`scope`).
- **A validação.** Cada serviço age como resource server do OAuth 2.0 (padrão em que a API
  recebe e valida tokens, sem emiti-los) e confere, localmente, a assinatura, o emissor
  (`issuer`), a audience, a validade e o scope exigido pelo endpoint.
- **A autorização** não tem perfis de pessoas; só três scopes: `cashflow.entries.write`
  (registrar), `cashflow.entries.read` (consultar lançamentos) e `cashflow.consolidation.read`
  (consultar o Consolidado).
- **Uma audience só.** Os dois serviços aceitam a mesma audience (`cashflow-api`): esta entrega
  expõe uma única API, e não duas identidades que precisem recusar o token uma da outra.
- **Fora dos serviços:** UI (User Interface, interface gráfica), login, emissão de tokens de
  produção, renovação de tokens e cadastro de usuários.
- **Tokens de desenvolvimento.** `scripts/dev-jwt/make_token.py` gera tokens assinados em
  HS256 (HMAC com SHA-256: quem assina e quem valida usam a mesma chave secreta,
  `Jwt:SigningKey`). Só serve para esta demonstração local. A Arquitetura Alvo
  (`docs/06-arquitetura-alvo.md` §8) usa RS256 (RSA com SHA-256); ES256 (curva elíptica com
  SHA-256) funcionaria do mesmo jeito, se o provedor escolhido preferir. Nos dois, o IdP assina com uma chave privada, e os serviços validam com a chave pública que ele
  publica num endereço padrão, o JWKS (JSON Web Key Set, conjunto de chaves públicas).

## Alternativas consideradas

- **IdP externo e validação local do JWT em cada serviço (escolhida).** Autenticação padrão de
  mercado sem escrever login, e cada serviço se protege sozinho.
- **Identidade no próprio serviço** (cadastro, senha e emissão de tokens dentro de Lançamentos).
  Não dependeríamos de um sistema externo. Descartamos porque está fora do escopo e aumenta o
  risco: guardar senhas e emitir tokens é o que um IdP faz melhor. Seria a escolha certa só sem
  nenhum IdP disponível.
- **Chaves de API** (um segredo fixo por cliente, enviado em cada requisição). Simples.
  Descartamos porque não carregam scopes nem validade, e a troca é manual — inadequado para
  pessoas e integrações na mesma API. Seria a escolha certa para poucas integrações, sem
  pessoas.
- **JWT validado só no API Gateway** (a porta de entrada valida o token e os serviços aceitam
  tudo o que chega). Ganharíamos validação num único lugar. Descartamos porque não basta: se
  alguém chegar a um serviço sem passar pelo gateway — por exemplo, outro container na mesma
  rede interna, ou uma regra de rede errada —, o serviço aceitaria qualquer requisição. Validando
  também em cada serviço, temos defesa em profundidade: duas barreiras, e a falha de uma não
  expõe os dados. Seria aceitável só se a rede garantisse que nada chega aos serviços sem passar
  pelo gateway — garantia que não queremos assumir.
- **`dotnet user-jwts`** (ferramenta do .NET que gera tokens de desenvolvimento e guarda a chave
  de assinatura nos "user secrets" da máquina). Evitaria manter um script próprio. Mantivemos o
  script em Python por três motivos: a ferramenta cria uma chave por projeto, e aqui os dois
  serviços precisam aceitar o mesmo token, com a mesma chave; a chave fica nos user secrets da
  máquina do desenvolvedor, que os containers do `docker compose` não leem; e ela exige
  configurar a autenticação pela seção que ela mesma preenche (`Authentication:Schemes:Bearer`),
  em vez da configuração `Jwt:*` que os dois serviços usam. Seria a escolha certa para um único
  serviço rodando fora de containers.

## Consequências

**Positivas:**

- Bom, porque nenhum serviço implementa login, cadastro ou recuperação de senha.
- Bom, porque os scopes distinguem o que cada chamador pode fazer, sem perfis de pessoas (FR-005,
  requisito: aceitar operações de pessoas e de integrações).

**Negativas:**

- Ruim, porque a plataforma depende da configuração correta do IdP e da publicação e troca
  periódica das chaves.
- Ruim, porque, com a chave compartilhada do ambiente local, quem a conhece cria tokens válidos
  para os dois serviços. Aceitável só como demonstração local.

## Trade-offs

Aceitamos uma chave HS256 compartilhada nesta demonstração, para não precisar de um IdP local, e
documentamos o risco. A Arquitetura Alvo não repete essa troca.

## Failure modes

- Um token revogado continua válido até expirar. Encurtar a validade — o TTL (Time To Live,
  tempo de vida), hoje 60 minutos nos tokens de desenvolvimento — e consultar o IdP a cada
  requisição (introspection), se o ambiente exigir, ficam como decisões futuras.
- Uma queda do IdP não impede validar tokens já emitidos, desde que os serviços tenham as chaves
  públicas em cache. Os serviços nunca emitem tokens por conta própria como plano B.

## Quando revisitar

Se pessoas precisarem de permissões diferentes das integrações (por exemplo, um operador que não
pode consultar o registro de auditoria), introduzir perfis (`Operator`/`Viewer`/`Administrator`)
ligados a regras de autorização, além dos scopes. Se a plataforma crescer para várias APIs, com
times e implantações independentes, dar a cada API sua própria audience, como segunda barreira:
um token emitido para uma API seria recusado pela outra. As duas mudanças se somam a esta
decisão, sem desfazê-la.

## Rastreabilidade

FR-005; NFR-006 (toda operação é auditável); NFR-007 (autenticar e autorizar pessoas e sistemas);
A-006 (identidade fica num IdP externo);
[contratos de autenticação](../03-contratos.md#autenticação-e-cabeçalhos).
