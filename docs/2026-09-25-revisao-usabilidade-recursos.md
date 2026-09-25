# Revisao do Modbus TCP Troubleshooter

Data: 2026-09-25. Revisao do codigo local, fluxos WPF, motor Modbus, captura, diagnosticos, persistencia e distribuicao. Mantido o direcionamento de interface Windows simples, menus convencionais, configuracao em popups e abas independentes.

## Correcao entregue nesta revisao

1. Full Test ganhou Cancel Test. Interrompe etapas, identifica cancelamento e disponibiliza relatorio parcial. Scan e captura permanecem controles independentes; o botao explica isso.
2. Full Test libera o estado de execucao em finally, inclusive em falhas na preparacao. Antes, uma excecao podia deixar o teste permanentemente em execucao.
3. Configuracoes de teste e conexao ficam bloqueadas durante o Full Test, assim como novos comandos manuais de inicio/leitura.
4. Cabecalho e relatorio exibem o endpoint do modo ativo, em vez de sempre mostrar o alvo Client.
5. Em Server, conectividade verifica o listener local, mapa verifica a configuracao local e envio/recebimento correlaciona requests e respostas observados. Os resultados explicam que isso nao equivale a uma varredura ativa do mapa Client.
6. Disconnect solicita cancelamento e espera o loop terminar antes de permitir novo scan. O loop usa seu proprio token, evitando que um scan anterior assuma o token do seguinte.
7. Cancelamento solicitado pelo operador nao gera um erro de comunicacao por cada bloco restante do scan.
8. Timeout de sondagem aceita 200-3000 ms; concorrencia 4-128; bloco de descoberta 1-120. Os limites da interface agora correspondem ao motor, eliminando reducoes silenciosas. Faixa de Unit IDs invertida e rejeitada.
9. Leitura com quantidade zero, excesso por funcao ou endereco final acima de 65535 e rejeitada antes do envio.
10. Cliente valida TID, Unit ID, FC, tamanho MBAP, byte count e eco de escrita. Uma resposta inconsistente nao pode mais ser marcada como sucesso.
11. Geracao de TID usa incremento atomico para chamadas concorrentes.
12. Servidor rejeita escrita fora do mapa ou em ponto R/O. Faixas multiplas sao verificadas antes de escrever. FC05 com valor invalido e payload multiplo incompleto retornam exception.
13. Permissao Writable da faixa passa a ser aplicada ao mapa real. Editar o valor local do simulador preserva essa permissao.
14. Dicionarios do mapa permitem acesso concorrente; a atualizacao local de um ponto nao invalida enumeracoes simultaneas.
15. Servidor valida tamanho do cabecalho e trata desconexao, cancelamento e erro de socket por cliente.
16. FC05/FC06 passam a representar quantidade 1 na extracao do range; antes o valor escrito era interpretado como quantidade.
17. Atualizacao da tabela Server preserva as linhas quando a estrutura nao mudou, evitando reconstruir a tabela inteira a cada evento e perder selecao/edicao.
18. Fila de captura limitada a 10000 entradas, com aviso consolidado de descarte. Isso protege memoria; nao transforma amostra descartada em captura completa.
19. Fechar a janela cancela operacoes, para o timer e libera a captura.
20. Idioma e salvo por usuario em LocalAppData/ModbusTcpTroubleshooter/language.txt. Menus e textos estaticos sao traduzidos preservando bindings. A cobertura de traducoes ainda e parcial.
21. Popup de escrita ajusta altura ao conteudo; Enter confirma e Esc cancela.

## Validacao realizada

- Compilacao WPF e Core.
- Testes locais FC01/03/05/06 e confirmacao dos valores escritos.
- Escrita R/O e fora de mapa rejeitada, sem modificar dados.
- Servidor falso em loopback devolvendo TID, UID, FC, byte count, comprimento MBAP e eco de escrita incorretos: cliente rejeitou os seis cenarios.
- Testes WPF de bloqueio entre papeis, controles do Full Test, endpoint Server, troca de idioma nos dois sentidos e preservacao de binding.
- Abertura e verificacao de dimensoes de controles em cinco popups: Client, Server, escopo, descoberta e escrita, na escala atual do Windows.
- Nao foi executada varredura na rede industrial. Captura de 24 horas, multiplas escalas DPI e comportamento sob carga de muitos clientes ainda exigem ensaio dedicado.

## Decisoes para discutir

Prioridade P0 = confiabilidade dos resultados; P1 = operacao cotidiana; P2 = evolucao. As opcoes abaixo nao foram implementadas nesta rodada.

### Comunicacao e simulacao

