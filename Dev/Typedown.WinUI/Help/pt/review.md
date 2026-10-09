# Revisão: comentários e alterações

A função de revisão funciona como as ferramentas de revisão de um processador de texto, mas tudo é escrito no documento como texto simples, de modo que um colega, outro editor e um assistente de IA também conseguem lê-lo. As marcas usam uma convenção pública chamada CriticMarkup:

| Você vê | Escrito no arquivo como |
|---|---|
| texto adicionado em verde | `{++added++}` |
| texto excluído em vermelho | `{--deleted--}` |
| uma substituição | `{~~old~>new~~}` |
| um comentário amarelo | `{==the text==}{>>@Name 2026-10-07: the note<<}` |

Nada é guardado em outro lugar, e a revisão nunca começa sozinha: digitar e excluir não criam marcas.

## Adicionar um comentário

Selecione o texto, clique com o botão direito e escolha **Adicionar comentário** (ou **Revisão > Adicionar comentário**). Escreva a nota e confira o seu nome (no início, é o seu nome de usuário do Windows). O texto vira um destaque amarelo com a nota ao lado. Ele é digitado no documento, então **Desfazer** funciona.

## Comparar com outro arquivo

**Revisão > Comparar com outro arquivo...** pede uma versão anterior do documento (por exemplo, a cópia que você enviou) e quem fez as alterações. O Caret escreve as diferenças entre essa versão e o documento na tela como uma revisão *em uma nova guia*; seu documento não é alterado. Use isso para ver o que um colega mudou no arquivo que devolveu.

## Aceitar e rejeitar

- *Clique com o botão direito em uma alteração* (verde, vermelha ou uma substituição): **Aceitar alteração** mantém o que ela diz (as adições ficam, as exclusões saem); **Rejeitar alteração** devolve o texto antigo.
- *Clique com o botão direito em um comentário:* **Excluir comentário**.
- **Revisão > Aceitar todas as alterações**, **Rejeitar todas as alterações** e **Excluir todos os comentários** fazem isso no documento inteiro, como uma única etapa de Desfazer.

## Ocultar as marcas

**Configurações > Editor > Mostrar marcas de revisão** desativa as cores; as marcas passam a aparecer como o texto simples que são.

## Bom saber

- As marcas são texto, então um arquivo com uma revisão pode ser salvo, enviado e aberto em qualquer editor.
- Um arquivo devolvido com as marcas de um colega é lido da mesma forma: as alterações dele aparecem em cores e você as aceita ou rejeita.
- Há mais por vir: ativar o controle de alterações e ver as suas próprias alterações enquanto edita. Este tópico dirá como quando isso chegar.