# Windows Widgets

Widgets que ficam **pousados na área de trabalho** do Windows 11, no espírito
dos gadgets do Windows 7: atrás de todas as janelas, fora da barra de tarefas e
do Alt+Tab, sem painel lateral e sem precisar apertar `Win+W`. Você minimiza
tudo e a informação está lá — como se fizesse parte do papel de parede.

O primeiro widget é uma **previsão do tempo**.

---

## O que ele faz

- **Vive na área de trabalho, não sobre ela.** O widget não fica por cima das
  suas janelas nem rouba foco. Aparece quando você chega na área de trabalho e
  some atrás do que estiver aberto.
- **Não polui.** Nada na barra de tarefas, nada no Alt+Tab. O único ponto de
  contato é o ícone na bandeja do sistema.
- **Arraste e solte onde quiser.** A posição é lembrada entre reinícios,
  inclusive em multi-monitor.
- **Redimensione arrastando o canto** ou escolha um zoom pronto no menu.
- **Várias cópias do mesmo widget.** Duas cidades, duas posições, duas
  configurações independentes.
- **Sobrevive ao Windows.** Se o `explorer.exe` reiniciar (ou travar), os
  widgets voltam sozinhos em poucos segundos, na mesma posição.

## O widget de previsão do tempo

Três **variantes de layout**. Não é zoom: cada uma mostra um conjunto diferente
de informação.

| Variante | Tamanho | O que mostra |
|---|---|---|
| **Pequeno** | 212×89 | ícone da condição, temperatura, condição, cidade |
| **Médio** | 300×291 | + faixa do dia, próximos 4 dias, umidade, vento |
| **Grande** | 424×355 | + 6 dias, chance de chuva por dia, nascer e pôr do sol |

O elemento que dá o tom é a **barra de faixa do dia**: uma escala frio→quente
entre a mínima e a máxima previstas, com um marcador na temperatura atual. Ela
diz o que número nenhum sozinho conta — *"10°, você está na base da faixa, ainda
vai esquentar"*.

**Sem cadastro e sem chave de API.** Os dados vêm da
[Open-Meteo](https://open-meteo.com/), que é aberta. Na primeira execução a
cidade é detectada pelo seu IP; depois é só trocar nas configurações.

**Funciona offline.** A última leitura boa fica guardada em disco: sem internet,
o widget abre com o último estado conhecido e marca o dado como
*"sem conexão"* ou *"dado antigo"* em vez de mostrar um retângulo vazio. Quando
a rede volta, ele se recupera sozinho — as tentativas vão espaçando (1, 2, 4…
até 30 minutos) para não martelar o serviço.

## Instalando e rodando

Precisa do [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
e do Windows 10 ou 11.

Para gerar o executável (um único `.exe` de ~0,3 MB), com o
[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) instalado:

```powershell
dotnet publish src\WidgetHost -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=none -o dist
```

Rode o `dist\WindowsWidgets.exe`. Não há instalador: dá para deixar o `.exe`
onde preferir — só evite movê-lo depois de ligar o início automático, porque o
atalho registrado aponta para o caminho onde ele estava.

Na primeira execução aparece um widget de tempo na área de trabalho e o ícone na
bandeja.

## Usando

### Menu da bandeja

É a porta de entrada do app — os widgets não aparecem na barra de tarefas.

| Item | O que faz |
|---|---|
| **Adicionar widget** | Coloca mais um widget na área de trabalho (é assim que se põe uma segunda cidade). |
| **Atualizar tudo** | Força uma busca imediata em todos os widgets. |
| **Configurações...** | Abre as opções do widget. |
| **Iniciar com o Windows** | Liga/desliga o início automático. |
| **Ancoragem: …** | Informativo. Mostra como o app conseguiu se fixar na área de trabalho — serve para diagnosticar se algo sumir numa máquina diferente. |
| **Sair** | Fecha tudo. |

### Menu do widget (botão direito sobre ele)

| Item | O que faz |
|---|---|
| **Atualizar agora** | Busca dados novos só deste widget. |
| **Configurações...** | Cidade, unidade e frequência de atualização. |
| **Tamanho** | Troca a variante de layout (Pequeno / Médio / Grande). O zoom volta a 100%. |
| **Zoom** | 75%, 100%, 150% ou 200% do mesmo layout. |
| **Bloquear posição** | Trava o widget: para de arrastar e de redimensionar. Bom para quem já deixou tudo no lugar. |
| **Fechar widget** | Remove esta cópia. |

Você também pode **arrastar** o widget com o botão esquerdo e **redimensionar**
pelo canto inferior direito, que aparece ao passar o mouse. O zoom vai de 0,6x a
2,5x e mantém a proporção — a tipografia, os ícones e os espaçamentos crescem
juntos, sem esticar nada.

### Configurações do tempo

- **Localização** — busque pelo nome da cidade. Os resultados vêm com estado e
  país porque o serviço ranqueia por população: procurar "Santa Maria" traz
  cidades grandes de outros países antes da sua.
- **Unidade** — Celsius ou Fahrenheit.
- **Atualizar a cada** — 5, 10, 15 (padrão), 30 minutos ou 1 hora.

Cada cópia do widget tem suas próprias configurações.

## Onde ficam seus dados

Tudo em `%APPDATA%\WindowsWidgets\`:

- `settings.json` — quais widgets existem, posição, tamanho, zoom e opções.
- `cache\weather.json` — a última previsão baixada, usada quando não há rede.

Apagar essa pasta reseta o app ao estado de primeira execução. Nada é enviado
para lugar nenhum: as únicas conexões de saída são a consulta de previsão, a
busca de cidade e a detecção de localização por IP na primeira execução.

## Se algo der errado

**O widget não aparece.** Verifique se o ícone está na bandeja (pode estar
escondido na setinha "mostrar ícones ocultos"). Abra o menu e olhe a linha
**Ancoragem** — ela diz qual modo de fixação o app conseguiu usar nesta máquina.

**Sumiu depois de trocar o papel de parede ou de o Windows travar.** Ele deve
voltar sozinho em até 3 segundos. Se não voltar, saia pelo menu da bandeja e
abra de novo.

**Aparece "sem conexão" ou "dado antigo".** O widget está mostrando a última
leitura que conseguiu. Ele tenta de novo sozinho; **Atualizar agora** força a
tentativa.

**Cidade errada.** A detecção inicial é por IP e erra quando você usa VPN. Vá em
**Configurações** e busque a cidade certa.

## Para desenvolvedores

O código é uma solução .NET 8 com um host WPF (`src/WidgetHost`) e os widgets em
projetos separados (`src/Widgets.Weather`), ligados pelo contrato `IWidget` em
`src/Widgets.Abstractions`. Adicionar um widget novo é implementar essa
interface e registrar uma linha em `WidgetCatalog.Types` — ancoragem, arrasto,
zoom, persistência e recriação vêm de graça do host.

```powershell
dotnet build "WindowsWidgets.sln"
dotnet test  "WindowsWidgets.sln"
dotnet run --project src\WidgetHost
```

Detalhes de arquitetura e as armadilhas do Windows envolvidas em pousar uma
janela na área de trabalho estão em [`CLAUDE.md`](CLAUDE.md) e comentados em
[`DesktopLayer.cs`](src/WidgetHost/Desktop/DesktopLayer.cs) — resumo: o truque
clássico do `WorkerW` **não funciona** no Windows 11 build 26100, e o alvo que
funciona é o `SHELLDLL_DefView`.

Código e identificadores em inglês; comentários e interface em português.

## Licença

[MIT](LICENSE).
