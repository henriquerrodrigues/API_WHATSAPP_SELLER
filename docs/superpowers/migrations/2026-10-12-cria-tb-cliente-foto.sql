-- Cria tb_cliente_foto para guardar a foto da fachada enviada no cadastro
-- via WhatsApp. Imagem vai como BLOB porque o host (Render/Railway) tem
-- disco efemero e o Twilio pode apagar a midia original.
--
-- NAO APLICADO AINDA. Aplicar manualmente no banco antes do deploy do codigo
-- que usa CreateClienteComFotoAsync.
--
-- MEDIUMBLOB comporta ate 16 MB (limite da aplicacao: 5 MB).
-- max_allowed_packet padrao do MySQL 8 (64 MB) basta.
-- Sem UNIQUE em FOT_CLICODIGO de proposito: hoje so uma foto por cliente,
-- mas a tabela nao impede futuras.

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
