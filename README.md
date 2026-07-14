# Modbus TCP Troubleshooter

Criado em 2026-07-14.

## Objetivo

Construir uma ferramenta de troubleshoot para Modbus TCP com foco em diagnostico de campo, substituicao temporaria de dispositivos na rede e validacao rapida de comunicacao entre clientes e servidores.

O diferencial nao e apenas "ler/escrever registradores" como um Modscan tradicional. A ideia e combinar:

- simulacao controlada de um dispositivo
- inspeção viva do trafego Modbus TCP
- comparacao entre mapa esperado e comportamento observado
- assistente de troubleshooting orientado por contexto operacional

## Cenario principal

Fluxo alvo inicial:

> "Sou o IP X da rede, tenho esse mapa, vou remover esse dispositivo da rede e entrar no lugar dele para diagnosticar a rede."

A ferramenta deve permitir que Felipe:

- assuma o IP do equipamento original
- carregue ou monte o mapa Modbus esperado
- opere como servidor substituto para responder ao cliente da rede
- opere como cliente substituto para testar um servidor real
- capture e interprete as trocas Modbus TCP
- identifique divergencias entre o esperado e o observado
- gere hipoteses de falha com linguagem operacional

## Escopo inicial

Primeira fase:

- somente Modbus TCP
- sem RTU, RTU over TCP ou serial
- foco em troubleshoot e diagnostico
- operacao local em Windows
- suporte a dois modos principais:
  - servidor substituto na rede
  - cliente substituto na rede

## Visao do produto

O software pode ser pensado em 4 camadas.

### 1. Workspace de caso

Cada troubleshoot abre um "caso" com contexto salvo:

- nome do ativo ou sistema
- IP local que a ferramenta vai usar
- IP/porta do alvo
- papel assumido: client ou server
- mapa Modbus associado
- anotacoes do tecnico
- capturas de trafego e eventos

Isso evita uso solto da ferramenta e ajuda a transformar cada teste em evidencia reutilizavel.

### 2. Motor de comunicacao

Camada responsavel por:

- stack Modbus TCP client
- stack Modbus TCP server
- bind de IP/porta
- controle de Unit ID
- leitura e escrita de coils, discrete inputs, holding registers e input registers
- injecao de respostas, timeouts e excecoes para testes

### 3. Motor de observabilidade

Camada responsavel por enxergar e explicar o trafego:

- log cronologico das requisicoes e respostas
- decodificacao do frame MBAP + PDU
- correlacao transaction ID
- tempos de resposta
- funcoes mais usadas e codigos de excecao
- comparacao entre valor esperado, valor configurado e valor observado

### 4. Motor de diagnostico

Camada que transforma eventos em troubleshooting pratico:

- "o cliente esta lendo registrador inexistente"
- "o cliente espera FC03, mas o dispositivo real responde em outro bloco"
- "houve timeout apos N tentativas"
- "o polling esta rapido demais"
- "o mapa carregado nao cobre os enderecos requisitados"
- "ha escrita em registradores criticos que merecem destaque"

## Modos operacionais propostos

### Modo A. Server Substitute

Uso:

- retirar um device real da rede
- assumir o mesmo IP
- responder ao client/SCADA/PLC como se fosse o equipamento

Capacidades desejadas:

- importar ou montar um mapa
- definir comportamento por endereco
- responder com valores fixos, sequenciais ou simulados
- responder com excecoes especificas
- sinalizar leituras fora do mapa
- registrar quais pontos o cliente realmente consome

Valor pratico:

- validar se o problema esta no device removido ou no cliente
- descobrir o mapa real consumido em campo
- confirmar intervalos, funcoes e sequencia de polling

### Modo B. Client Substitute

Uso:

- conectar em um servidor Modbus TCP real
- testar leitura/escrita
- validar mapa, tempos e comportamento

Capacidades desejadas:

- navegador de mapa por tipo de dado
- sequencias de teste
- leitura ciclica
- escrita manual assistida
- presets de diagnostico
- varredura controlada de ranges

Valor pratico:

- confirmar se o servidor responde
- isolar falha de configuracao, endereco, funcao ou offset
- testar rapidamente antes de envolver SCADA/PLC

### Modo C. Passive Observe

Nao e o foco do primeiro corte, mas faz sentido como evolucao:

