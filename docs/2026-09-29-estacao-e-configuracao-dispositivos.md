# Estacao cliente, dispositivos e diagnosticos globais

## Hierarquia

- **Estacao cliente**: cadastro dos servidores-alvo e estado das conexoes. Clique no cabecalho Cliente / Mestre da lateral ou use Conexoes > Cliente / Mestre > Gerenciar estacao.
- **Dispositivo: configuracao e mapa**: somente o servidor-alvo selecionado ou o servidor simulado local. O cabecalho informa papel, nome e endpoint.
- **Linha do tempo e avisos**: areas globais, com indicacao do escopo.
- **Teste completo**: area independente; operacao e navegacao documentadas em [[2026-09-29-teste-completo-independente]].

## Configurar um dispositivo

Selecione um alvo na lateral ou abra a linha correspondente na estacao. Use Configuracao do dispositivo para editar nome, IP, porta, ID da unidade, intervalo de leitura e persistencia TCP.

Aplicar confirma as alteracoes; Descartar restaura os campos. A comunicacao desse dispositivo deve estar parada. Outros clientes e o servidor local podem continuar funcionando, exceto durante testes que bloqueiam configuracoes.

Alterar IP, porta ou ID descarta a qualidade e os valores antigos recebidos, mantendo os blocos do mapa. Aplicar nao conecta automaticamente. Um socket persistente ocioso e encerrado antes da alteracao.

O servidor local tambem pode ser renomeado e configurado nessa area. Seu mapa permanece separado dos mapas dos servidores-alvo.

## Persistencia

Salvar/abrir configuracao .ffd inclui o nome da estacao e do servidor local, alem dos parametros e mapas existentes. Arquivos anteriores continuam usando nomes padrao quando esses campos nao existem.

## Verificacao

Testes automatizados cobrem isolamento entre alvos, invalidacao de leituras antigas, bloqueio durante comunicacao, validacao sem mutacao, rascunho/descartar, tabela da estacao e persistencia FFD. Os testes de comunicacao usam servidores locais, sem varrer a LAN.
