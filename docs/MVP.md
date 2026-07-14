# MVP tecnico

Atualizado em 2026-07-14.

## Stack

- .NET 9
- WPF
- CommunityToolkit.Mvvm
- Serilog
- Core Modbus TCP proprio

## Como rodar

Na pasta do projeto:

```powershell
dotnet run --project .\ModbusTcpTroubleshooter.App\ModbusTcpTroubleshooter.App.csproj
```

Para compilar:

```powershell
dotnet build
```

## Porta de teste

O MVP usa `1502` como porta padrao para teste local. A porta Modbus TCP oficial e `502`, mas no Windows ela pode exigir permissao elevada e tambem pode estar ocupada por outro software.

## Funcionalidades implementadas

- criar um caso em memoria
- mapa inicial simulado com coils, discrete inputs, holding registers e input registers
- modo server substitute
- modo client substitute
- seletor de modo operacional `Client` / `Server`
- configuracoes de client e server separadas na UI
- mapa do server configuravel por faixas descontínuas
- sub-aba de configuracao de faixas do server
- sub-aba de visualizacao do mapa efetivo do server
- mapa editavel do client com linhas habilitaveis por function code, endereco e quantidade
- leitura unica do mapa do client
- leitura ritmada do mapa do client com taxa de scan configuravel
- paineis internos redimensionaveis por splitters
- diagnosticos com abas de lista completa, resumo de avisos importantes e checklist automatico
- avisos importantes agregados por ocorrencia, com contagem, primeira vez, ultima vez e recomendacao
- checklist automatico de socket TCP, resposta Modbus, transaction ID, function code, mapa/range e escritas
- verificacao de padrao de polling e exceptions repetidas com base no trafego observado pelo app
- timeline inferior com abas `Modbus` e `TCP`
- timeline TCP com captura passiva via SharpPcap/PacketDotNet, quando Npcap estiver disponivel
- selecao de interface de rede e filtros guiados de captura por protocolo, IP, direcao e porta
- filtro visual por coluna na timeline TCP: source, destination, protocol e info
- BPF gerado automaticamente a partir dos filtros guiados
- timeline TCP em formato semelhante ao Wireshark, com numero, tempo relativo, origem, destino, protocolo, tamanho e info

## Captura passiva TCP

A aba `TCP` pode capturar trafego real da interface de rede. No Windows, isso depende de Npcap instalado e permissao suficiente para captura.

Filtros:

- `Protocolo`: todos, TCP, UDP, ARP, ICMP ou Modbus TCP.
- `IP` + `Direcao IP`: origem, destino ou ambos.
- `Porta` + `Direcao porta`: origem, destino ou ambos.
- `Filtro Source`, `Filtro Destination`, `Filtro Protocol`, `Filtro Info`: filtros visuais por coluna.
- `BPF gerado`: expressao tecnica montada automaticamente para a captura.

Sem Npcap, o app continua funcionando como client/server Modbus, mas a captura passiva nao lista interfaces.
- leitura FC03 e FC04 no cliente
- leitura FC01 e FC02 no cliente
- escrita FC06 no cliente
- servidor respondendo FC01, FC02, FC03, FC04, FC05, FC06, FC15 e FC16
- timeline de frames com transaction ID, unit ID, function code, endereco, quantidade e hex
- diagnosticos simples baseados em timeout, exception, escrita e range fora do mapa
- exportacao do caso para JSON
- log em `ModbusTcpTroubleshooter.App/logs/`

## Teste local rapido

1. Abra o app.
2. Deixe `IP local` como `0.0.0.0`.
3. Deixe `IP alvo` como `127.0.0.1`.
4. Deixe a porta como `1502`.
5. Clique em `Iniciar servidor`.
6. Em `Client substitute`, leia FC03 a partir do endereco `0`, quantidade `10`.
7. Escreva FC06 no endereco `0` com outro valor.
8. Leia FC03 novamente e confira a mudanca no mapa e na timeline.

## Limites conscientes do MVP

- sem captura passiva de rede
- sem importacao real de CSV/Excel ainda
- sem editor visual de mapa
- sem assumir IP automaticamente pelo Windows
- sem empacotador/installer
- diagnostico ainda baseado em regras simples

## Proxima etapa tecnica recomendada

1. Adicionar editor/importador de mapa.
2. Permitir salvar e reabrir caso.
3. Criar modo de leitura ciclica no client substitute.
4. Adicionar presets de diagnostico.
5. Preparar instalador Windows.
