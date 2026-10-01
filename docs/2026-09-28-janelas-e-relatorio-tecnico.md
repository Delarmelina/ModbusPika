# Teste completo: janelas, evidencias e cobertura

## Configuracao

Em Teste completo > Configurar teste:

- Referencia passiva inicial: 1 a 3600 segundos, padrao 12. Ocorre antes de novas sondagens do teste; comunicacoes previamente ativas continuam.
- Monitorar linha do tempo TCP: 1 a 86400 segundos, padrao 30. Coleta operacional apos as sondagens, procurando RST, zero-window, segmentos repetidos e picos.
- Leituras por bloco: 1 a 1000, padrao 15. Intervalo adicional entre ciclos de 100 a 60000 ms, padrao 1000.
- Limiar de pico: pacotes/s configuraveis. E um criterio operacional, nao saturacao fisica do enlace.
- Sondagem adicional: desabilitada por padrao. Exige autorizacao e CIDR IPv4 explicito /22 a /32. Limites de taxa e concorrencia se aplicam a sondagens e descoberta de mapa; o polling existente tem sua propria taxa.

Os parametros sao salvos em `%LOCALAPPDATA%/ModbusTcpTroubleshooter/test-scope.json`. Autorizacao de sondagem de outros hosts nao e memorizada entre inicializacoes.

## Sequencia e seguranca

Captura/interface, contexto/rotas, referencia passiva, ARP, sondagem autorizada, confirmacao read-only de protocolo, descoberta opcional, leituras repetidas por alvo e monitoramento operacional. No modo servidor, correlacao externa e avaliada depois do monitoramento.

Alvos cadastrados sao explicitamente incluidos; IPs vistos em Internet, ARP e captura nao entram automaticamente na sondagem. Hosts do CIDR sao limitados ao escopo informado. Escritas automaticas continuam desabilitadas.

## Evidencias

Modbus passivo exige um ADU completo estruturalmente reconhecido, nunca apenas porta 502 ou SYN. A confirmacao ativa aceita leitura valida ou exception Modbus validada: uma exception confirma protocolo, nao range. Nao ha remontagem TCP completa, e ADUs segmentados/FCs nao suportados podem nao ser reconhecidos.

Contadores de janelas sao anteriores a fila UI, independentes das 2000 linhas da tabela. Medem a duracao inteira, usando timestamps para rejeitar historico e separar uma curta drenagem do buffer de captura. Descartes do driver e da UI sao distintos. Descarte de captura torna a carga inconclusiva.

Repeticao de sequencia/comprimento e indicio de retransmissao, nao prova de perda; duplicacao da captura/reuso de fluxo sao limites. RST tambem pode ser encerramento/rejeicao esperado. Sao identificadas conversas associadas aos sinais para correlacao, sem atribuir causa raiz automaticamente.

Leituras mostram validas/tentadas/planejadas, erros por categoria e horario, min/mediana/p95/max. Latencias incluem espera pelo socket compartilhado. P95 de amostra pequena tem baixa representatividade da cauda. P95 acima do scan configurado gera atencao contextual. Leituras de validacao sao excluidas da estimativa de intervalo de polling, pois adicionam trafego proprio.

Servidor usa contadores de requests/responses e ranges independentes das 500 linhas de timeline. Correlacao e limitada a 4096 pendencias/ranges; latencias por range conservam primeiras 1000 amostras. Ranges observados nao sao todo o mapa cadastrado do cliente.

## Relatorio

Resumo por servidor, achados prioritarios com evidencias/horarios/proxima verificacao, estatisticas por bloco, cobertura/limitacoes e comparacao entre janelas. Rotas, ARP, interfaces e inventario extensos ficam no apendice.

Estados: OK, Atencao, Falha, Inconclusivo e Nao aplicavel. O placar principal indica etapas processadas, nao porcentagem de estabilidade. Topologia fisica permanece inconclusiva sem SNMP/LLDP/CDP/inventario de switches. Loopback/endereco local nao exigem ARP; alvo roteado exige analisar gateway, nao ARP do destino.

## Validacao

Testes locais cobrem contadores acima da retencao UI, exclusao de historico, janela que nao termina cedo, ausencia de captura, 2000 correlacoes no servidor, classificacao de ADUs completos, dois servidores locais com leitura repetida e isolamento de falhas, execucao completa sem sondar outros hosts, relatorio e dialogo rolavel. Validacao em rede industrial e de longa duracao permanece necessaria.
