<p align="center">
  <img src="docs/banner.svg" alt="Audial" width="100%" />
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square" alt="Windows 10 e 11" />
  <img src="https://img.shields.io/badge/.NET-8-512BD4?style=flat-square" alt=".NET 8" />
  <img src="https://img.shields.io/badge/WinUI-3-F43F5E?style=flat-square" alt="WinUI 3" />
  <img src="https://img.shields.io/badge/status-experimental-F59E0B?style=flat-square" alt="Experimental" />
</p>

<p align="center">
  <b>Um cliente nativo de YouTube Music para Windows, com interface Fluent, mini player e controle pelo celular.</b><br />
  Sem Electron e sem aba de navegador disfarçada de aplicativo.
</p>

<p align="center">
  <img src="docs/screenshots/inicio.png" alt="Tela inicial do Audial" width="90%" />
</p>

> As imagens usam o modo demonstração, com dados fictícios. O Audial é um projeto independente e não é afiliado ao YouTube ou ao Google.

<p align="center">
  <a href="https://audial-site-nine.vercel.app/"><img src="https://img.shields.io/badge/Beta%20fechado-chega%20em%202027-F43F5E?style=for-the-badge" alt="Beta fechado, chega em 2027" /></a>
</p>

O Audial está em beta fechado e abre para todo mundo em 2027. Até lá, ele é testado por um grupo pequeno de pessoas. Acompanhe pelo [site](https://audial-site-nine.vercel.app/).

## Por que o Audial é diferente

- **Nativo de verdade.** Interface em WinUI 3 com efeito Mica, integrada aos controles de mídia do Windows.
- **Sua biblioteca.** Suas playlists e curtidas chegam pela API oficial do YouTube, com login pelo navegador.
- **Controle pelo celular.** Um receptor experimental permite vincular o YouTube do celular por código de TV.
- **Mini Player sempre visível.** Fica sobre as outras janelas, com volume na rodinha do mouse.
- **Modo demonstração.** Dá para explorar toda a interface sem conta e sem áudio.

## Telas

| | |
| --- | --- |
| ![Tocando agora](docs/screenshots/tocando.png) | ![Busca](docs/screenshots/busca.png) |
| **Tocando agora**, com fila ao lado | **Busca** em músicas, playlists e artistas |
| ![Artista](docs/screenshots/artista.png) | ![Playlist](docs/screenshots/playlist.png) |
| **Artista**, com músicas e discografia | **Playlist** com capa e lista de faixas |
| ![Fila](docs/screenshots/fila.png) | ![Dispositivos](docs/screenshots/dispositivos.png) |
| **Fila** de reprodução | **Dispositivos** e pareamento |

<p align="center">
  <img src="docs/screenshots/mini-player.png" alt="Mini Player" width="360" />
</p>

## Recursos

- Início com recomendações, recentes, álbuns e mixes que tocam com um clique
- Busca com filtros de músicas, playlists, artistas e álbuns
- Fila com reprodução, remoção e limpeza
- Reprodução por LibVLC, com busca na faixa e volume
- Atalhos de teclado, como `Ctrl+K`, `Ctrl+Q` e `Ctrl+M`
- Status no Discord, ícone na bandeja e controles de mídia do Windows
- Ouvir junto por P2P: cada pessoa toca o próprio áudio e a sala mantém todo mundo sincronizado
- Overlay para lives com a música atual, por um link HTTPS que funciona no TikTok LIVE Studio e no OBS
- Atualização automática pelos Releases do GitHub, com verificação SHA-256
- Tokens cifrados pelo Windows (DPAPI)

## Como é feito

```mermaid
flowchart LR
    App["YtMusic.App<br/>WinUI 3 e XAML"] --> Presentation["YtMusic.Presentation<br/>ViewModels MVVM"]
    App --> Services["YtMusic.Services<br/>Google, YouTube e demo"]
    Presentation --> Core["YtMusic.Core<br/>modelos e fila"]
    Services --> Core
    App --> VLC["LibVLC<br/>áudio"]
    App --> Receiver["Receptor Node<br/>controle pelo celular"]
```

## A história

O código original do Audial se perdeu e só sobrou o programa compilado. O projeto foi reconstruído a partir do próprio binário e depois ganhou um visual novo, com ajuda do Claude Code. O processo completo está em [A história da reconstrução](docs/RECONSTRUCAO.md).

## Aviso

O Audial é experimental, usa integrações não oficiais do YouTube Music e pode parar de funcionar se elas mudarem. Ele não confirma benefícios de assinaturas, e o uso deve respeitar os termos do YouTube. As licenças de terceiros estão em [THIRD-PARTY](THIRD-PARTY).
