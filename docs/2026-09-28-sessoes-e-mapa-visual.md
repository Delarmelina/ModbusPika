# Sessoes simultaneas e mapa visual

## Operacao

1. Configure o servidor local pelo atalho Configuracoes Server. Sua porta e independente das portas dos clientes.
2. Em Cliente / Mestre, selecione um alvo e use Configuracoes Cliente ou Conectar Cliente.
3. Use + Adicionar alvo para cadastrar outros servidores remotos. Cada alvo possui socket, Unit ID, taxa de scan, modo de conexao e mapa proprios.
4. Selecione cada alvo antes de conectar ou desconectar. Desconectar um alvo nao para os outros clientes nem o servidor local.
5. Selecione Servidor local para visualizar o mapa simulado. Selecionar uma visualizacao nao altera a execucao das outras sessoes.

## Mapa

- Mapa de enderecos apresenta os pontos em blocos visuais, com filtro HR/IR/COIL/DI, busca por endereco base zero e paginas de 96 pontos.
- Tabela / configurar blocos mantem a configuracao e as tabelas detalhadas anteriores.
- Duplo clique, Enter ou Espaco em um bloco abre a edicao. No cliente, apenas HR e COIL podem ser escritos. No servidor, a edicao altera a memoria local, incluindo pontos somente leitura no protocolo.
- Nao lido nao apresenta um zero valido. Ao parar o scan, a ultima amostra e mantida com qualidade Leitura parada. Alterar um bloco invalida suas amostras anteriores.

## Diagnosticos

O Teste completo utiliza o papel e o alvo selecionados. Outras sessoes podem continuar comunicando. A timeline identifica a origem de cada evento; intervalos de polling e contadores de falha incluem a sessao no identificador. As etapas passivas do servidor usam apenas eventos do servidor local. A etapa Falhas observadas continua global e declara esse escopo no resultado.

Salvar caso inclui a configuracao de todos os clientes e a porta/Unit ID independentes do servidor local no JSON exportado. Nao foi adicionada importacao de configuracoes nesta revisao.

## Verificacao

Os testes de interface exercitam dois clientes simultaneos e um servidor local, escrita no alvo correto apos trocar a visualizacao, desconexao isolada, invalidacao de valores e paginacao de 200 pontos. Os testes de protocolo continuam cobrindo FC01/03/05/06, escrita fora de mapa/somente leitura, TID/UID/FC, byte count, MBAP e eco de escrita.
