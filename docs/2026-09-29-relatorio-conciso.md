# Relatorio de diagnostico orientado a evidencias

## Leitura principal

O relatorio distingue o resultado dos alvos exercitados da descoberta de protocolo em outros endpoints. A descoberta nao demonstra validacao repetida de mapa. Percentual de sucesso usa tentativas efetivamente realizadas; planejadas permanecem visiveis para identificar cobertura incompleta.

As leituras aparecem em tabela, com mediana, p95, scan e interpretacao por transacao. A comparacao nao certifica o ciclo inteiro nem o processamento isolado do PLC. Amostras pequenas e captura limitada permanecem explicitadas.

## Reducao de ruido

Etapas OK ficam no checklist com duracao. Etapas com ocorrencias mantem o resultado completo no apendice e a recomendacao no resumo de achados. Inventario ARP e conversas genericas continuam disponiveis nas subabas do teste, mas nao sao reproduzidos integralmente nos exports.

A topologia exportada prioriza evidencias Modbus, ICMP e infraestrutura anunciada/configurada. Conversas genericas nao entram no diagrama. MD/PDF substituem o diagrama textual pelo visual para nao duplicar os enlaces. A ausencia de LLDP/CDP nao demonstra ausencia de switches.

## Dois exports

`Exportar resumo...` preserva a leitura focada acima. `Exportar completo...` acrescenta resultado integral de cada etapa, inventario de IPs observado, conversas capturadas e notas do mapa descoberto. Cada acao permite selecionar MD ou PDF. Ambos retratam a execucao finalizada, nao novas sondagens.

O software diferencia endpoint (IP:porta) de grupo de host por IP. Loopback e enderecos IPv4 atribuidos as interfaces ativas deste computador pertencem a uma unica identidade local; IPs remotos distintos continuam separados por falta de evidencia para consolida-los fisicamente. A lista lateral consolida os aliases locais sem apagar as linhas IP/porta do inventario completo.

## Verificacao

Testes de regressao cobrem leituras, export MD/PDF, preservacao de evidencias completas em falhas, exclusao de trafego generico no diagrama, ausencia de diagramas duplicados, PDF completo e consolidacao de aliases locais. Os testes nao fazem varredura ativa da LAN.