- ouvir trafego espelhado
- decodificar conversas existentes
- atuar como um "mini-Wireshark Modbus orientado a processo"

Isso provavelmente depende de captura de rede mais profunda e permissao adequada, entao eu deixaria como fase 2.

## Estrutura funcional sugerida

### 1. Case Manager

- cria, salva e reabre casos
- centraliza configuracao de rede, mapa e evidencias

### 2. Map Manager

- editor de mapa Modbus
- importacao de CSV/Excel no futuro
- metadados por ponto:
  - endereco
  - tipo
  - tamanho
  - descricao
  - acesso R/W
  - escala
  - observacoes

### 3. Client Engine

- conexao ativa a servidor Modbus TCP
- execucao manual e automatizada de leituras/escritas

### 4. Server Engine

- servidor Modbus TCP local
- respostas dinamicas por regra
- simulacao de falhas e excecoes

### 5. Traffic Timeline

- trilha temporal tipo analisador
- filtros por function code, endereco, erro, origem

### 6. Diagnostics Engine

- regras heuristicas
- sugestoes de causa raiz
- checklist de proximos testes

### 7. Evidence Export

- exportar log do caso
- exportar mapa observado
- exportar relatorio simples de troubleshooting

## Ideias de diferenciacao

Essas sao as ideias que realmente diferenciam de um Modscan tradicional:

### 1. "Entrar no lugar do device"

Esse e o caso mais forte. A ferramenta nasce pensando em assumir o lugar do equipamento, nao apenas conversar com ele.

### 2. Mapa esperado x mapa observado

O software deve aprender com o trafego:

- quais enderecos foram lidos
- com que frequencia
- com qual function code
- se houve escrita

No fim, ele pode gerar um "mapa observado em campo", muito util quando a documentacao esta errada ou incompleta.

### 3. Troubleshooting guiado

Em vez de mostrar so frames crus, a ferramenta deve responder:

- o que aconteceu
- o que isso significa
- o que testar em seguida

### 4. Simulacao de falha controlada

Exemplos:

- responder exception 02 para um range
- atrasar respostas
- derrubar resposta intermitentemente
- devolver valor fora da faixa

Isso ajuda a reproduzir problemas e validar comportamento do cliente.

### 5. Perfil de consumo do cliente

Quando rodando como servidor substituto, a ferramenta pode mostrar:

- taxa de polling
- ranges mais lidos
- escritas recebidas
- bursts
- tempos entre ciclos

Isso aproxima a ferramenta de um analisador operacional.

## Riscos e desafios tecnicos

### 1. Assumir o IP do equipamento

Isso e viavel, mas depende de contexto de rede, ARP, firewall, bindings e permissao local. O produto deve tratar isso como procedimento assistido, nao magica.

### 2. Diferenca de enderecamento

Ha muita confusao de mercado entre:

- 0-based vs 1-based
- 40001 style vs offset real
- blocos documentados de forma inconsistente

O produto precisa explicitar isso muito bem.

### 3. Captura passiva de trafego

Para fazer algo proximo de Wireshark, talvez seja necessario suporte separado para captura bruta e analise. Melhor nao misturar isso no MVP.

### 4. Escrita em ambiente real

Qualquer funcao de write precisa de barreiras claras, destaque visual e modo seguro.

## MVP recomendado

Eu atacaria o MVP assim:

### Fase 1. Base operacional

- criar casos
- carregar mapa manual simples
- client Modbus TCP basico
- server Modbus TCP basico
- log detalhado de frames

### Fase 2. Valor real de troubleshoot

- assumir papel de servidor substituto com mapa configuravel
- timeline de trafego
- deteccao de enderecos fora do mapa
- estatisticas de polling
- exportacao de evidencia

### Fase 3. Diferenciais

- diagnostico guiado
- mapa observado automatico
- simulacao de falhas
- presets de teste

## Proposta de estrutura de projeto de software

Ainda sem travar tecnologia, eu visualizo algo assim:

```text
modbus-tcp-troubleshooter/
  docs/
    product-vision.md
    protocol-notes.md
    troubleshooting-playbooks.md
  app/
    case-manager/
    map-manager/
    client-engine/
    server-engine/
    diagnostics-engine/
    traffic-analyzer/
    export/
  samples/
    maps/
    captures/
    scenarios/
  tests/
    integration/
    protocol/
    diagnostics/
```

