# Seller integrado à central Cremollati — Design

Data: 2026-10-13
Repositórios: `api-whatsapp-seller` (branch `feat/central-cremollati`) e `central-cremollati` (branch `cremollati`).

## 1. Contexto

A central Cremollati passou a ser dona do webhook Twilio WhatsApp (canal `wabaBSP: "twilio"`). Toda mensagem
do cliente vira ticket na central. Enquanto o ticket não tem atendente, a central repassa a mensagem ao seller;
quando o ticket é fechado, avisa o seller para encerrar a conversa.

Hoje o seller recebe o webhook direto da Twilio e responde direto pela Twilio. Com a central no meio:

- o seller passa a receber o repasse da central (mesmo formato da Twilio), autenticado por um segredo
  compartilhado;
- o seller responde pela API externa da central, para a resposta aparecer no ticket;
- a mídia recebida vem de uma URL pública da central, não da Twilio;
- existe um evento novo de encerramento (reset).

O envio direto pela Twilio continua disponível por configuração, para rollback sem deploy.

## 2. Contrato central ⇄ seller

| Direção | Chamada | Formato |
|---|---|---|
| central ⇒ seller | `POST /api/whatsapp/webhook` | form-urlencoded: `From` (`whatsapp:+<DDI><número>`), `Body`, `NumMedia`, `MediaUrl0`, `MediaContentType0`, `MessageSid`; header `X-Central-Secret` |
| central ⇒ seller | `POST /api/whatsapp/reset` | form-urlencoded: `telefone` (`whatsapp:+<DDI><número>`); header `X-Central-Secret` |
| seller ⇒ central | `POST /v1/api/external/:apiId` | JSON `{ "number": "...", "body": "...", "externalKey": "<guid>" }`; header `Authorization: Bearer <token do ApiConfig>` |

- `From` e `telefone` usam o mesmo formato que a Twilio usa hoje. `tb_conversa.CON_TELEFONE` continua batendo;
  não há migração de dados.
- `number` pode ser enviado como `whatsapp:+55...`: a central normaliza.
- O repasse só acontece com ticket **sem atendente**. Atendente assumiu ⇒ o seller para de receber. Ticket
  fechado ⇒ reset.
- A central repassa com timeout de 15 s e tenta de novo (8 tentativas, backoff exponencial) quando o seller falha
  ou demora.

## 3. Mudança na central: envio síncrono para o canal Twilio

Problema: a API externa enfileira cada mensagem no job `SendMessageAPI`, com `delay: 6000` e concorrência 200.
As respostas do bot chegariam ~6 s atrasadas e mensagens seguidas poderiam chegar fora de ordem.

Mudança em `backend/src/controllers/APIExternalController.ts` (`sendMessageAPI`):

- Depois de validar `ApiConfig` e payload, carregar o canal (`Whatsapp.findByPk(APIConfig.sessionId)`).
- Se `wabaBSP === "twilio"`: chamar `SendMessageAPITwilio` na própria requisição.
  - Sucesso ⇒ `200` com `{ "messageId": "<SID da Twilio>" }`.
  - Falha ⇒ `502` com `{ "error": "<mensagem>" }` (ex.: `ERR_TWILIO_SEND: 63016`). Sem retry na central.
- Demais canais: inalterados (fila `SendMessageAPI`, resposta `{ "message": "Message add queue" }`).
- O desvio Twilio dentro do job `SendMessageAPI` permanece, para jobs que já estejam na fila.

Ordem garantida: o seller aguarda a resposta HTTP de cada envio antes do próximo; como o envio à Twilio ocorre
dentro da requisição, a ordem de chegada na Twilio é a ordem em que o bot escreveu.

Testes (Jest, `backend/src/__tests__/unit/Twilio/`): canal Twilio chama `SendMessageAPITwilio` e não enfileira;
erro vira `502`; canal não-Twilio continua enfileirando.

## 4. Mudanças no seller

### 4.1 Configuração

Nova seção `Central` (`Models/CentralSettings.cs`), registrada com `Configure<CentralSettings>`:

| Chave (env) | Uso |
|---|---|
| `Central__Canal` | `Central` ou `Twilio`. Padrão: `Twilio` (comportamento atual). |
| `Central__ApiUrl` | URL completa da API externa, ex.: `https://central-cremollati-production.up.railway.app/v1/api/external/<apiId>` |
| `Central__ApiToken` | token do `ApiConfig` (sem o prefixo `Bearer`) |
| `Central__SharedSecret` | mesmo valor de `SELLER_SHARED_SECRET` na central |

Valores reais só em variáveis de ambiente; `appsettings.json` recebe placeholders vazios.

Com `Canal=Central`, `ApiUrl`, `ApiToken` e `SharedSecret` são obrigatórios: o app falha no boot se faltar algum
(validação com `ValidateOnStart`).

### 4.2 Envio

- `Interfaces/ITwilioService.cs` vira `Interfaces/IEnvioMensagemService.cs`, mesmo método
  `Task EnviarMensagem(string telefone, string mensagem)`.
- `TwilioService` passa a implementar `IEnvioMensagemService`, sem outra mudança.
- Novo `Services/CentralMensagemService.cs`:
  - `HttpClient` tipado, timeout de 20 s.
  - `POST` em `ApiUrl` com JSON `{ number: telefone, body: mensagem, externalKey: Guid.NewGuid() }` e
    `Authorization: Bearer <ApiToken>`.
  - Resposta não-2xx ⇒ lança `InvalidOperationException` com status e corpo (sem o token).
