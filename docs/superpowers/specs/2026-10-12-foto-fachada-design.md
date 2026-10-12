# Spec: Etapa de foto da fachada no cadastro de cliente (BotService)

Data: 2026-10-12

## Contexto

O cadastro de cliente novo acontece via `BotService.ProcessarMensagem`, máquina de estados por conversa (`EtapaConversa`): `AguardandoCpf` → `AguardandoNome` → `AguardandoEmail` → `MenuCliente`. Hoje o cliente é criado em `ProcessarEmail`.

O webhook (`WhatsappController.ReceberMensagem`) lê só `From` e `Body` do form do Twilio. Mídia chega nos campos `NumMedia`, `MediaUrl{N}`, `MediaContentType{N}` e é ignorada. `MediaUrl{N}` exige Basic Auth (`AccountSid:AuthToken`) para download, e o Twilio pode apagar a mídia depois — guardar só a URL não serve.

Deploy roda em host com disco efêmero (Render/Railway): arquivo local some no redeploy. Por isso a imagem é gravada como BLOB no MySQL.

## Objetivo

Adicionar etapa obrigatória de envio da foto da fachada no cadastro de cliente novo, após o e-mail, salvando a imagem no banco junto com o cliente de forma atômica.

## Decisões

| Tema | Decisão |
|---|---|
| Posição no fluxo | Após e-mail, antes de criar o cliente |
| Obrigatoriedade | Obrigatória. Sem foto válida, não cria cliente. Sem opção de pular ou cancelar |
| Armazenamento | BLOB (`MEDIUMBLOB`) no MySQL, tabela nova `tb_cliente_foto` |
| Tipos aceitos | `image/jpeg`, `image/png`, `image/webp` |
| Tamanho máximo | 5 MB |
| Várias mídias na mesma mensagem | Usa só a primeira (`MediaUrl0`) |
| Cliente já cadastrado (achado por CPF/telefone) | Não pede foto, segue direto pro menu como hoje |
| Sync com Bling | Fora de escopo. Spec separada no repo bling-go. Tabela já traz `FOT_SINCRONIZADO_BLING` |

## Fluxo novo

```
AguardandoCpf → AguardandoNome → AguardandoEmail → AguardandoFotoFachada → MenuCliente
```

### `ProcessarEmail` (alterado)
- Validação de e-mail inalterada.
- Válido: salva `CON_EMAIL`, etapa vira `AguardandoFotoFachada`, **não cria cliente**.
- Mensagem: "Envie uma foto da fachada do seu estabelecimento."

### `ProcessarFotoFachada` (novo)

Avaliado nesta ordem; em todo caso de erro a conversa **permanece** em `AguardandoFotoFachada`:

1. Mensagem sem mídia (`NumMedia == 0`) → "Envie uma foto (JPG, PNG ou WEBP) da fachada do seu estabelecimento."
2. `MediaContentType0` fora dos tipos aceitos → mesma mensagem do item 1. Não baixa.
3. Download via `ITwilioMidiaService.BaixarAsync(url)`:
   - Tamanho acima de 5 MB → "Foto muito grande, envie outra."
   - Erro HTTP, timeout ou exceção de rede → "Não consegui receber a foto, envie novamente." Loga o erro.
4. Sucesso: `IClienteService.CreateClienteComFotoAsync(cliente, foto)` (cliente montado igual ao `ProcessarEmail` atual: nome, e-mail, telefone normalizado, CPF).
   - Exceção → "Erro ao finalizar cadastro, envie a foto novamente." Loga o erro.
   - Ok → `CON_CLICODIGO = codigo`, etapa `MenuCliente`, mensagem "Cadastro realizado!" + menu (texto atual).

## Componentes

### `MensagemRecebida` (novo, `Models/`)
```csharp
public class MensagemRecebida
{
    public string Telefone { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public MidiaRecebida? Midia { get; set; }
}

public class MidiaRecebida
{
    public string Url { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}
```

### `WhatsappController.ReceberMensagem` (alterado)
- Lê `From`, `Body`, `NumMedia`, `MediaUrl0`, `MediaContentType0`.
- `NumMedia` ausente, não numérico ou `0` → `Midia = null`.
- Chama `IBotService.ProcessarMensagem(MensagemRecebida)`. Assinatura antiga `(string telefone, string mensagem)` é substituída; demais handlers usam `mensagem.Texto`.

### `EtapaConversa` (alterado)
- Novo valor `AguardandoFotoFachada = 8`. `AguardandoTelefone = 4` (sem uso) fica intocado para não mudar valores gravados.

### `ITwilioMidiaService` / `TwilioMidiaService` (novo)
```csharp
Task<MidiaBaixada> BaixarAsync(string url, CancellationToken ct = default);
```
- `MidiaBaixada { byte[] Conteudo; string ContentType; }`.
- `HttpClient` tipado registrado com `AddHttpClient<ITwilioMidiaService, TwilioMidiaService>`, timeout 10 s (webhook do Twilio corta em 15 s).
- Header `Authorization: Basic base64(AccountSid:AuthToken)` a partir de `TwilioSettings`.
- Limite de 5 MB: rejeita se `Content-Length` > 5 MB; se ausente, lê o stream e aborta ao passar de 5 MB. Excesso lança `MidiaMuitoGrandeException`.
- Status HTTP não-sucesso lança `HttpRequestException`.