## Primeiras decisoes de produto que eu sugiro

- Desktop first para Windows
- Modbus TCP only no inicio
- Caso de uso principal: substituir temporariamente server ou client para diagnostico
- Forte foco em interpretacao do trafego, nao apenas emissao de comandos
- Estrutura orientada a "casos de troubleshoot", nao a sessoes soltas

## Backlog inicial de perguntas

Antes de desenhar a arquitetura tecnica, eu fecharia estas respostas:

- A ferramenta sera desktop nativa, web local ou hibrida?
- Voce quer prioridade em UX visual ou velocidade de entrega?
- O mapa sera digitado manualmente no inicio ou vale importar CSV/Excel logo no MVP?
- O modo passivo de observacao entra cedo ou fica para depois?
- Voce quer suporte a multiplas conexoes simultaneas no MVP?
- O software precisa salvar projetos/casos em arquivo portavel?

## Minha leitura estrategica

Se eu resumir em uma frase:

> nao e para ser "mais um Modscan", e sim um "assistente de troubleshoot Modbus TCP com capacidade de substituicao de papel na rede e analise operacional orientada por mapa".

Essa diferenca e boa e defensavel. O centro do produto nao deve ser a tela de leitura de registradores; deve ser o caso de diagnostico.

## MVP implementado

Primeira versao tecnica criada em 2026-07-14.

### Stack escolhida

- .NET 9
- WPF para frontend Windows
- CommunityToolkit.Mvvm para MVVM
- Serilog para log em arquivo
- Core Modbus TCP proprio, sem biblioteca Modbus externa

### Como executar

```powershell
dotnet run --project .\ModbusTcpTroubleshooter.App\ModbusTcpTroubleshooter.App.csproj
```

### Como testar a comunicacao local

```powershell
dotnet run --project .\ModbusTcpTroubleshooter.SmokeTests\ModbusTcpTroubleshooter.SmokeTests.csproj
```

### Status

- build da solution completa passando
- smoke test local passando com FC03 e FC06
- botao `TESTE COMPLETO` executa checklist guiado com contexto, inventario passivo TCP, ARP local, conectividade TCP, carga de trafego, validacao do mapa, teste read-only de envio/recebimento e consolidacao de falhas
- o teste completo agora inclui interfaces/rotas IP, gateways, velocidade nominal de placas, medicao de banda por contadores da interface, varredura ativa limitada de hosts candidatos/sub-rede, descoberta de portas Modbus/configuradas e tabela visual de dispositivos encontrados
- etapas dependentes de captura passiva usam janela de observacao configuravel antes de declarar amostra insuficiente
- deteccao de polling rapido usa mediana de multiplas amostras e ignora bursts muito curtos para reduzir falso positivo
- cada etapa do teste completo agora inclui bloco `Interpretacao` com leitura direta dos numeros, percentuais, sinais de atencao e acao recomendada
- a inferencia de topologia aponta gateway, possiveis elementos de infraestrutura e lacunas de visibilidade; identificacao fisica de switches/portas exige evolucao futura com SNMP/LLDP/CDP ou integracao com switches gerenciaveis
- relatorio do teste completo pode ser exportado em Markdown
- por seguranca industrial, o teste completo nao executa escrita automatica em PLC/equipamento; escrita deve ser manual ou futura configuracao explicita de ponto seguro
- detalhes tecnicos em [[docs/MVP]]

### Proximos testes recomendados

- SNMP/LLDP/CDP para identificar switches gerenciaveis, portas fisicas, VLANs e topologia real
- deteccao de IP duplicado por ARP churn, MAC mudando para o mesmo IP e conflito entre ARP/captura
- histograma de tempo de resposta Modbus por endpoint, Unit ID, function code e range
- analise TCP de retransmissao, reset, janela zero, handshake incompleto e conexoes fechadas pelo equipamento
- varredura read-only por Unit ID e function code para descobrir mapa publicado sem escrever em PLC
- baseline de polling: taxa por cliente, ciclos fora do padrao, rajadas e mudanca de periodo ao longo do tempo
- deteccao de broadcast/multicast excessivo e protocolos nao industriais competindo na rede
- comparacao de mapa esperado x mapa observado passivamente, gerando mapa consumido real
- teste de estabilidade longo com resumo por janela de tempo, evitando lista gigante de eventos
