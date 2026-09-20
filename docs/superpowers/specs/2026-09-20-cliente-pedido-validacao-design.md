# Spec: Validação robusta + testes unitários — fluxo cadastro de cliente e criação de pedido (BotService)

Data: 2026-09-20

## Contexto

O fluxo de cadastro de cliente e criação de pedido acontece via `BotService.ProcessarMensagem`, uma máquina de estados por conversa do WhatsApp (`EtapaConversa`). Hoje não há validação de CPF/CNPJ, e-mail, nome ou conteúdo de item de pedido; entradas inválidas ou avançam a conversa mesmo assim, ou falham silenciosamente. Não existe projeto de teste no repositório.

Os `Controllers` REST (`ClienteController`, `PedidoController`) existem mas ficam **fora de escopo** desta spec — não são o caminho usado pelo cliente final e não recebem validação nem teste aqui.

## Objetivo

1. Tornar o fluxo de conversa (cadastro de cliente + criação de pedido) resistente a entrada inválida, sem travar ou corromper dado.
2. Corrigir dois bugs de integridade de dado encontrados durante a análise (não são "validação de input", mas quebram o fluxo).
3. Criar projeto de testes unitários cobrindo `ClienteService`, `PedidoService` e `BotService`.

## Validações novas

Local: `ClienteService`, `PedidoService`, `BotService` (camada de serviço/bot — não nos repositórios).

### CPF/CNPJ
- Aceita só dígitos (remove máscara se vier com pontuação, ou rejeita se vier com caractere não numérico — decisão de implementação: normalizar removendo `.`, `-`, `/`, depois validar dígito).
- Tamanho deve ser 11 (CPF) ou 14 (CNPJ) após normalização.
- **Não** calcula dígito verificador — fora de escopo, decisão explícita do usuário.
- Inválido → bot reenvia a mesma pergunta ("CPF/CNPJ inválido, digite novamente"), permanece na etapa `AguardandoCpf`.

### Busca de cliente por CPF ou telefone (`ProcessarCpf`)
- Ao receber CPF/CNPJ válido, `BotService` busca primeiro por CPF (`IClienteService.BuscaPorCpf`). Se não encontrar, busca por `CON_TELEFONE` (`IClienteRepository.GetByTelefoneAsync`, já existe no repositório, hoje sem uso).
- Encontrado por telefone (mesmo com CPF digitado diferente do cadastrado): trata como cliente já cadastrado, segue com os dados do banco (nome, CPF, código), pula direto para `MenuCliente`. O CPF digitado nessa mensagem é descartado — não sobrescreve o cadastro.
- Não encontrado nem por CPF nem por telefone: fluxo atual, segue para `AguardandoNome` como cliente novo, usando o CPF digitado.

### E-mail
- Regex formato padrão: `^[^@\s]+@[^@\s]+\.[^@\s]+$`.
- Inválido → bot reenvia a pergunta, permanece na etapa `AguardandoEmail`.

### Nome
- Obrigatório, não vazio após `Trim()`.
- Sem validação de tamanho máximo ou caracteres especiais (fora de escopo — usuário optou por manter simples).
- Vazio → bot reenvia a pergunta, permanece na etapa `AguardandoNome`.

### Item de pedido (dentro de `ProcessarNovoPedido`, por linha recebida, não só no fechamento do pedido)
- Linha que não casa com o padrão `^(\d+)\s*(?:[xX])?\s*(.+)$` → bot responde "não entendi esse item, use o formato: 2x produto" e **não acumula** a linha no carrinho; conversa permanece na etapa `NovoPedido`.
- Quantidade extraída igual a `0` → rejeitada com a mesma mensagem de formato inválido.
- Quantidade extraída maior que `100` → rejeitada com mensagem "quantidade muito alta, confirme o valor".
- Consequência arquitetural: a extração/validação de quantidade+produto passa a acontecer linha a linha no momento em que o item chega (`ProcessarNovoPedido`), não só dentro de `PedidoService.NormalizarPedido` no fechamento. `NormalizarPedido` continua existindo e é usado para montar o corpo final (`PED_CORPO`) a partir de itens já validados e acumulados — ela deixa de ser o único ponto de validação, mas continua sendo o ponto de formatação final.

### Cliente inexistente ao finalizar pedido
- `PedidoService.CreatePedidoAsync` passa a receber (ou consultar via `IClienteRepository`) e validar que `PED_CLICODIGO` corresponde a um cliente existente antes de inserir. Se não existir, lança `InvalidOperationException` (ou tipo de exceção de negócio equivalente já usado no projeto) — `BotService` captura e informa erro genérico ao usuário, reinicia conversa (mesmo padrão já usado quando `CON_CLICODIGO` está null em `ProcessarNovoPedido`).

## Bugs corrigidos

### 1. `PED_NUMERO` nunca preenchido
`PedidoRepository.CreatePedidoAsync` insere sem popular `PED_NUMERO`, ficando `null`. Correção: após o `INSERT` e obter `LAST_INSERT_ID()`, executar `UPDATE tb_pedido SET PED_NUMERO = @numero WHERE PED_CODIGO = @id` na mesma conexão, usando o próprio `PED_CODIGO` (auto-increment) como valor de `PED_NUMERO`. Retorna o `PED_CODIGO` como antes.

