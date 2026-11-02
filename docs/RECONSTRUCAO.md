# A história da reconstrução

O código-fonte original do Audial se perdeu. Sobrou apenas o programa compilado da versão 8.6.
Em 02/11/2026, o projeto foi reconstruído a partir desse build, com a ajuda do Claude Code, e depois ganhou um visual novo.

## De onde veio cada parte

| Parte | Origem |
| --- | --- |
| C# | Descompilado das DLLs da v8.6 com ILSpy, por isso quase não há comentários e alguns nomes locais são genéricos |
| XAML das telas | Decodificado dos arquivos `.xbf` dentro de `resources.pri`; os `x:Bind` e eventos foram religados pelos IDs do código gerado |
| Tema | Os 4 arquivos do tema "Audial Fluent" |
| Assets, receptor e libvlc | Copiados do build |
| Pacotes NuGet | Versões exatas de `YtMusicDesktop.deps.json` |

## O que deu trabalho

- O formato XBF não guarda `UpdateSourceTrigger`. Sem ele, a busca enviava o texto vazio.
- O compilador de XAML pode falhar sem mensagem quando falta um prefixo de namespace ou quando um XAML está malformado.
- O `ItemsRepeater` usa templates compilados com `x:Bind` sem definir `DataContext`. Por isso os cliques leem o item pela posição, com `UiHelpers.ItemOf`.
- Erros de `StaticResource` só aparecem com o app aberto, nunca na compilação.

## Limitações conhecidas

- Os testes de `tests/YtMusic.Tests` não foram recuperados.
- O `app.manifest` foi reescrito, porque o original não foi recuperado.
- `ArtistPage`, `ListenTogetherPage` e `MiniPlayerWindow` são montadas em C#. As duas páginas têm um XAML vazio só para o `Frame` conseguir navegar até elas.
