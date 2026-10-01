# Categorias de dispositivos e descoberta de portas

## Painel lateral

Categorias recolhiveis, exclusivas por IP; a evidencia Modbus prevalece sobre ARP ou sondagem TCP:

- Modbus encontrado: resposta ativa validada ou ADU de resposta/exception reconhecido passivamente. Portas confirmadas e observadas ficam separadas, inclusive no mesmo IP.
- TCP aberto / nao confirmado: handshake bem-sucedido, mas sem evidencia de resposta Modbus. Possiveis motivos incluem outro protocolo, Unit ID diferente, timeout ou filtro.
- Vizinhos IPv4 / ARP: entradas do cache ARP, associando IP e MAC; nao prova atividade atual nem identifica PLC.
- Outros hosts observados: captura ou sondagem, sem evidencia industrial. A antiga classificacao generica "Host industrial ou infraestrutura" foi retirada, pois nao tinha evidencias suficientes.
- Infraestrutura anunciada: identidades/portas LLDP/CDP disponiveis no teste, sem verificacao de integridade.
- Enderecos especiais: loopback/multicast e outros enderecos nao individuais.

Os dispositivos encontrados permanecem no painel lateral e no resultado Dispositivos/rede. Configurar escopo contem somente configuracoes e selecao dos alvos cadastrados, nao inventario descoberto.

## Portas e autorizacao

Configurar teste > Portas para descoberta Modbus aceita 1 a 16 portas entre 1 e 65535. Padrao 502,1501,1502; portas dos alvos selecionados e do servidor local tambem entram na sondagem autorizada. Lista e limites sao persistidos; autorizacao para sondar outros hosts continua desabilitada a cada inicializacao.

Para descobrir um servidor ocioso em 172.27.30.84:1501, habilitar sondagem adicional e autorizar, por exemplo, 172.27.30.84/32 (somente esse host), incluindo 1501 na lista. /24 amplia o escopo para a sub-rede e so deve ser usado com autorizacao correspondente. A primeira etapa testa o socket; a segunda confirma Modbus por leitura minima ou exception valida. Confirmacao usa o Unit ID do alvo cadastrado; para novos hosts, usa o cliente selecionado no modo Cliente ou o servidor local no modo Servidor. Ausencia de resposta nao comprova ausencia de Modbus.

Sem sondagem ativa, um servidor ocioso pode nao ser observado. Captura comum numa porta de switch nao observa necessariamente conversas entre outros PCs; sao necessarios trafego visivel ou SPAN/TAP. Respostas Modbus passivas reconhecidas aparecem na lateral independentemente da porta, com a limitacao de nao haver remontagem TCP completa.

## Validacao

Testes cobrem lista padrao incluindo 1501, rejeicao de portas invalidas, mudanca de categoria por evidencia, separacao por porta, preservacao de identidade apos ARP e servidores locais em portas nao padrao. Confirmacao em rede industrial depende de escopo, conectividade/firewall e Unit ID.
