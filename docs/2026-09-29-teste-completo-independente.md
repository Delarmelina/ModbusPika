# Teste completo: escopo independente e resultados centralizados

## Operacao

1. Abra Teste completo > Execucao.
2. Escolha o papel Cliente / Mestre ou Servidor / Escravo.
3. Como cliente, use Selecionar alvos para incluir os servidores cadastrados. Como servidor, o teste usa o simulador local.
4. Configure procedimentos: referencia passiva, monitoramento TCP, tentativas/intervalo por bloco, sondagem autorizada da rede e rotas ICMP.
5. Opcionalmente habilite a descoberta de mapa e ajuste suas faixas no popup proprio.
6. Execute e acompanhe as etapas e evidencias.
7. Consulte Resumo, Dispositivos / rede, Topologia, Mapa descoberto e Relatorio nas subabas da mesma area. Exportar MD / PDF fica em Relatorio.

## Escopos e seguranca

O papel e os alvos do teste sao independentes de SelectedMode e SelectedClientSession. IP, porta, ID e mapa continuam sendo parametros do dispositivo cadastrado, sem campos de edicao de endpoint no teste.

A selecao de alvos utiliza rascunho no popup. Cancelar nao altera o escopo. Cliente sem alvos incluidos nao pode iniciar. O papel e a lista de sessoes sao capturados ao iniciar a execucao; mudar a selecao lateral nao interfere no plano.

A configuracao da proxima execucao pode ser alterada sem apagar resultados anteriores. O cabecalho informa o escopo da execucao registrada; so uma nova execucao ou Nova sessao substitui/limpa os resultados.

Sondagem da sub-rede e captura podem observar equipamentos nao incluidos na validacao por alvo. A selecao controla as etapas TCP/Modbus dos servidores cadastrados, nao um filtro global do trafego.

Cliente e servidor podem continuar funcionando simultaneamente. Inicio automatico do servidor pelo teste nao muda a visualizacao selecionada. Escritas Modbus nao sao executadas.

## Navegacao

A antiga tela Detalhes do teste e a aba principal separada de Topologia foram removidas. Clicar em dispositivos encontrados abre somente um popup de evidencias. Duplo clique na tabela Dispositivos / rede usa esse mesmo popup, sem iniciar testes nem alterar o dispositivo selecionado.

## Persistencia e verificacao

Arquivos FFD incluem o papel e os indices dos alvos incluidos. Arquivos antigos, sem esses campos, usam o papel Cliente e incluem todos os alvos cadastrados.

Regressoes automatizadas verificam papel/alvos independentes, preservacao do plano ao selecionar dispositivos, rascunho no popup, bloqueio sem alvos, preservacao dos resultados, inicio automatico do servidor sem mudar SelectedMode, role correto nas etapas/relatorio e resultados aninhados. Testes de protocolo, comunicacao simultanea, FFD e exportacao de relatorios permanecem ativos.
