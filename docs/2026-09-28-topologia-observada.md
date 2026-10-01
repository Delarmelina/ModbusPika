# Topologia observada

## Operacao

Teste completo > Detalhes > Topologia apresenta diagrama de enlaces, conversas, rotas ICMP e vizinhos LLDP/CDP. O relatorio MD/PDF inclui um diagrama logico textual e tabelas de evidencias.

Configurar teste e topologia permite habilitar rastreamento de rotas, ajustar saltos (1-30), timeout (200-3000 ms) e limite de alvos (1-32). Padrao: habilitado, 12 saltos, 500 ms, 8 alvos; uma consulta por TTL, limitada pela taxa de sondagens. No cliente, somente alvos selecionados. No servidor, somente clientes com requisicoes Modbus observadas na aplicacao. Sem DNS e sem escrita/configuracao de equipamentos.

## Evidencias e limites

- Captura: fluxos direcionais agrupados por par de hosts/protocolo, antes do limite da fila UI. Ultimo endpoint/porta aparece no detalhe; nao e um inventario de todas as portas. Sondagens proprias sao excluidas. Limite de 4096 fluxos; tabela ate 200 por volume; diagrama ate 30 enlaces prioritarios.
- Modbus confirmado: resposta validada confirma protocolo e conectividade logica, nao cabeamento.
- Gateway configurado: configuracao IPv4 local, nao prova de que o alvo utiliza aquela rota.
- ICMP: consulta com TTL crescente equivalente ao principio do tracert. Mede tempo da consulta, uma amostra por salto; nao mede perda percentual, caminho de retorno ou latencia Modbus. Timeout/limite de saltos significa rota parcial, nao falha de PLC. Switches de camada 2 nao aparecem no TTL.
- LLDP/CDP: identidade/porta anunciadas passivamente. Parser Ethernet suporta ate duas tags VLAN; LLDP verifica TLVs obrigatorios e fim, CDP verifica checksum/TLVs. TTL zero retira o anuncio e anuncios expirados nao aparecem como vizinhos atuais. Ate 1024 vizinhos. Sem anuncios ou filtro excludente nao implica ausencia de switches.
- BPF Todos em Ethernet inclui LLDP e multicast CDP. Filtros IP/porta ainda restringem anuncios; nao sao ignorados. Loopback nao possui vizinhanca Ethernet. Uma captura previamente iniciada conserva seu filtro.
- Nao ha consultas SNMP, tabela MAC do switch, inventario remoto LLDP, portas/VLANs ou verificacao da integridade dos switches.

O estado OK se refere ao mapeamento logico executado. Infraestrutura fisica permanece explicitamente nao verificada. Rotas parciais/limites geram Atencao; ausencia de evidencias gera Inconclusivo.

## Validacao

Testes UI cobrem rastreamento ate loopback, relatorio, diagrama responsivo, LLDP normal/VLAN/truncado, CDP valido/corrompido, e entradas aleatorias. Rede industrial, rotas com multiplos roteadores e anuncios de equipamentos reais ainda precisam de validacao em campo.
