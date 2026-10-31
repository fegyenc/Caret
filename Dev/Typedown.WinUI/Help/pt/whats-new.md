# Novidades do Caret

As principais mudanças, das mais recentes às mais antigas. Cada recurso tem seu próprio tópico neste menu; esta página só diz onde olhar.

## Versão 2.10: digitalizados, capturas de tela e uma barra lateral mais clara

- **Editar** > **Colar texto de uma imagem (OCR)** lê uma captura de tela ou uma imagem da área de transferência (Win+Shift+S faz uma) com o OCR do Windows, no seu computador, e cola o texto onde está o cursor.
- Na página **Documentos e digitalizados**, ative **Ler também as imagens das pastas (OCR)** e uma pasta que você escolhe ou solta entrega ao OCR as imagens dela (PNG, JPG, BMP, TIFF) além dos documentos. No início fica desativado, e uma pasta com 30 imagens ou mais pergunta antes de começar. As opções de OCR agora vêm primeiro na página, e a linha abaixo da área de soltar cita os digitalizados e as imagens.
- A barra lateral tem três grupos: **Converter** (**Documentos e digitalizados**, que se chamava *Converter para Markdown*, **E-mails do Outlook** e **Faturas em uma só tabela**), **Este documento** (**Revisão**, **Marcas de fala** e, para um arquivo XML, **Ferramentas de XML**) e **Biblioteca**. **Ocultar dados pessoais...** também está no painel **Revisão**, e o cartão de baixo mostra uma dica.

## Versão 2.9: Brasil e Portugal, ferramentas de XML, digitalizados

- Mais documentos eletrônicos em palavras claras: a NFS-e nacional do Brasil (nota de serviço), o CT-e (documento de transporte) e o MDF-e (manifesto), com seus eventos, e o arquivo de auditoria SAF-T (PT) de Portugal, com conferência de totais e impostos. Uma pasta de faturas (*Faturas em uma só tabela*) pode misturá-los: um CT-e substituído por um CT-e de substituição é listado, mas não somado aos totais. Veja o tópico *Converter em Markdown*.
- Arquivos XML: a estrutura na lista *Estrutura de tópicos* e no mapa do documento; um trecho de XML com vários elementos lado a lado é lido e formatado como fragmento; **Editar** > **Colar como XML** põe a área de transferência na nota como bloco de código e a abre como documento XML; um clique num valor da versão legível o seleciona no código.
- **Editar** > **Ocultar dados pessoais...** oculta endereços de e-mail, telefones, IBANs, números de cartão, números de documentos e os nomes que você listar, em qualquer nota ou arquivo XML: numa cópia ou na própria nota (um Desfazer a traz de volta).
- PDF: converta só algumas páginas (a caixa *Páginas do PDF* da página Converter); leia digitalizados e fotos de páginas com o OCR do Windows, no seu PC; e acrescente marca d'água, cabeçalho, rodapé e números de página ao exportar (**Arquivo** > **Exportar** > **PDF...**).

## Versão 2.7: arquivos XML, em palavras claras

- Leia um arquivo XML como um documento. Abra-o, solte-o na janela ou escolha-o na página Converter. O Caret conhece as faturas eletrônicas da Colômbia (DIAN), do México (CFDI), do Brasil (NF-e e NFC-e), do Peru (SUNAT) e do Chile (DTE) e outras faturas UBL, e explica seus códigos em palavras (em português, inglês ou espanhol). Qualquer outro XML vira um esboço. Veja o tópico *Converter para Markdown*.
- Uma pasta de faturas como uma única tabela (página Converter, *Faturas em uma só tabela*): Excel, CSV ou Markdown, com as notas de crédito subtraídas e as duplicatas contadas uma vez.
- Edite arquivos XML. Eles abrem como código, com a versão legível ao lado. Uma faixa diz em palavras claras se o XML está bem formado e onde não está; **Editar** > **Formatar XML** arruma só os espaços em branco; **Editar** > **Validar com um esquema...** compara o arquivo com um XSD. O arquivo é salvo como foi escrito. Veja *Editar arquivos XML* no mesmo tópico.
- **Ir para o arquivo** (`Ctrl+K`) lista os arquivos XML, e o Windows oferece o Caret em *Abrir com* para arquivos .xml, .xsd, .xsl e .xslt.
- As imagens viajam com a nota: **Arquivo** > **Exportar** > **Markdown com imagens (.zip)...**, reúna as imagens ao lado da nota e **Parágrafo** > **Gráfico a partir da tabela...**

