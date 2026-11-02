# Audial (fonte reconstruído)

Reconstruído em 02/11/2026 a partir do build v8.6, porque o código original se perdeu.
Inclui o tema "Audial Fluent" em `src/YtMusic.App/Theme`.

## De onde veio cada parte

- **C#**: descompilado das DLLs da v8.6 com ILSpy. Sem comentários e com nomes locais genéricos.
- **XAML das telas**: decodificado dos `.xbf` em `resources.pri`. Os `x:Bind` e eventos foram religados pelos IDs de conexão do código gerado.
- **Tema**: os 4 arquivos de `Audial-tema-fluent.zip`.
- **Assets, receiver e libvlc**: copiados do build.
- **Pacotes**: versões exatas de `YtMusicDesktop.deps.json`.

`ArtistPage`, `ListenTogetherPage` e `MiniPlayerWindow` são montadas em C#. As duas páginas têm um XAML vazio só para o `Frame` conseguir navegar até elas.

## Dependências locais

Estes itens não ficam no Git por causa do tamanho e precisam existir antes de compilar:

- `src/YtMusic.App/libvlc/`: plugins nativos do VLC, copiados do build.
- `src/YtMusic.App/receiver/node.exe`: runtime do Node para o receptor.
- `src/YtMusic.App/receiver/node_modules/`: rode `npm ci` em `src/YtMusic.App/receiver`.

## Compilar

```powershell
$env:DOTNET_ROOT = "$PWD\.tools\dotnet"
$env:NUGET_PACKAGES = "$PWD\.tools\nuget"
.\.tools\dotnet\dotnet.exe build src\YtMusic.App\YtMusic.App.csproj -c Release -p:Platform=x64
```

O executável sai em `src\YtMusic.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64`.

## Limitações

- Os testes de `tests/YtMusic.Tests` não foram recuperados.
- O `app.manifest` foi reescrito; o original não foi recuperado.
- `.decompiled` e `.decoded-xaml` guardam o material bruto, e `.tools` guarda SDKs e ferramentas. Nada disso entra na compilação.
