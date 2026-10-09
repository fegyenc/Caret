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

**Revisão > Comparar com outro arquivo...** pede uma versão anterior do documento (por exemplo, a cópia que você enviou) e quem fez as alterações. O Caret escreve as diferenças entre essa versão e o documento na tela como uma revisão *em uma nova guia*; seu documento não é alterado. Use isso para ver o que um colega mudou no arquivo que devolveu. Se as guias estiverem desativadas (**Configurações > Guias e janelas**), a revisão toma o lugar do seu documento na janela, então salve seu documento primeiro.

## Controlar alterações enquanto você edita

**Revisão > Controlar alterações** (também um botão no painel Revisão) lembra o documento como ele está agora. Enquanto você edita, um instante depois de parar de digitar, o Caret mostra o que você mudou: o painel da direita da visualização **Dividido** desenha o documento com o que foi acrescentado em verde e o que foi excluído em vermelho, cada alteração com seu nome e o dia, e o painel Revisão lista as alterações e as conta (também na barra de status). Nada é escrito no arquivo. **Parar de controlar** esquece a versão de partida e deixa o documento como está. A lista serve também para se mover pelo documento: *clique em uma alteração* e o Caret salta até ela no editor e a destaca no painel da direita, por maior que seja o documento. **Alteração anterior** e **Próxima alteração** passam de uma para outra. O ✓ ao lado de uma alteração a aceita (ela passa a fazer parte da versão de partida e sai da lista); o ✗ a rejeita (o texto antigo volta ao documento, e **Desfazer** funciona). **Desfazer aceitar** devolve as últimas alterações aceitas. **Aceitar todas as alterações** e **Rejeitar todas as alterações** fazem o mesmo com todas as alterações acompanhadas de uma vez. As diferenças dentro de blocos de código só podem ser aceitas ou rejeitadas todas juntas. Mostrar as alterações na visualização Visual e gravá-las no arquivo como uma revisão são os próximos passos; este tópico os descreverá quando chegarem.

## Aceitar e rejeitar

- *Clique com o botão direito em uma alteração* (verde, vermelha ou uma substituição): **Aceitar alteração** mantém o que ela diz (as adições ficam, as exclusões saem); **Rejeitar alteração** devolve o texto antigo.
- *Clique com o botão direito em um comentário:* **Excluir comentário**.
- **Revisão > Aceitar todas as alterações**, **Rejeitar todas as alterações** e **Excluir todos os comentários** fazem isso no documento inteiro, como uma única etapa de Desfazer.

## Ocultar as marcas

**Configurações > Editor > Mostrar marcas de revisão** desativa as cores; as marcas passam a aparecer como o texto simples que são.

## Bom saber

- As marcas são texto, então um arquivo com uma revisão pode ser salvo, enviado e aberto em qualquer editor.
- Um arquivo devolvido com as marcas de um colega é lido da mesma forma: as alterações dele aparecem em cores e você as aceita ou rejeita.