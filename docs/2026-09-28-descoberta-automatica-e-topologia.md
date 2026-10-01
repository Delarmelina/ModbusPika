# Descoberta automatica da rede e topologia visivel

## Comportamento

O teste completo descobre todos os enderecos IPv4 da sub-rede local por padrao. O segmento e determinado pela interface de captura (GUID Npcap associado a placa Windows), endereco local selecionado ou unica interface fisica ativa. Se houver ambiguidade, exige selecionar a interface ou configurar CIDR; nao adivinha qual rede industrial usar. A lista ARP nao limita os candidatos.

Redes /16 a /32, incluindo /31 ponto a ponto, sao suportadas sem truncamento silencioso. /24 implica 254 candidatos, mesmo com apenas quatro equipamentos ativos. Uma /16 pode demorar horas na taxa padrao de cinco sondagens por segundo. Concorrencia e taxa permanecem configuraveis; a varredura e cancelavel. Paralelismo limitado evita criar uma tarefa para cada endereco de uma rede grande.

Cada IP recebe teste ICMP e conexao nas portas de descoberta configuradas (padrao 502,1501,1502), mais portas dos alvos/local. Ping nao e pre-requisito para sondar TCP. Portas abertas recebem leitura Modbus minima e validacao de TID/UID/FC/MBAP, inclusive exceptions validas. Equipamento desconhecido: tenta Unit ID 1 antes do ID configurado localmente. Alvo cadastrado: usa seu Unit ID e bloco. A interface exibe portas sondadas e distingue ausencia de sondagem de resposta nao confirmada.

Descoberta de mapa habilitada: exercita todos os endpoints Modbus confirmados, sem limite anterior de oito, usando o Unit ID que respondeu. No modo servidor, mantem tambem a observacao dos ranges requisitados pelos clientes externos. Leitura somente, limitada aos FCs e enderecos configurados; nao equivale ao cadastro completo do PLC.

Verificar IP continua disponivel como atalho opcional, nao como requisito do teste de rede.

## Identificacao

- Servidor Modbus: resposta de leitura/exception validada ativamente ou ADU de resposta reconhecido na captura.
- Cliente Modbus: requisicao reconhecida passivamente ou recebida pelo servidor local. Nao requer porta TCP de servidor aberta.
- Infraestrutura: anuncios LLDP/CDP recebidos, gateway e rotas ICMP como evidencias separadas.
- Outros hosts: resposta de rede sem evidencia suficiente do papel. ARP nao identifica modelo, PLC ou switch.

Nao e possivel determinar o papel de um cliente silencioso ou identificar todo switch L2 apenas por varredura TCP/ICMP. Em rede comutada, comunicacoes entre terceiros exigem captura em porta espelhada/TAP para visibilidade. Portas nao configuradas nao sao varridas; nao se enviam PDUs Modbus a todos os servicos arbitrarios.

## Topologia

Topologia agora e uma aba principal, controlada por Exibir > Topologia e acessivel pelo botao Ver topologia do teste. O diagrama, conversas, rotas e vizinhos anunciados deixaram de ficar escondidos na tela antiga de detalhes.

O relatorio preserva tabelas e enlaces textuais e inclui diagramas visuais na exportacao. PDF incorpora as imagens no arquivo. Markdown gera arquivos `.topologia-NN.png` na mesma pasta, referenciados pelo `.md`; envie esses arquivos juntos. Diagrama limitado aos 30 enlaces prioritarios, em grupos de seis para legibilidade. Nao representa uma planta de cabeamento.

## Validacao

Regressoes em loopback: descoberta de porta nao padrao, resposta Modbus versus porta fechada, probe individual sem varredura LAN, candidatos completos para 172.27.30.0/24 incluindo .84 sem entrada ARP, topologia em aba principal e exportacao dos diagramas em MD/PDF. Os testes automatizados nao sondam a LAN real.
