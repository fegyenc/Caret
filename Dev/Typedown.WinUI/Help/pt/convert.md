# Converter documentos e e-mails

O Caret transforma documentos em Markdown: um texto limpo que mantém títulos, listas, tabelas, links e notas de rodapé, e deixa de fora fontes e layout. O resultado costuma ser uma pequena parte do tamanho original, fácil de ler para as pessoas e de absorver para assistentes de IA com muito menos tokens. Tudo é convertido no seu PC: nada é enviado.

## Documentos

Abra **Converter para Markdown** na barra lateral e solte arquivos ou uma pasta inteira, ou use **Escolher arquivos...**. Word, Excel, PowerPoint, PDF, CSV e XML são aceitos. Cada resultado mostra o tamanho antes e depois e uma estimativa de seus tokens de IA. **Copiar tudo para a IA** coloca tudo na área de transferência como um único texto.

Você também pode clicar com o botão direito em um arquivo, em vários arquivos ou em uma pasta no *Explorador de Arquivos* e escolher **Converter para Markdown** (indisponível quando a sua organização desativa isso).

Os arquivos Markdown são salvos ao lado dos originais, ou em uma pasta que você escolher. Um arquivo que já existe nunca é sobrescrito.

## E-mails do Outlook

Abra **E-mails do Outlook** e escolha e-mails salvos do Outlook (`.msg`) ou de outros programas de e-mail (`.eml`). O Caret transforma a conversa inteira em um único arquivo: cada resposta como uma mensagem própria, da mais antiga para a mais recente, sem assinaturas, avisos legais e faixas de "remetente externo", com os anexos convertidos no lugar.

**Ocultar dados pessoais** (ativado por padrão) substitui nomes, endereços de e-mail, números de telefone, números de contas bancárias e números de documentos por marcadores como `[PERSON-1]`, o mesmo marcador para a mesma pessoa, de modo que a conversa continue legível quando você a colar em um assistente de IA. Isso funciona com regras fixas e dígitos verificadores, não com IA. As pessoas são encontradas pelos remetentes e destinatários do e-mail e pelas saudações e despedidas, então um nome que só aparece no meio de uma frase não é encontrado. Confira o resultado antes de compartilhar.

E-mails em inglês, francês, espanhol, polonês e português são compreendidos, e as regras são arquivos simples que uma empresa pode ampliar.

## Arquivos XML

Os arquivos XML que programas e autoridades fiscais produzem (notas fiscais eletrônicas, declarações, exportações, configurações) também são convertidos, para que uma pessoa possa lê-los. Onde o Caret conhece o formato (a fatura eletrônica colombiana da DIAN, o CFDI mexicano do SAT, a NF-e brasileira, a fatura peruana da SUNAT e o DTE chileno do SII), o arquivo começa com um resumo curto; depois dele, ou para qualquer outro arquivo XML, vêm todos os campos do arquivo, em ordem. As partes que têm um título próprio (um `id` e um `name`) viram títulos para onde você pode pular, listas de texto viram listas numeradas, registros repetidos viram tabelas, e um arquivo grande começa com uma visão geral do que ele contém. As assinaturas digitais são nomeadas, não impressas.

O Caret não verifica nem julga um arquivo XML: mostra o que o arquivo diz. Um arquivo mal formado diz onde ele quebra. O XML é lido somente no seu PC, e um arquivo vindo de fora não pode fazer o Caret ler outros arquivos nem se conectar à internet.

Para ler um arquivo XML sem salvar nada, use **Importar documento como Markdown...** no menu Arquivo: ele abre a versão legível como uma nova nota.

## Editar arquivos XML

Um arquivo XML também pode ser aberto para edição: **Arquivo > Abrir...**, a árvore de pastas ou a linha de comando. Ele abre como código, com a versão legível ao lado, refeita um instante depois que você para de digitar. Sob o código, uma faixa diz se o texto é XML bem formado e, se não for, onde (clique nela para ir até lá). As marcas são coloridas, fechadas e emparelhadas enquanto você digita, e os elementos podem ser recolhidos. **Ctrl+K** (Ir para o arquivo) mostra os arquivos XML da pasta aberta junto com as suas notas, e na lista *Abrir com* do Windows o Caret aparece para os arquivos .xml, .xsd, .xsl e .xslt. A estrutura do arquivo está na aba **Estrutura de tópicos** do painel de inspeção (`Ctrl+Shift+I`): os elementos que contêm outros elementos, com um id ou um nome quando houver, os três primeiros de cada tipo e depois uma contagem. Um clique vai a esse elemento no código, e o mapa do documento na borda direita marca os mesmos elementos na visualização Código.

O arquivo é salvo como foi escrito: a mesma codificação (também ISO-8859-1 ou UTF-16), a mesma marca de ordem de bytes e os mesmos fins de linha (um arquivo com tipos diferentes de fim de linha recebe o mais frequente em todo o arquivo, e a barra de status avisa), para que o programa que o lê continue lendo. Se você digitar um caractere que a codificação do arquivo não consegue guardar, nada é gravado e o Caret diz onde ele está. **Editar > Formatar XML** coloca cada elemento em sua própria linha, com recuo, e muda só o espaço entre os elementos (um Desfazer traz de volta o texto anterior). Um arquivo com assinatura digital pergunta antes de ser salvo ou formatado, porque qualquer alteração invalida a assinatura. O salvamento automático nunca grava um arquivo XML.

**Editar > Validar com um esquema...** (ou o botão **Esquema** da faixa) valida o texto com um esquema XSD: escolha o arquivo .xsd, ou escreva o nome dele no arquivo (`xsi:noNamespaceSchemaLocation` ou `xsi:schemaLocation`) e o Caret o procura junto do arquivo XML. Cada problema é dito com palavras simples ("O elemento "price" não é esperado aqui (dentro de "book"). Esperado: title.") e marcado com um ponto vermelho na margem da sua linha; clique no texto `Esquema · problemas: 4` da faixa para ir de um ao seguinte. Só arquivos do seu computador são lidos (também os que o esquema inclui ou importa); um endereço da web em `schemaLocation` nunca é baixado. O × para a validação.

## Solte arquivos em qualquer lugar

Não é preciso abrir antes a página de conversão: arraste arquivos do Word, PDF, Excel, PowerPoint ou e-mails (ou uma pasta inteira) para a janela do Caret, em qualquer lugar, e solte-os quando aparecer **Converter para Markdown**. Uma bandeja pequena, no canto inferior direito, lista os arquivos, cada um com uma barra de progresso e, quando o ponteiro está sobre ele ou o teclado está nele, **Abrir**, **Mostrar no Explorador de Arquivos** (o botão de pasta) e **Comparar**. Um arquivo que falha diz o motivo, com **Tentar novamente**; **Limpar lista** esvazia a bandeja e Esc a fecha.

**Comparar** mostra o original como texto simples à esquerda e o Markdown à direita, e rola os dois juntos. Acima ficam as opções usadas: se as imagens foram extraídas e, em um e-mail, se os dados pessoais foram ocultados e quantos nomes, endereços, números e identificações foram encontrados (no Markdown aparecem em negrito e sublinhados). Esc fecha. Use para ver o que a conversão manteve, descartou ou ocultou antes de compartilhar o resultado.

## Quando algo não é convertido

Um arquivo protegido por senha ou danificado é listado com o motivo. Um PDF digitalizado sem texto não pode ser lido: o Caret não faz reconhecimento de texto.