| ID | Prioridade | Achado / proposta | Decisao necessaria |
| --- | --- | --- | --- |
| C01 | P0 | Connect mantem polling, mas ModbusTcpClientProbe abre um socket por transacao. Isso altera carga, contagem de conexoes e latencia frente a um PLC real. | Conexao persistente por endpoint como padrao, com opcao de reconectar a cada request? |
| C02 | P0 | Scan aguarda o tempo configurado depois de concluir os blocos; o periodo real inclui o tempo das requisicoes. | Taxa deve significar periodo total do ciclo ou pausa entre ciclos? Exibir ambos. |
| C03 | P0 | Unit ID configurado no Server nao filtra os requests recebidos. | Aceitar todos, filtrar um UID, ou mapas distintos por UID? Nao alterar silenciosamente compatibilidade de simulacao. |
| C04 | P1 | Client e Server compartilham Port e UnitId no ViewModel. | Guardar dois perfis independentes e definir migracao dos valores existentes. |
| C05 | P1 | Falta estado Connected separado de Polling/Connecting/Retrying/Disconnected. | Quais estados e politica de retry/backoff devem ser expostos? |
| C06 | P1 | Read Once e escrita podem se sobrepor a outras operacoes; faltam filas por endpoint e telemetria de pendencias. | Serializar por conexao ou permitir paralelismo configuravel? |
| C07 | P1 | Falta estatistica de ciclo: tentativas, respostas validas, exceptions, timeouts, ultimo sucesso, min/media/p95/max. | Janela da sessao inteira, janela movel ou ambas? |
| C08 | P2 | Simulacao de atraso, exception, desconexao e valores variaveis nao existe. | Criar perfil de injecao de falhas para bancada, com limites e identificacao visivel. |

### Mapas e escrita

| ID | Prioridade | Achado / proposta | Decisao necessaria |
| --- | --- | --- | --- |
| M01 | P0 | Faixas Server sobrepostas usam a ultima configuracao, sem politica explicita; nomes personalizados nao chegam integralmente ao mapa efetivo. | Rejeitar sobreposicao ou permitir override explicitamente? |
| M02 | P1 | Numeracao base zero e referencias 40001/30001 podem ser confundidas. | Exibir endereco de protocolo e referencia documental em colunas distintas. |
| M03 | P1 | Valores sao inteiros de 16 bits; faltam signed, hex, binario, float32/64 e ordem de palavras/bytes. | Definir tipos por bloco ou por tag, preservando visualizacao raw. |
| M04 | P1 | Qualidade OK continua visivel quando a comunicacao para; falta idade do dado e estado Stale. | Prazo em multiplos do scan ou timeout por tag? |
| M05 | P1 | Duplo clique na configuracao abre escrita em faixa e conflita com a edicao dos campos. | Reservar duplo clique para editar bloco e usar Write Range no menu de contexto? |
| M06 | P1 | Escrita em faixa usa varias FC05/FC06, podendo terminar parcialmente. | Acrescentar FC15/FC16, progresso, cancelamento e resultado por endereco. |
| M07 | P1 | Importacao/exportacao CSV/JSON e templates por fabricante facilitariam preparar testes. | Formato canonico, campos obrigatorios e politica de importacao parcial. |
| M08 | P2 | Falta busca, filtro por qualidade, favoritos e congelamento de colunas. | Priorizar busca/endereco e somente falhas antes de alterar o layout. |

### Captura e diagnostico de rede

| ID | Prioridade | Achado / proposta | Decisao necessaria |
| --- | --- | --- | --- |
| N01 | P0 | Timeline retida tem 2000 pacotes; Modbus tem 500 eventos e diagnosticos 200. Taxa calculada sobre amostra retida nao e medicao integral. | Separar contadores agregados de longa duracao da lista da UI. Mostrar inicio/fim, descartes e cobertura da amostra. |
| N02 | P0 | Selecionar interface por maior trafego pode escolher Wi-Fi/internet em vez da interface industrial. | Priorizar rota do alvo, IP local e atividade pertinente; permitir interface fixada pelo operador. |
| N03 | P0 | Decode Modbus usa payload individual e heuristica; nao reagrupa segmentos TCP nem mensagens concatenadas. | Implementar reassembly por fluxo antes de prometer analise equivalente ao Wireshark. |
| N04 | P1 | Popup de pacote mostra resumo; raw bytes e cabecalhos completos nao sao preservados. | Retencao de bytes + painel hex/campos + exportacao PCAPNG. |
| N05 | P1 | Sessao de 24 horas precisa de arquivos rotativos e limites de disco; buffer da UI nao atende esse requisito. | Escolher limite por tempo/tamanho, pasta, retencao e informacao de descarte. |
| N06 | P1 | Faltam retransmissao, RTT, zero-window, resets e tentativas sem resposta por fluxo. | Distinguir medicao real de heuristica e declarar visibilidade SPAN/TAP/host. |
| N07 | P1 | Filtro de captura e filtro visual tem efeitos diferentes, pouco evidentes. | Dois grupos nomeados, resumo legivel, limpar filtros e presets. |
| N08 | P1 | ARP e IP nao identificam switches L2 com confianca. | Integrar LLDP/SNMP read-only e credenciais locais; topologia grafica somente com fonte/confiança explicitas. |
| N09 | P2 | Ping em sequencia, traceroute e verificacao de duplicidade IP seriam uteis. | Quantidade/intervalo configuraveis e diferenciar ausencia de ICMP de host indisponivel. |

