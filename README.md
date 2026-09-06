# Windows Widgets

Widgets que ficam pousados na área de trabalho do Windows 11 — atrás de todas
as janelas, fora da barra de tarefas e do Alt+Tab, no espírito dos gadgets do
Windows 7. O primeiro é uma previsão do tempo.

> The interesting part for non-Portuguese speakers is
> [`DesktopLayer.cs`](src/WidgetHost/Desktop/DesktopLayer.cs): the classic
> `WorkerW` trick for pinning windows to the Windows desktop **does not work**
> on Windows 11 build 26100. See [Why the WorkerW trick fails](#por-que-o-truque-do-workerw-falha-no-windows-11)
> below. Code and identifiers are in English; comments and UI are in Portuguese.

## Por que o truque do WorkerW falha no Windows 11

A receita conhecida (Rainmeter, Wallpaper Engine, dezenas de posts) é: mandar a
mensagem não documentada `0x052C` para o `Progman`, esperar que ele forke uma
janela `WorkerW`, e fazer `SetParent` nela.

Testando as quatro ancoragens possíveis lado a lado no Windows 11 build 26100:

| Alvo | Renderiza | Recebe mouse |
|---|---|---|
| `WorkerW` | não | — |
| `Progman` | não | — |
| **`SHELLDLL_DefView`** | **sim** | **sim** |
| bottom-most top-level | sim | sim |

Duas causas:

1. O `Progman` carrega `WS_EX_NOREDIRECTIONBITMAP` (exstyle `0x00200080`). Essa
   árvore de janelas não tem superfície de redirecionamento: uma janela filha
   reporta `IsWindowVisible=true` e **não aparece na tela**. `PrintWindow`
   nessas janelas devolve 100% preto, o que corrobora.
2. O `0x052C` funciona, mas nesta build o `WorkerW` nasce como **filho** do
   `Progman`, não como irmão — então o `FindWindowEx(NULL, progman, "WorkerW", NULL)`
   clássico devolve `NULL`.

O alvo que funciona é o `SHELLDLL_DefView`, a camada dos ícones.

Como a validade de cada modo varia por build do Windows, `DesktopLayer` desce
uma escada e valida cada degrau em vez de apostar num só:

```
1. SetParent no SHELLDLL_DefView        ← funciona no 26100
2. SetParent no WorkerW irmão clássico  ← builds mais antigas
3. Top-level WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW, repinado em HWND_BOTTOM
```

O modo efetivamente usado aparece no menu da bandeja — diagnosticar "sumiu numa
máquina diferente" não pode depender de adivinhação.

### Três armadilhas de API pelo caminho

- **`SetParent` não serve como teste de sucesso.** Para uma janela top-level ele
  devolve o `HWND` da área de trabalho (`#32769`) como "pai anterior" — ou seja,
  **não-nulo justamente quando deu certo**. Testar o retorno contra `NULL` dá
  falso negativo.
- **`GetParent` é a sonda errada.** Sem `WS_CHILD` ele devolve o *owner*, não o
  pai, e retorna `0` mesmo depois de um `SetParent` bem-sucedido. A sonda certa
  é `GetAncestor(hwnd, GA_PARENT)`.
- **`WS_CHILD` não vem de graça.** Sem aplicá-lo explicitamente (removendo
  `WS_POPUP`) e chamar `SetParent` de novo, o Windows trata o alvo como owner e
  a janela continua top-level.

### Coordenadas mudam de espaço

Depois do `SetParent`, as posições passam a ser relativas ao *client* do alvo. A
origem do client do `SHELLDLL_DefView` é o canto da tela virtual, que em
multi-monitor costuma ser negativo. Sem converter com `GetWindowRect(target)`, o
widget cai no monitor errado.

Pelo mesmo motivo o arrasto é feito à mão com `GetCursorPos` em coordenadas de
tela: o `DragMove()` do WPF opera no espaço do pai.

### O host recria widgets; não basta reanexar

Quando o `explorer.exe` reinicia, o `SHELLDLL_DefView` morre e o Windows destrói
as janelas filhas junto — a nossa deixa de existir, não há o que reanexar.

Por isso há um watchdog de 3 s e a janela é **reconstruída**. `IWidget.CreateView()`
pode ser chamado várias vezes, e todo o estado (ViewModel, timers, dados) mora
na implementação de `IWidget`, nunca na View.

## O widget de tempo

Três variantes de layout — não é zoom, cada uma mostra um conjunto diferente de
informação:

| Variante | Tamanho | Conteúdo |
|---|---|---|
| Pequeno | 212×89 | ícone, temperatura, condição, cidade |
| Médio | 300×291 | + faixa do dia, 4 dias, umidade, vento |
| Grande | 424×355 | + 6 dias, chuva por dia, nascer e pôr do sol |

Dados da [Open-Meteo](https://open-meteo.com/) — sem chave de API, sem cadastro.
Localização detectada por IP na primeira execução, trocável nas configurações.
Uma única requisição traz tudo; os layouts menores ignoram o que não usam.

O elemento que carrega o widget é a **barra de faixa do dia**: uma escala
frio→quente entre a mínima e a máxima previstas, com um marcador na temperatura
atual. Diz o que número nenhum sozinho conta — *"10°, você está na base da faixa,
vai esquentar"*.

## Rodando

Requer [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) e Windows 10/11.

```powershell
dotnet build "WindowsWidgets.sln"
dotnet test  "WindowsWidgets.sln"
dotnet run --project src\WidgetHost
```

Distribuição — um único `.exe` de ~0,3 MB (framework-dependent):

```powershell
dotnet publish src\WidgetHost -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=none -o dist
```

O app vive na bandeja do sistema: é por lá que se adiciona widget, atualiza,
liga o início automático e se sai. Cada widget tem menu próprio no botão
direito (tamanho, zoom, configurações, bloquear posição, fechar).

Estado do usuário fica em `%APPDATA%\WindowsWidgets\`. Apagar a pasta reseta.

## Estrutura

| Projeto | Papel |
|---|---|
| `src/WidgetHost` | App WPF. Ancoragem, moldura, bandeja, persistência. Não sabe nada sobre previsão do tempo. |
| `src/Widgets.Abstractions` | O contrato `IWidget`. Quebra a referência circular entre host e widgets. |
| `src/Widgets.Weather` | O widget de tempo: API, domínio, ViewModel e Views. Não sabe nada sobre `WorkerW`. |
| `tests/Widgets.Weather.Tests` | Lógica pura, com fixtures reais capturadas das APIs. |
| `tests/WidgetHost.Tests` | Migração de settings, round-trip do store, limites de escala. |

## Escrevendo um widget novo

Implemente `IWidget` e adicione uma linha em `WidgetCatalog.Types`. O host cuida
de ancoragem, arrasto, zoom, persistência de posição, múltiplas instâncias e
recriação após reinício do shell.

O único cuidado real: `DesignSizeFor(size)` precisa ser **medido** contra o
conteúdo real, não estimado. O host envolve a View num `Viewbox` e escala a
partir desse valor — e um `Viewbox` mede o filho com largura infinita, o que
expõe layouts que dependem da largura disponível. Há testes cobrindo essa classe
de defeito em `LayoutOverlapTests`.

## Licença

[MIT](LICENSE).