## Versão 2.6: a janela vem até você

- **Paleta de comandos** (`Ctrl+Shift+P`, **Exibir** > **Paleta de comandos**): digite algumas letras do que você quer fazer, um comando, uma configuração ou uma nota, e pressione Enter.
- **Painel de inspeção** (`Ctrl+Shift+I`, **Exibir** > **Painel de inspeção**): uma coluna ao lado da nota com seu esboço, suas marcas de revisão, o tempo da apresentação, todas as ocorrências da busca e o **Histórico** da nota, as versões salvas anteriores que você pode comparar e restaurar. As cópias das notas salvas ficam no seu PC.
- **Mapa do documento** (**Exibir** > **Mapa do documento**): uma faixa fina na borda direita que mostra onde você está na nota e onde estão os títulos, os comentários e as alterações.
- Uma barra de status que diz o que importa: quando o arquivo foi salvo, palavras e tempo de leitura, a seleção, as marcas de revisão. Um segmento sem nada a dizer não aparece.
- Uma barra pequena sobre as palavras selecionadas, com os formatos mais usados e um botão que faz **Copiar como texto do WhatsApp**.
- Solte arquivos em qualquer lugar da janela (Word, PDF, Excel, PowerPoint, CSV, e-mails ou uma pasta) para convertê-los em Markdown. Uma bandeja mostra os trabalhos, com Abrir, Mostrar na pasta, Comparar e Tentar novamente.
- Cartões de aparência (**Configurações** > **Aparência**): veja o esquema de cores em uma prévia ao vivo, encontre o que você mudou e volte ao esquema com um clique.
- Teclado primeiro: pressione `Alt` para ver qual tecla leva a cada botão, e `Ctrl+Shift+/` para a lista de todos os atalhos (**Atalhos de teclado** no menu Ajuda).
- Início como página de trabalho: os arquivos em que você trabalhava, o que você pode começar e suas pastas, em três colunas.
- Correções: as cores de toda a janela acompanham na hora uma mudança de tema, e as dicas de tecla somem quando a janela rola.

## Versão 2.5: envie como arquivo do Word

**Arquivo** > **Exportar** > **Documento do Word (.docx)** escreve um arquivo do Word de verdade no seu PC, com títulos, listas, tabelas, imagens, notas de rodapé, sumário, alterações controladas e comentários. O Word não precisa estar instalado. Veja o tópico *Exportar para o Word*.

## Versão 2.0: escreva, controle as alterações, fale

- Controle de alterações enquanto você edita (**Revisão**), comentários, aceitar ou rejeitar, e comparar um arquivo com uma versão anterior.
- **Marcas de fala** e **teleprompter** para suas apresentações, com relógio e ensaio.
- Verificador ortográfico, **Copiar como texto do WhatsApp**, um menu Ajuda, e português do Brasil, polonês e espanhol de todos os países de língua espanhola.

## Versão 1.6: arquivos a partir do Explorador

Clique com o botão direito em Converter para Markdown no Explorador de Arquivos, uma guia pode ser solta em outra janela do Caret, um menu de contexto no editor e uma conversão muito melhor de arquivos reais de PDF, Word, Excel, PowerPoint e e-mail.

## Versão 1.5

Uma guia pode ser arrastada para a própria janela, e a barra lateral rola e se recolhe. O mascaramento de dados pessoais nos e-mails também encontra os nomes nas saudações e nas despedidas.

## Versão 1.4: guias e um visual novo

Vários documentos em uma janela, uma página de Configurações, esquemas de cores, layouts e melhor contraste.

## Versão 1.3: e-mails do Outlook

Os **E-mails do Outlook** viram Markdown limpo, um arquivo para uma conversa inteira, com os dados pessoais mascarados se você quiser.

## Versão 1.2: Converter para Markdown

**Converter para Markdown** lê arquivos do Word, PDF, Excel e PowerPoint, o francês e o espanhol foram adicionados, e as letras acentuadas saem certas em arquivos que não são UTF-8.

## Versão 1.0

O próprio editor: um editor de Markdown que mostra o texto como ele vai ficar, com tabelas, links e imagens, favoritos e arquivos recentes, e nada é enviado para lugar nenhum.