### 2. FK check desabilitado no insert de pedido
Remove `SET FOREIGN_KEY_CHECKS = 0;` / `SET FOREIGN_KEY_CHECKS = 1;` do SQL em `PedidoRepository.CreatePedidoAsync`. Combinado com a validação de cliente existente em `PedidoService` (seção acima), impede pedido órfão tanto na camada de negócio quanto no banco.

## Testes unitários

### Setup
- Novo projeto `API_WHATSAPP_SELLER.Tests` na raiz do repositório (`C:\api-whatsapp-seller\API_WHATSAPP_SELLER.Tests`), referenciando `API_WHATSAPP_SELLER.csproj`.
- Adicionado ao `API_WHATSAPP_SELLER.slnx` (raiz).
- Stack: xUnit + Moq + FluentAssertions.
- Nenhum teste toca banco real: `IClienteRepository`, `IPedidoRepository`, `ITwilioService`, `IConversaRepository` são mockados via Moq.

### Casos de teste — `ClienteService`
- CPF válido (11 dígitos) aceito.
- CNPJ válido (14 dígitos) aceito.
- CPF/CNPJ com tamanho errado (ex: 10 ou 12 dígitos) rejeitado.
- CPF/CNPJ com letra no meio rejeitado.
- CPF/CNPJ com máscara (`123.456.789-00`) normalizado e aceito se resultar em 11/14 dígitos.
- E-mail válido aceito (`nome@dominio.com`).
- E-mail sem `@` rejeitado.
- E-mail sem domínio (`nome@`) rejeitado.
- Nome vazio ou só espaço rejeitado.
- `CreateClienteAsync` com `newCliente == null` continua lançando `ArgumentNullException` (comportamento já existente, não regredir).

### Casos de teste — `PedidoService`
- `NormalizarPedido`: múltiplas linhas válidas viram itens formatados (`2X PRODUTO`).
- Linha com espaço antes do "x" (`2 x produto`) normaliza corretamente.
- Linha maiúscula (`3X PISTACHE`) normaliza corretamente.
- Linha vazia entre itens é ignorada, não quebra.
- Corpo totalmente vazio ou só espaço lança `ArgumentException`.
- Quantidade `0` em uma linha é rejeitada (não devia aparecer aqui após mudança no BotService, mas o método mantém a guarda como defesa em profundidade).
- Quantidade acima de 100 rejeitada, mesma lógica.
- `CreatePedidoAsync` com `PED_CLICODIGO` de cliente inexistente lança exceção e não chama `IPedidoRepository.CreatePedidoAsync`.

### Casos de teste — `BotService`
- Fluxo feliz completo: telefone novo → `AguardandoCpf` → CPF não encontrado → `AguardandoNome` → nome → `AguardandoEmail` → e-mail → cliente criado → `MenuCliente` → opção "1" → `NovoPedido` → item válido acumulado → "terminar" → pedido criado → confirmação enviada com código do pedido → volta pro `MenuCliente`.
- CPF já cadastrado → pula direto para `MenuCliente`, sem passar por nome/e-mail.
- CPF não encontrado, mas telefone já cadastrado (com CPF diferente) → pula direto para `MenuCliente` usando dados do cliente existente, CPF digitado descartado.
- CPF não encontrado e telefone não encontrado → segue fluxo de cliente novo (`AguardandoNome`).
- CPF inválido (tamanho errado) → conversa permanece em `AguardandoCpf`, mensagem de erro reenviada, `IConversaRepository.Atualizar` não muda a etapa.
- E-mail inválido → conversa permanece em `AguardandoEmail`, mensagem de erro reenviada.
- Item de pedido em formato não reconhecido → mensagem de erro enviada, `CON_ULTIMA_MENSAGEM` não é alterado, etapa continua `NovoPedido`.
- Item com quantidade `0` ou `> 100` → rejeitado com mensagem específica, carrinho não muda.
- "terminar" sem nenhum item no carrinho → mensagem "nenhum item foi adicionado", pedido não é criado.
- "terminar" com `CON_CLICODIGO` null → mensagem de erro de identificação, conversa reinicia para `AguardandoCpf`.
- Opção de menu inválida (diferente de "1"/"2") → mensagem de opção inválida, etapa não muda.
- Acompanhamento de pedido: código numérico válido existente retorna status; código inexistente retorna mensagem de não encontrado; "0" volta pro menu; texto não numérico pede novamente.

## Fora de escopo (explicitamente)

- Controllers REST (`ClienteController`, `PedidoController`) — sem validação, sem teste.
- Dígito verificador de CPF/CNPJ.
- Limite de tamanho/caracteres no nome do cliente.
- `_pasta_duplicada_backup/` e `slnx` duplicado dentro de `API_WHATSAPP_SELLER/` — resíduo encontrado durante análise, não tratado nesta spec.