- `Program.cs` registra `IEnvioMensagemService` conforme `Central__Canal`.
- `BotService` e `WhatsappController` trocam o tipo/nome do campo (`_twilioService` ⇒ `_envioService`). Nenhuma
  mudança nas mensagens do bot.

### 4.3 Autenticação do webhook e do reset

- Com `Canal=Central`, `POST /api/whatsapp/webhook` e `POST /api/whatsapp/reset` exigem o header
  `X-Central-Secret`. Comparação com `CryptographicOperations.FixedTimeEquals` sobre os bytes UTF-8. Ausente ou
  diferente ⇒ `401`, sem processar.
- Com `Canal=Twilio`, o webhook segue sem checagem (como hoje) e o reset responde `404`.
- Implementação: um filtro de ação (`CentralSecretAttribute`) aplicado às duas actions.

### 4.4 Duplicatas

A central repete o repasse se o seller não responder em 15 s (ex.: etapa da foto, com download + criação do
cliente). Para não processar a mesma mensagem duas vezes:

- O webhook lê `MessageSid`. Se já foi visto nos últimos 10 minutos (`IMemoryCache`), responde `200` sem chamar
  o `BotService`.
- O `MessageSid` é marcado como visto **antes** de processar.
- Sem `MessageSid` (envio manual/teste), processa normalmente.

### 4.5 Mídia

`TwilioMidiaService` vira `MidiaService` (`IMidiaService`), com regra por host da URL:

| Host | Download |
|---|---|
| `api.twilio.com` (https) | Basic auth `AccountSid:AuthToken`, como hoje |
| host de `Central__ApiUrl` (https) | sem credenciais |
| qualquer outro | `ArgumentException` (rejeita) |

Limites atuais mantidos: 5 MB e 10 s no total. `BotService` passa a depender de `IMidiaService`.

### 4.6 Reset e reabertura da conversa

- `POST /api/whatsapp/reset` (form `telefone`):
  - conversa encontrada ⇒ `CON_STATUS = Finalizada`, demais campos inalterados;
  - conversa inexistente ⇒ nada a fazer;
  - sempre `200` (idempotente).
- `IConversaRepository.Atualizar` já grava `CON_STATUS`; não precisa de método novo.
- `BotService.ProcessarMensagem`: antes do `switch` de etapas, se `CON_STATUS != Ativa`:
  - `CON_STATUS = Ativa`;
  - `CON_CLICODIGO` preenchido ⇒ `CON_ETAPA = MenuCliente`, envia saudação + menu (mesmo texto do menu atual);
  - sem cliente ⇒ `CON_ETAPA = AguardandoCpf`, envia as boas-vindas pedindo CPF/CNPJ (mesmo texto de
    `IniciarConversa`);
  - grava a conversa e retorna; o conteúdo dessa mensagem é ignorado (ela só reabre a conversa).

### 4.7 Erros

- Falha ao enviar pela central: o `BotService` loga e segue, como já faz com falha da Twilio. O webhook responde
  `200` para a central não repetir o repasse por causa de um erro de envio.
- Exceção não tratada no processamento: `500`; a central tenta de novo (protegido pela deduplicação de 4.4).

## 5. Testes do seller

xUnit + Moq, no padrão de `API_WHATSAPP_SELLER.Tests`:

- `CentralMensagemServiceTests`: payload JSON, header `Authorization`, `externalKey` único, erro em não-2xx
  (`HttpMessageHandler` fake).
- `WhatsappControllerTests`: `401` sem/errado secret com `Canal=Central`; `200` com secret certo; webhook sem
  checagem com `Canal=Twilio`; `MessageSid` repetido não chama o `BotService`; reset marca `Finalizada`.
- `MidiaServiceTests`: credenciais só para `api.twilio.com`; host da central sem `Authorization`; outro host
  rejeitado; limites mantidos.
- `BotServiceTests`: conversa `Finalizada` com cliente ⇒ menu; sem cliente ⇒ pedido de CPF; status volta a
  `Ativa`.

## 6. Virada

1. Central: deploy da mudança da seção 3. Criar `ApiConfig` com "Enviar por" = canal Twilio; guardar ID e token.
2. Seller: deploy da branch com `Central__Canal=Twilio` (nada muda em produção).
3. Seller: definir `Central__ApiUrl`, `Central__ApiToken`, `Central__SharedSecret` e `Central__Canal=Central`;
   redeploy.
4. Central: definir `SELLER_WEBHOOK_URL=https://<seller>/api/whatsapp/webhook`,
   `SELLER_RESET_URL=https://<seller>/api/whatsapp/reset`, `SELLER_SHARED_SECRET=<mesmo segredo>`; redeploy.
5. Twilio: "When a message comes in" e "Status callback URL" apontando para as URLs da central (já feito).
6. Smoke test: mensagem do celular ⇒ ticket na central ⇒ bot responde e a resposta aparece no ticket ⇒
   atendente assume ⇒ bot para ⇒ fechar ticket ⇒ próxima mensagem cai no menu (cliente identificado).

**Rollback:** `Central__Canal=Twilio` no seller e "When a message comes in" de volta para a URL do seller.

## 7. Fora do escopo

- Seller enviar mídia pela central (o bot só envia texto).
- Autenticação do endpoint `POST /api/whatsapp/enviar` (público hoje; segue como está).
- Retry de envio no seller.
