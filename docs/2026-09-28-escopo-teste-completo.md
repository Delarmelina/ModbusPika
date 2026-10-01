# Escopo do teste completo

## Cliente / Mestre

- Desmarcado `Testar todos os servidores`: somente o alvo selecionado.
- Marcado: todos os alvos cadastrados na lista de clientes, inclusive parados.
- O teste solicita leitura automatica dos alvos incluidos; os scans continuam apos o teste, como anteriormente.
- TCP, mapa habilitado e request/response possuem etapas individuais por alvo, com IP, porta e Unit ID proprios.
- Falha em uma etapa nao impede testar os demais alvos. Cancelamento interrompe a execucao.
- O relatorio apresenta uma tabela comparativa por servidor.
- A sondagem nao escreve valores. Hosts descobertos na rede nao entram automaticamente no escopo de validacao dos mapas configurados.

## Consolidacao

Os avisos usados no fechamento sao coletados separadamente desde o inicio de cada teste, apenas para as sessoes incluidas. Eventos antigos e erros de outros clientes permanecem no registro global, mas nao reprovam um teste de alvo selecionado. Os contadores de padrao de comunicacao iniciam nova amostra.

A conclusao agrega as etapas; seu status de falha nao representa uma segunda falha independente. Escrita observada sem erro de protocolo e informativa. Erros de leitura/conexao nao recebem o rotulo de timeout indiscriminadamente.

## Limites

A captura passiva continua respeitando a interface e o filtro BPF configurados. Pode nao visualizar outros alvos se houver filtro restritivo ou rotas por outras interfaces. Os testes ativos TCP/Modbus por alvo independem da captura.

## Validacao local

Testes de regressao verificam exclusao de historico/outros clientes, inclusao de erro atual, limpeza entre execucoes, inclusao de outro alvo no escopo de todos os servidores e validacao real de mapas em dois servidores locais simultaneos. Validacao em rede industrial permanece necessaria.