### Full Test e relatorios

| ID | Prioridade | Achado / proposta | Decisao necessaria |
| --- | --- | --- | --- |
| T01 | P0 | Ainda ha estado historico compartilhado: avisos anteriores podem entrar na conclusao atual; ARP/descoberta continuam usando o alvo Client em partes do fluxo. | Criar contexto imutavel por execucao com papel, alvos, configuracao e colecoes proprias. |
| T02 | P0 | Descoberta tem limites internos de hosts/endpoints e enderecos; relatorio pode parecer mais completo do que a cobertura real. | Exibir planejado/executado/ignorado e motivo, com limites configuraveis. |
| T03 | P0 | Cancel Test coopera com os tokens; algumas sondas/processos internos so terminam no timeout e o resultado parcial pode demorar. | Uniformizar cancelamento em todas as tarefas/filhos, com prazo de encerramento observavel. |
| T04 | P1 | Falta checklist de etapas selecionaveis e estimativa de duracao/carga antes de iniciar. | Perfis Rapido, Passivo, Completo e Personalizado com escopo visivel. |
| T05 | P1 | Descoberta por reads prova faixa legivel, nao tipo fisico, escala ou nome de tag. | Importar mapa oficial e apresentar descobertas como evidencias parciais. |
| T06 | P1 | Ausencia de trafego/gateway pode significar rede isolada ou amostra insuficiente, nao necessariamente falha. | Estados Nao aplicavel, Inconclusivo e Sem amostra separados de Falha. |
| T07 | P1 | Faixas genericas de latencia podem nao servir a todos os PLCs. | Limiares por perfil de equipamento/processo e evidencia numerica para cada classificacao. |
| T08 | P1 | Relatorio precisa de versao do software, configuracao integral, periodo, interface, filtros, cobertura e limitacoes. | Markdown/HTML primeiro; PDF assinado e comparacao entre execucoes depois. |

### Interface, configuracao e manutencao

| ID | Prioridade | Achado / proposta | Decisao necessaria |
| --- | --- | --- | --- |
| U01 | P1 | Idioma ainda parcial: catalogo por texto nao cobre todas as colunas, validacoes e diagnosticos; termos PT/EN misturados. | Migrar para recursos identificados por chave, separar codigo de evento do texto e decidir idioma de exportacao. |
| U02 | P1 | Falta salvar/reabrir uma sessao completa; CaseStorage existe, mas o menu atual nao oferece fluxo completo. | File > New/Open/Save/Save As com formato versionado, perfis e aviso de alteracoes pendentes. |
| U03 | P1 | Falta estado vazio util nas tabelas e feedback quando nao ha bloco habilitado. | Mensagens com proxima acao e navegacao direta para configuracao. |
| U04 | P1 | Dialogos ainda usam muitas dimensoes fixas. | Validar 100/125/150/200% DPI e telas menores; layout por conteudo com scroll quando necessario. |
| U05 | P1 | Rotulos abreviados e informacoes importantes apenas por cor dificultam operacao. | Tooltips tecnicos, legenda, navegacao por teclado e nomes acessiveis. |
| U06 | P1 | Fechar abas, reabrir a ultima e guardar larguras/ordem de colunas ainda nao e consistente. | Persistir preferencias visuais sem reintroduzir docking, conforme direcao atual. |
| U07 | P1 | Logs usam caminho relativo ao diretorio de trabalho; falhas de inicializacao nao tem apresentacao central. | Pasta por usuario, Open Logs, versao/build e tratamento global com arquivo de falha. |
| U08 | P2 | Codigo de UI e diagnosticos concentrado em MainViewModel grande. | Extrair servicos de sessao, captura, test runner e mapas, com contratos testaveis. |
| U09 | P2 | Builds precisam de versao visivel, notas, checksum e teste em maquina limpa. | Pipeline de Release e assinatura digital; decidir ZIP portatil versus instalador. |

## Ordem sugerida para a proxima rodada

1. C01, C02, C03 e C04: definir exatamente o significado de conexao, scan e papel ativo.
2. T01, N01 e T02: garantir que cada relatorio descreva apenas sua execucao e sua cobertura real.
3. U02, M02, M03 e M04: perfis salvos, enderecamento, tipos e idade do dado.
4. N03, N04, N05 e N06: captura persistente e analise TCP mais profunda.
5. U01, U04 e U05: completar idioma e validacao de interface em escalas diferentes.

As escolhas acima permanecem abertas para Felipe. Nao foi adicionado docking, alteracao automatica de IP do Windows, escrita automatica em PLC nem integracao SNMP nesta rodada.
