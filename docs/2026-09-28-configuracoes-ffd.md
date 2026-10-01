# Configuracoes portateis (.ffd)

## Uso

- Arquivo > Salvar configuracao (ou o botao Salvar): escolhe o arquivo na primeira vez; depois atualiza esse mesmo arquivo.
- Arquivo > Salvar configuracao como: exporta outra copia, inclusive para enviar a outro computador.
- Arquivo > Abrir configuracao (.ffd): importa e substitui a configuracao atual apos confirmacao.
- Antes de abrir, pare o teste, o servidor, os scans dos clientes e a captura. Conexoes persistentes ociosas sao desconectadas na importacao.
- Dispositivos encontrados podem ser recolhidos como um unico grupo ou ocultados por Exibir > Dispositivos encontrados. Isso nao remove evidencias nem interrompe a captura.

## Conteudo

O arquivo armazena o nome do caso, modo e cliente selecionados, IP/porta/Unit ID do servidor, todas as faixas (inclusive desabilitadas), valores efetivos e permissoes dos pontos do servidor. Cada cliente conserva nome, IP, porta, Unit ID, intervalo de scan, modo de conexao persistente/por requisicao e seus blocos de leitura (nome, funcao, endereco, quantidade e habilitacao).

Resultados, trafego, dispositivos descobertos e estados de conexao nao fazem parte da configuracao. A exportacao de caso JSON continua disponivel separadamente. Parametros do teste completo continuam no armazenamento local existente; nao fazem parte da versao 1 do FFD.

O arquivo nao inicia comunicacao automaticamente. Leituras anteriores nao sao restauradas como OK. A descoberta de rede segue a configuracao local do teste e somente executa quando o operador inicia o teste completo.

## Formato e validacao

JSON UTF-8, extensao `.ffd`, assinatura `ModbusTcpTroubleshooter.FFD`, versao 1. Sem dependencia adicional ou dados binarios executaveis.

Validacao antes da substituicao: assinatura/versao, IPv4, portas, intervalos, funcoes/limites Modbus, correspondencia entre faixas e pontos, acessos e valores binarios. Limites: arquivo de 32 MB, 64 clientes, 1000 blocos por cliente, 65536 pontos por cliente, 1000 faixas e 262144 pontos de servidor.

O salvamento usa arquivo temporario no mesmo diretorio, seguido de substituicao. Configuracoes invalidas nao sobrescrevem um arquivo anterior. Se houver faixas editadas ainda nao aplicadas, aplique o mapa antes de salvar.

Verificacao automatizada: quatro clientes, diferentes modos de socket, mapas esparsos, valores personalizados HR/IR/COIL, faixa desabilitada, selecao preservada, importacao sem conexoes automaticas, rejeicao de versao/endereco invalido, preservacao de arquivo anterior e bloqueio durante servidor ativo.