### `FotoCliente` (novo, `Models/`)
Espelha `tb_cliente_foto`: `FOT_CODIGO`, `FOT_CLICODIGO`, `FOT_CONTEUDO` (`byte[]`), `FOT_CONTENT_TYPE`, `FOT_TAMANHO`, `FOT_SINCRONIZADO_BLING` (`bool`), `FOT_CREATED_AT`.

### `ClienteService.CreateClienteComFotoAsync` (novo)
- Valida `cliente` e `foto` não nulos (`ArgumentNullException`), igual `CreateClienteAsync`.
- Delega para `IClienteRepository.CreateClienteComFotoAsync`.

### `ClienteRepository.CreateClienteComFotoAsync` (novo)
- Uma conexão, `BeginTransaction`.
- `INSERT tb_cliente` + `SELECT LAST_INSERT_ID()` → `INSERT tb_cliente_foto` com `FOT_CLICODIGO` = id.
- Commit e retorna `CLI_CODIGO`. Erro → rollback, exceção propaga.
- `CreateClienteAsync` existente continua (usado pelo `ClienteController`).

## Banco

Migration `docs/superpowers/migrations/2026-10-12-cria-tb-cliente-foto.sql`:

```sql
CREATE TABLE tb_cliente_foto (
    FOT_CODIGO INT AUTO_INCREMENT PRIMARY KEY,
    FOT_CLICODIGO INT NOT NULL,
    FOT_CONTEUDO MEDIUMBLOB NOT NULL,
    FOT_CONTENT_TYPE VARCHAR(50) NOT NULL,
    FOT_TAMANHO INT NOT NULL,
    FOT_SINCRONIZADO_BLING TINYINT(1) NOT NULL DEFAULT 0,
    FOT_CREATED_AT DATETIME NOT NULL,
    CONSTRAINT FK_FOTO_CLIENTE FOREIGN KEY (FOT_CLICODIGO) REFERENCES tb_cliente (CLI_CODIGO)
);
```

- `MEDIUMBLOB` comporta até 16 MB, acima do limite de 5 MB.
- `max_allowed_packet` padrão do MySQL 8 (64 MB) basta.
- Sem `UNIQUE` em `FOT_CLICODIGO`: hoje só uma foto por cliente é gravada, mas a tabela não impede futuras.

## Testes

Stack atual: xUnit + Moq + FluentAssertions, sem banco real.

### `BotService`
- E-mail válido: etapa vira `AguardandoFotoFachada`, `CreateClienteAsync`/`CreateClienteComFotoAsync` **não** chamados.
- Foto: texto sem mídia → mensagem de pedido de foto, etapa mantida, download não chamado.
- Foto: `application/pdf` / `video/mp4` → rejeita sem chamar download.
- Foto: download lança `HttpRequestException` → mensagem de reenvio, etapa mantida, cliente não criado.
- Foto: download lança `MidiaMuitoGrandeException` → "Foto muito grande", etapa mantida.
- Foto: `CreateClienteComFotoAsync` lança → mensagem de erro, etapa mantida.
- Foto: sucesso → `CreateClienteComFotoAsync` chamado com dados da conversa e bytes baixados; `CON_CLICODIGO` preenchido; etapa `MenuCliente`.
- Cliente existente (por CPF e por telefone) → vai pro `MenuCliente` sem passar pela foto (regressão).
- Testes existentes adaptados à nova assinatura `ProcessarMensagem(MensagemRecebida)`.

### `TwilioMidiaService`
`HttpMessageHandler` fake:
- Envia header Basic Auth correto.
- Retorna bytes e content-type em 200.
- `Content-Length` > 5 MB → `MidiaMuitoGrandeException`.
- Stream sem `Content-Length` passando de 5 MB → `MidiaMuitoGrandeException`.
- 404/500 → `HttpRequestException`.

### `WhatsappController`
- `NumMedia=0` → `Midia == null`.
- `NumMedia=1` com URL e content-type → `Midia` preenchida.
- `NumMedia` ausente ou inválido → `Midia == null`.

### `ClienteService`
- `CreateClienteComFotoAsync` com cliente ou foto nulos → `ArgumentNullException`.

## Fora de escopo

- Envio da foto pro Bling (spec separada no bling-go; antes, verificar se a API v3 do Bling aceita imagem em contato).
- Pedir foto a cliente já cadastrado.
- Atualizar/substituir foto depois do cadastro.
- Endpoint para ler a foto.
- Validação de magic bytes (confia no `MediaContentType0` do Twilio).
- Opção de pular ou cancelar na etapa de foto.
- Testes de integração do `ClienteRepository` (transação) — exige banco real; o projeto não tem.
