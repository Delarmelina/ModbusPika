import json
from pathlib import Path
import pymupdf

ROOT = Path(__file__).resolve().parent
ASSETS = ROOT.parent / 'assets' / 'pratica'
PDF = Path('C:/Users/felip/Downloads/2026-10-02-16-36-teste-modbus-completo.pdf')
doc = pymupdf.open(PDF)
original = json.loads((ROOT / 'slides-pratica.json').read_text(encoding='utf-8-sig'))
config = []
report = []

def slide(target, title, image, body, notes, kind='wide', caption=None):
    entry = dict(kind=kind, title=title, section='05 · Configuração item a item' if target is config else '07 · Relatório real: leitura guiada', image=image, body=body, notes=notes)
    if caption:
        entry['caption'] = caption
    target.append(entry)

def cfg(title, image, body, notes, kind='wide'):
    slide(config, title, image, body, notes, kind)

def crop(name, page, start=None, end=None, height=210, x0=28, x1=814):
    p = doc[page-1]
    y0 = 34
    if start:
        hits = p.search_for(start)
        if not hits:
            raise ValueError(f'Heading not found: {page} {start}')
        exact = []
        for block in p.get_text('dict')['blocks']:
            for line in block.get('lines', []):
                if ''.join(span['text'] for span in line['spans']).strip() == start:
                    exact.append(pymupdf.Rect(line['bbox']))
        y0 = (exact[0] if exact else hits[0]).y0 - 0.5
    y1 = min(560, y0 + height)
    if end:
        hits = p.search_for(end)
        if hits and hits[0].y0 > y0:
            y1 = min(y1, hits[0].y0 - 0.1)
    drawings = p.get_drawings()
    crossing_table = any(item['rect'].width < 1 and item['rect'].height > 1 and item['rect'].y0 < y1 < item['rect'].y1 for item in drawings)
    if crossing_table:
        boundaries = [item['rect'].y0 for item in drawings if item['rect'].height < 0.5 and item['rect'].width > 50 and y0 + 15 < item['rect'].y0 <= y1]
        if boundaries:
            y1 = max(boundaries) + 0.4
    else:
        for block in p.get_text('dict')['blocks']:
            for line in block.get('lines', []):
                r = pymupdf.Rect(line['bbox'])
                if r.y0 + 1 < y1 < r.y1 - 1:
                    y1 = r.y0 - 0.5
    rect = pymupdf.Rect(x0, y0, x1, y1)
    p.get_pixmap(matrix=pymupdf.Matrix(2.5,2.5), clip=rect).save(ASSETS / f'{name}.png')
    return name

def rpt(title, page, start, body, notes, end=None, height=210, x0=28, x1=814):
    image = crop('relatorio-' + str(len(report)+1).zfill(2), page, start, end, height, x0, x1)
    slide(report, title, image, body, f'Fonte: PDF fornecido por Felipe, página {page}. '+notes, caption=f'Recorte do relatório fornecido. Página {page} de 34. Execução em 02/10/2026.')

cfg('Selecionar alvos: significado de cada coluna','alvos-teste',[
    'Incluir: marca quais cadastros receberão validação repetida.',
    'Nome: identificação local do servidor-alvo.',
    'Endpoint e ID: IP:porta e Unit ID cadastrados.',
    'Estado: situação da sessão, independente da inclusão no teste.'
], 'A seleção é independente do item lateral. Campos de cadastro são somente leitura nesta janela. Alterar IP, porta ou mapa na aba Dispositivo. Um alvo parado pode participar e ser iniciado pelo teste. Um alvo em leitura pode continuar acrescentando tráfego durante a referência. Exemplo: incluir apenas o PLC da bancada e deixar o PLC demonstrativo desmarcado.')
cfg('Selecionar alvos: ações e teste sem cadastro','alvos-teste',[
    'Selecionar todos: marca todos os alvos cadastrados.',
    'Limpar seleção: remove a inclusão de todos os alvos.',
    'OK confirma. Cancelar descarta a alteração da seleção.',
    'Sem alvos: a descoberta depende do escopo autorizado.'
], 'OK apenas grava a seleção, não conecta imediatamente. Sem alvo selecionado, Cliente/Mestre pode descobrir servidores no escopo autorizado e validar os confirmados conforme o mapa disponível. Não presumir que todo host de ARP será sondado fora do escopo. No papel Servidor/Escravo, o serviço local observa clientes que o procuram, não usa essa lista como destino de leituras. Demonstrar revisão antes de executar.')
cfg('Procedimentos: referência passiva inicial','procedimento-grupo-0',[
    'Referência inicial: janela de observação anterior às novas sondagens.',
    'Unidade: segundos. Faixa aceita: 1 a 3600 s.',
    'Polling e serviços já ativos continuam durante a referência.',
    'Exemplo: 10 s permite comparar com a janela operacional.'
], 'A referência não representa rede silenciosa. O teste adia suas novas leituras e sondagens, mas não suspende comunicação já existente. A interface e BPF limitam a amostra. Aumentar para 30 a 60 s pode representar melhor tráfego variável, conforme autorização e duração aceitável. Comparar taxas normalizadas por segundo, não só totais de pacotes.')
cfg('Procedimentos: monitoramento e limiar de pico','procedimento-grupo-0',[
    'Monitorar TCP: duração final, de 1 a 86400 segundos.',
    'Pico: número de quadros no intervalo de 1 segundo.',
    'Limiar pkt/s: dispara atenção ao ultrapassar o valor configurado.',
    'O limiar precisa refletir a referência da sua rede.'
], 'O monitoramento final ocorre após sondagens, sem nova varredura de hosts nessa janela. Procura RST, zero-window, segmentos repetidos e picos. 5000 pkt/s é limiar operacional configurável, não capacidade universal de PLC nem saturação física. Tempo maior favorece observar intermitências, mas uma ausência de sinais só vale para o período e tráfego visíveis.')
cfg('Procedimentos: tentativas por bloco','procedimento-grupo-1',[
    'Tentativas: leituras planejadas para cada bloco habilitado.',
    'Faixa aceita: 1 a 1000 tentativas por bloco.',
    '15/15 válidas: todas as tentativas executadas responderam.',
    'Mais amostras ajudam a investigar falhas raras e latência.'
], 'A quantidade multiplica-se pelo número de blocos e alvos incluídos. Separar planejadas, tentadas e válidas quando houver cancelamento. 15/15 não prova estabilidade permanente. Menos de 20 respostas não caracteriza a cauda de latência. Descrever timeout, exceção e falha TCP separadamente. Essas são leituras adicionais ao polling existente, sem escrita.')
cfg('Procedimentos: intervalo adicional entre ciclos','procedimento-grupo-1',[
    'Intervalo: pausa adicional entre ciclos.',
    'Unidade: ms. Faixa aceita: 100 a 60000 ms.',
    'A duração inclui transações, timeouts e a pausa.',
    '15 ciclos a 1000 ms somam cerca de 14 s de pausas.'
], 'Para 15 tentativas há aproximadamente 14 pausas entre elas. A duração real soma o tempo das leituras de todos os blocos. Não confundir essa configuração com o intervalo de polling do cadastro do alvo. O socket pode ser compartilhado com polling ativo e a espera participa da latência medida. O exemplo do relatório leva 14,1 s para 15 leituras rápidas.')
cfg('Procedimentos: autorização e CIDR','procedimento-grupo-2',[
    'Autorizar sondagem: habilita requisições ativas na rede.',
    'CIDR: delimita a sub-rede quando o escopo é manual.',
    'Exemplo: 192.168.1.0/24 representa aquela sub-rede IPv4.',
    'Automático: usa a sub-rede da interface escolhida.'
], 'A autorização refere-se à sondagem, mesmo que somente leitura. Ela pode aumentar carga e gerar registros no equipamento. O campo CIDR manual é usado quando a identificação automática da sub-rede estiver desmarcada. Conferir VLAN, máscara e interface na revisão antes de executar. Não presumir alcance de outra sub-rede nem testar rede de produção sem autorização.')
cfg('Procedimentos: timeout TCP de sondagem','procedimento-grupo-2',[
    'Timeout: tempo máximo de espera na sondagem TCP.',
    'Faixa aceita: 200 a 3000 ms.',
    'Curto demais: pode omitir serviços lentos ou distantes.',
    'Longo demais: amplia a duração dos candidatos silenciosos.'
], 'Distinguir ausência de resposta de rejeição imediata. Timeout não prova host desligado. Pode haver firewall, ACL, serviço lento ou caminho indisponível. Escolher valor com base no caminho e equipamento, iniciando de forma conservadora e autorizada. No relatório exemplo a sondagem usa 250 ms, o que deve ser considerado ao interpretar 172.27.20.34:502 não confirmado.')
cfg('Procedimentos: concorrência e taxa de sondagens','procedimento-grupo-2',[
    'Concorrência: até 1 a 16 sondagens simultâneas.',
    'Taxa máxima: de 1 a 100 tentativas por segundo.',
    'Concorrência controla simultaneidade. Taxa controla ritmo.',
    'Valores baixos reduzem impacto e aumentam o tempo do teste.'
], 'Os dois limites atuam em conjunto. Não interpretar concorrência 2 como 2 sondagens/s. A duração depende de candidatos, portas, timeouts e confirmações. No relatório a varredura leva 210,2 s usando 5/s e concorrência 2. Evitar aumentar limites apenas para tornar a aula mais rápida em rede industrial real.')
cfg('Procedimentos: sub-rede automática e portas','procedimento-grupo-3',[
    'Automático: identifica a sub-rede da interface selecionada.',
    'Portas: lista TCP separada por vírgulas, com até 16 entradas.',
    '502,1501,1502: testa somente esses serviços candidatos.',
    'Portas não listadas ficam fora da descoberta ativa.'
], 'Cada porta deve estar entre 1 e 65535. O programa não enumera as 65535 portas. A presença na porta 502 não confirma Modbus. Após abertura TCP, faz confirmação de protocolo read-only. Se habilitar descoberta de mapa, ela avalia servidores confirmados. Switches sem anúncio podem continuar desconhecidos. No modo automático não é preciso cadastrar os IPs individualmente.')
cfg('Procedimentos: habilitar rastreamento ICMP','procedimento-grupo-4',[
    'Rastrear rotas: consulta o percurso com TTL crescente.',
    'Cliente: usa os alvos. Servidor: pares Modbus observados.',
    'Um switch L2 não aparece como salto de roteador.',
    'ICMP sem resposta pode coexistir com Modbus funcional.'
], 'Equivalente funcional ao tracert com uma consulta por TTL e sem DNS. O objetivo é apoiar entendimento do caminho IP, não certificar cabeamento. LLDP/CDP são anúncios passivos recebidos pela interface, não consultas SNMP. Não desenhar um switch intermediário como verificado apenas porque um anúncio foi visto.')
cfg('Procedimentos: limites do rastreamento','procedimento-grupo-4',[
    'Máximo de saltos: TTL limite, de 1 a 30.',
    'Timeout por salto: espera ICMP, de 200 a 3000 ms.',
    'Máximo de alvos: de 1 a 32 destinos rastreados.',
    'Limites baixos reduzem duração e podem deixar caminhos parciais.'
], 'Aproximação superior de espera: alvos vezes saltos vezes timeout, antes de considerar encerramento ao atingir destino. 8 alvos, 10 saltos e 200 ms representam até cerca de 16 s de espera se todos silenciosos. Destinos omitidos pelo limite devem constar da cobertura. Tempo ICMP não mede resposta Modbus.')
cfg('Descoberta de mapa: habilitação e papel','descoberta',[
    'Habilitar: inclui a etapa opcional no Teste completo.',
    'Cliente: sonda servidores com funções de leitura.',
    'Servidor: observa ranges solicitados pelos clientes.',
    'OK grava opções. A revisão confirma a execução.'
], 'O teste usa FC01 a FC04, sem escrita automática. No papel servidor, conhecer ranges pedidos por clientes não significa conhecer todo o mapa que o PLC original tinha. No papel cliente, apenas regiões dentro dos limites e UID/FC escolhidos são sondadas. Selecionar habilitação sem executar ainda não produz resultados.', 'screen')
cfg('Descoberta de mapa: endereço e tamanho do bloco','descoberta',[
    'Endereço máximo: limite inclusivo, de 0 a 65535.',
    'Bloco: pontos por leitura, de 1 a 120.',
    'Máximo 100, bloco 10: 11 leituras iniciais por UID/FC.',
    'A última leitura cobre só o restante até o limite.'
], 'Exemplo: 0 a 9, 10 a 19, até 90 a 99 e depois endereço 100. Fórmula sem fallback: teto((máximo+1)/bloco). Para máximo120/bloco10 são13 por combinação. Endereços são offsets, não inferir convenção40001 automaticamente. Região aceita até120 não prova ausência acima120. Blocksize maior pode falhar ao atravessar lacuna, mesmo com pontos válidos.', 'screen')
cfg('Descoberta de mapa: varredura dos Unit IDs','descoberta',[
    'Varrer IDs: testa a faixa de IDs em vez do UID cadastrado.',
    'ID inicial e final: limites inclusivos, de 0 a 255.',
    'Inicial deve ser menor ou igual ao final.',
    'Mais IDs multiplicam requests e timeouts.'
], 'Gateway pode mapear UID a escravos seriais. Se UID conhecido, reduzir varredura evita carga desnecessária. Faixa1a10 representa10IDs, não10equipamentos físicos comprovados. O diálogo valida campos mesmo se varredura desmarcada. O programa ainda depende do comportamento e limites do equipamento para resposta. No PDF só UID1 foi avaliado.', 'screen')
cfg('Descoberta de mapa: funções de leitura','descoberta',[
    'FC01: Coils. FC02: Discrete Inputs.',
    'FC03: Holding Registers. FC04: Input Registers.',
    'Ao menos uma função deve ficar selecionada.',
    'Cada função descreve uma área independente.'
], 'FC01 e FC02 retornam bits. FC03 e FC04 retornam palavras16bits. Um endereço0 em HR é independente de endereço0 em IR. Selecionar4funções quadruplica combinações comparado a uma. Exceção01 pode indicar função não suportada. Exceção02 pode indicar range inválido. Valores0 com resposta válida não representam falha, nem revelam significado de engenharia.', 'screen')
cfg('Descoberta de mapa: fallback ponto a ponto','descoberta',[
    'Fallback: repete pontos individuais após falha do bloco.',
    'Ajuda a localizar regiões menores e lacunas.',
    'Aumenta requests e duração em áreas inválidas.',
    'O mapa mostra leitura aceita, sem nomes ou escalas de engenharia.'
], 'Exemplo: bloco10a19 falha porque somente10a14 existe. Fallback pode identificar pontos aceitos. Timeout também requer cautela, pois ausência de resposta não distingue lacuna de atraso. Máximo120,13blocos,4FC e4endpoints =208requests iniciais antes de fallback. O PDF registra1016requests e206,9s na descoberta. O resultado não valida comandos de escrita nem semanticamente o processo.', 'screen')

rpt('Relatório real: escopo e resultado geral',1,'Diagnostico Modbus TCP',[
    'Papel: Cliente/Mestre. Duração total: 476,9 s.',
    'Resultado Atenção: há sinais a investigar, sem falhas agregadas.',
    'Alvo repetido: 127.0.0.1:1502, UID 1.',
    'Nenhuma escrita automática ocorreu nesta execução.'
], 'Apresentar o PDF real enviado. O resultado global não certifica a rede inteira. Separar testes do alvo local das sondagens da rede. Possibilidades: OK dentro da cobertura, Atenção por sinais, Falha por critério não atendido ou Inconclusivo por evidência insuficiente. A duração soma sondagens e descoberta, não apenas30s de monitoramento.',end='Resumo por servidor',height=160)
rpt('Resumo por servidor: sucesso e abrangência',1,'Resumo por servidor',[
    'Blocos 1/1: um bloco cadastrado recebeu validação.',
    '15/15/15: válidas, tentadas e planejadas coincidem.',
    'Sucesso 100%: leitura do alvo local funcionou na amostra.',
    'Os servidores descobertos não receberam esse mesmo ensaio.'
], 'Comparar planejadas com tentadas em cancelamento. Válidas menores que tentadas exigem verificar tipo de erro, bloco e horário. Blocos não exercitados permanecem fora da conclusão. O PDF lista apenasAlvo1nesta tabela. Adicionar172.27.20.200e.202como alvos e seus mapas para validar repetidamente, se autorizado.',end='Evidencias e achados prioritarios',height=150)
rpt('Achados prioritários: por que houve Atenção',1,'Evidencias e achados prioritarios',[
    'Referência: 6 RST em 10 segundos.',
    'Operacional: 15 RST e 21 segmentos repetidos em 30 s.',
    'Esses indicadores motivaram os avisos TCP.',
    'Causa raiz exige associar sinais à conversa e ao horário.'
], 'RST pode ser encerramento esperado, rejeição ou interrupção. Segmentos repetidos são indícios e podem refletir captura duplicada, reuso de sequência ou retransmissão. Zero-window, se presente, sugere falta de espaço anunciado pelo receptor e exige correlação. Não atribuir tudo aoPLC. Neste relatório as conversas detalhadas com sinais usam443.',end='Desempenho das leituras',height=270)
rpt('Desempenho: mediana, p95 e scan',1,'Desempenho das leituras',[
    'Bloco: FC03, início 0, quantidade 10.',
    'Mediana: 2,9 ms. p95: 4,4 ms.',
    'p95: 0,44% do período configurado de 1000 ms.',
    'Amostra: 15 respostas, insuficiente para eventos raros.'
], 'Mediana descreve o centro e p95 o percentil calculado da amostra. 4,4/1000*100=0,44%. Não equivale a44centésimos de utilização doPLC nem valida o ciclo inteiro com váriosblocos. Latência inclui espera do socket compartilhado. p95 próximo ou acima do período justifica analisar atraso e ciclo, não pressupor processamento doPLC como causa.',height=150)
rpt('Cobertura: o que permaneceu sem validação',2,'Cobertura e limitacoes',[
    'Só um bloco recebeu 15 leituras repetidas.',
    'Descoberta confirmou .200:502 e .202:502.',
    '127.0.0.1 e 172.27.20.21 representam este computador.',
    'Mapa completo, saúde física e escrita segura ficam fora do teste.'
], 'A confirmação de protocolo em endpoints remotos não valida estabilidade do mapa. Mesmo IPsremotos distintos podem ser interfaces do mesmoequipamento. A captura observa uma NIC/BPF, sem remontagemTCPcompleta. Possibilidades de lacuna: bloco desabilitado, porta não incluída, limitemáximo baixo, UID não testado, filtro ou interface insuficientes.',end='Trafego observado nas janelas',height=210)
rpt('Janelas TCP: comparação de taxas',2,'Trafego observado nas janelas',[
    'Referência: 63,1 pkt/s. Operacional: 62,7 pkt/s.',
    'Variação média: -0,7%. Pico final: 296 pkt/s.',
    'Zero-window: 0 nas duas janelas.',
    'Pico e volume isolados não demonstram saturação.'
], 'Não comparar631com1880sem considerar10sversus30s. O pico296está abaixo do limiar5000, mas limiteconfigurado não mede capacidade física. A taxa debytesaumentou e pode refletirHTTPSexterno. Saturação exige capacidade/contadores/latências correlacionados. Janelas ocorrem em instantes diferentes e não sustentam causalidade.',end='Diagrama visual',height=220)
rpt('Topologia: participantes e caminho inferido',3,'Topologia observada e rotas',[
    'Dois participantes remotos com Modbus confirmado.',
    '14 anúncios LLDP/CDP e 67 outros hosts fora do foco.',
    'MK-AUTVIX-BORDA aparece como vizinho anunciado.',
    'O anúncio não comprova o caminho físico dos servidores.'
], 'Há apenas texto de relação lógica nesta seção doPDF, não planta de cabos. Mostrar a distinção entre vizinho anunciado e intermediário verificado. SemconsultaSNMP/inventárioautorizado, switchesL2,portas,caboseVLANsnãoficamvalidados. A quantidade14inclui anúncios/identidades e não deve virar14switches. O estadoOKsignifica critériosdaetapaatendidos.',end='Rastreamento ICMP',height=180)
rpt('Rastreamento ICMP: tempo e TTL',3,'Rastreamento ICMP',[
    'Todos os quatro destinos responderam no TTL 1.',
    '.200 respondeu em 1 ms e .202 em 3 ms.',
    'TTL 1 não significa conexão direta sem switch.',
    'Esse tempo mede ICMP, não a leitura Modbus.'
], 'Quatro destinos incluemendereçoslocais, nãosãoquatrodispositivosfísicosnovos. TTLexpiradoindicarouterrespondente. TimeoutpodeocorrerporACLoufiltragem, comTCPfuncionando. DestinoalcançadocomTTL1mostraqueoensaionãoprecisouavançar, semidentificarcamada2. Compararcomrotaeendereçamento.',height=190)
rpt('Vizinhos anunciados: leitura dos campos',4,'Vizinhos anunciados',[
    'Nome/chassi e MAC ajudam a reconhecer a origem.',
    'Porta anunciada identifica o contexto informado pelo vizinho.',
    'IP de gestão pode estar ausente no anúncio.',
    'TTL é validade do anúncio, não contagem de saltos.'
], 'DiferenciarTTLdosanúnciosdoTTLIP. LLDPnãoéexclusivodeswitches, hosts/PLCtambémanunciam. Orelatório incluiMx80EthernetCPUem.200eMK-AUTVIX-BORDA. Nomeanunciadonãoéquasecertificaçãodaidentidade. MesmaorigempodeaparecerporLLDPeCDPeemcontextosdiferentes. Campoembranconãosignificafalhadoequipamento.',height=210)
rpt('Parâmetros de coleta: reproduzir o ensaio',5,'Parametros de coleta',[
    'Realtek Ethernet e BPF definem o tráfego visível.',
    'Referência 10 s, monitor 30 s, 15 leituras/bloco.',
    'CIDR 172.27.20.0/24. Portas 502,1501,1502.',
    'Limites: 5 sondagens/s e concorrência 2.'
], 'Guardarinterface,filtro,tempo,CIDR,portas,taxa,concorrência,UIDeFCpara repetir. MesmocenáriocomsupressãodeHTTPSouloopbackalteradojátemcoberturadiferente. BPFincluiTCP/UDP/ARP/ICMP/LLDP/CDP. Oalvo127.0.0.1exigeinterfaceloopbackparacapturapassiva, emboraaaplicaçãoTCPleiadiretamente.',end='Checklist da execucao',height=150)
rpt('Checklist: duração e estado por etapa',5,'Checklist da execucao',[
    'Varredura: 210,2 s. Descoberta de mapa: 206,9 s.',
    'Juntas: cerca de 87,5% dos 476,9 s totais.',
    'ARP: Não aplicável ao alvo loopback.',
    'Atenção aparece nas duas janelas TCP e na conclusão.'
], '210,2+206,9=417,1s,87,46%dototal. Identificarondeajustarescopoeconfiguraçãosemperdercobertura. Nãoaplicávelnãoequivaleafalha. Inconclusivosignifica dadosinsuficientes. Conclusãoagrega, nãocriatransaçãoextra. Cancelamentopodeproduziretapaspendentes/interrompidas.',end='Evidencias adicionais',height=340)
rpt('Referência: sinais TCP em conexões HTTPS',6,'Conversas com sinais TCP:',[
    'Os 6 RST envolvem 187.64.130.42:443.',
    'Porta 443 situa essas conversas fora do alvo Modbus testado.',
    'O sinal global não comprova falha em 127.0.0.1:1502.',
    'A análise precisa filtrar endpoints e horários relevantes.'
], 'RecorterealdoPDFmostraduasconversascom3RSTcada. Possibilidades: encerramentoesperado, aplicaçãoexterna,rejeiçãoousessãointerrompida. Orelatórionãoidentificaquemcausounemqualaplicação. Nãoatribuircausaraiz. RepetiroensaioemcapturaadequadaaoalvoecompararcomerrosModbus.',end='12. Monitoramento',height=190)
rpt('Monitoramento: volume e qualidade da captura',6,'12. Monitoramento TCP operacional',[
    '1880 pacotes em 30 s, pico de 296 pkt/s.',
    'Driver e fila reportam zero descartes nessa janela.',
    'SO reporta zero erros e descartes de interface.',
    'Nenhum desses contadores valida todas as portas do switch.'
], 'DistinguirdescartenafilaUI, nodriverenoSO. Mesmozeronãocomprovaausênciadeperdaporfiltros,visibilidadeoucaminhosnãoobservados. OPDFinforma detalhesdelimiteatingido, logoanálisederepetiçãoparcial. Compararcomcontadoresdoswitchautorizadamente. Média27,62KB/snãorevelasaturaçãofísica.',height=180)
rpt('Monitoramento: onde estão os segmentos repetidos',6,'Conversas com sinais TCP: 172.27.20.21:64592',[
    'As conversas detalhadas com sinais usam porta 443.',
    '21 segmentos repetidos são indícios na captura.',
    'Pode haver retransmissão, duplicação ou reuso de conexão.',
    'Correlacionar com atraso ou falha do serviço investigado.'
], 'Odetectorusa sequência/comprimento,nãoremontagemcompleta. Nãoapresentar21como21perdasfísicas. Hálimitesdesequências/conversas, análisepodeficarparcial. Paralocalizarcausa: capturaadequada, conexãoespecífica,TIDModbus, temporequisição/resposta,ACKs/seqecontraswitch. NãoexistecausaraizconfirmadanestePDF.',height=175)
rpt('Inventário de serviços: TCP aberto versus Modbus',7,'Dispositivos com evidencia de servico',[
    '.21:1502, .200:502 e .202:502: Modbus confirmado.',
    '.34:502: TCP aberto, protocolo não confirmado.',
    'Porta aberta demonstra aceitação de conexão TCP.',
    'FC, UID e resposta válida sustentam confirmação Modbus.'
], 'Host.34poderiaserserviçodeoutranatureza,UIDinadequado,dispositivocomrespostalenta,ACLaplicacionalougateway. Nenhuma hipóteseestáconfirmada. Nãochamá-lodePLCdessatabela. Observadopassivamentedifereconfirmadoativamente. Leranotasdetalhadasevalidarconformedocumentação.',end='Faixas de mapa descobertas',height=190)
rpt('Mapa descoberto: intervalos e amostras',7,'Faixas de mapa descobertas',[
    'Servidor local: quatro áreas de 0 a 19.',
    'Servidor .200: áreas aceitas desde 0 até o limite 120.',
    'Range validado: a leitura retornou resposta válida.',
    'Amostra zero pode ser dado válido, sem falha de comunicação.'
], 'As16faixasnaexecuçãoincluem4áreaspor4endpoints, comdoisendereçosdomesmoservidorlocal. Nãosão16dispositivos. .200e.202retornaramzerosnasamostras, seminformarnome,escala,unidadeouvalorfuncional. Conferirmapadeengenharia. Leituraaceita0a120nãoimplicalimitemáximorealdoequipamento.',height=220)
rpt('Descoberta Modbus: confirmação e timeout',12,'3 grupos por IP',[
    'Quatro endpoints confirmados, consolidados em três grupos.',
    'TID/UID/FC/MBAP coerentes sustentam a validação.',
    '.34:502 respondeu TCP, mas FC03/UID 1 deu timeout.',
    'Outros IDs, funções ou timeouts exigem ensaio autorizado.'
], '3gruposconsideramlocal+doisIPsremotos. ARPmostramesmoMACem.200e.202,logoatédoisIPsremotosnãoprovamdoisequipamentosfísicos. Consultarinventárioantesdenomear. Timeout250mspodeomitirrespostalenta. NãoaumentarvarreduradeUIDsemcontroledecarga.',end='Etapa 7:',height=180)
rpt('Descoberta de mapa: por que foram 1016 requests',12,'Etapa 7: Descoberta de mapa',[
    'Quatro endpoints, UID 1, quatro funções de leitura.',
    'Máximo 120, bloco 10: 13 blocos iniciais por combinação.',
    '208 requests iniciais, antes das tentativas adicionais.',
    'Fallback habilitado ampliou a sondagem para 1016 requests.'
], '13*4*4=208. Ototalreal1016incluifallback/releiturasconformedetector, nãoinferirque808sãoretransmissõesTCP. Aetapaleva206,9s. Pontoapontotentaáreasqueobloco nãoaceitou. Lerrestriçõesdorelatorio. Máximo120incluiendereço120, sendo121endereços. Asfaixasvalidaslocais0a19fazemrestantesfalharemcomfallback.',end='Interpretacao:',height=200)
rpt('Conectividade TCP: socket aberto não valida o mapa',13,'Etapa 9: Conectividade TCP',[
    'Socket abriu em 2 ms para 127.0.0.1:1502.',
    'O resultado confirma o transporte daquela tentativa.',
    'Ainda falta validar UID, função, range e resposta.',
    'Recusa e timeout pedem investigações diferentes.'
], 'Recusaimediatageralmenteapontalistenerausente/rejeição, masconferirserviçoeporta. Timeoutpodeserfiltragem,caminhoouserviço. Socketaberto+ModbusfalhandoorientaUnitID,FC,mapa,gatewayouprotocolo. Os2msnãosãotempoPLC. NestealvolocalnãocomprovaconectividadeatéumPLCreamoto.',end='Etapa 10:',height=170)
rpt('Leituras repetidas: sucesso e tipos de falha',14,'Bloco 1:',[
    '15/15 válidas, sem erros no bloco FC03 0 a 9.',
    'Mín. 2,7 ms, mediana 2,9 ms, máx. 4,4 ms.',
    'Timeout, exceção e falha TCP exigem diagnósticos separados.',
    'Só os blocos habilitados entram nessa validação.'
], 'Exceção01:funçãonãosuportada.02:endereço/rangeinválido.03:valor/parâmetroinválido.04:falhadoservidornaoperação. Códigosdependemdodestino, consultar documentação. Timeoutnão provaescriturasnemperdafísica. TID/UID/FCincoerentes sugeremrespostainesperada/conformidade. Essaspossibilidadesnãosemanifestaramnoexemplo. Nãoutilizar100%paraestabilidadefora15leituras.',end='Etapa 11:',height=150)
rpt('Envio e recebimento: validade da transação',14,'Etapa 11: Envio e recebimento',[
    'Request/response FC03 completo e válido.',
    'Valores de 1000 a 1009 no range solicitado.',
    'Confirmação cobre aquela linha e aquele UID.',
    'Valores plausíveis ainda precisam de contexto de engenharia.'
], 'Comunicaçãofuncionalnãoimplicasemântica correta. Palavras podemrepresentar inteiroassinado,float,escalaoubits. Endianness,offseteunidadepodemgerarvaloresinesperadosmesmosem errodeprotocolo. Estetesteéread-only. Nolocal,valoressequenciaissãodadossimulados,não mediçõesdoPLC.',end='Etapa 12:',height=180)
rpt('Falhas observadas e conclusão: escopos diferentes',15,'Etapa 13: Falhas observadas',[
    'Falhas observadas: zero avisos das sessões testadas.',
    'Conclusão: 0 falhas, 2 atenções e 1 não aplicável.',
    'Sinais TCP gerais podem coexistir com leituras locais OK.',
    'Atenção global não equivale a falha do alvo.'
], 'Etapa13filtraeventosdesdoiníciodotesteesessõestestadas. Registroglobalprévioeoutrosalvosnãoreprovamessasessão. Etapa14agregaasetapas, incluindoatençõescaptura, logoosresultadosnãosecontradizem. Zeroeventossignificaos sintomasmonitoradosnãoforamregistradosnajaneladaquelasessão.',end='Etapa 14:',height=210)
rpt('Inventário completo: origem e identidade dos IPs',11,'172.27.20.200',[
    'Fonte: ARP, sondagem e captura indicam como o IP apareceu.',
    '.200 e .202 compartilham o MAC 00-04-17-91-c8-60.',
    'Isso exige conferir a identidade física no inventário.',
    'IPs e entradas ARP não são contagem segura de PLCs.'
], 'MesmoMACpodeserinterfaces/aliasdamesmaCPU,gateway,proxyARPouconfiguraçãoparticular. Nãoescolherhipótesesemvalidar. OLLDPde.200nomeiaMx80EthernetCPUeajudacorrelação. Inventáriopáginas15a25tambémincluiInternet,multicasteendereçosespeciais, quediferemdedispositivosindustriais. MostrarcolunasIdentidade,MAC,Fonte,Papel,eNotas.',end='172.27.20.255',height=40,x1=500)
rpt('Conversas completas: tráfego relevante ao diagnóstico',26,None,[
    'Origem/destino e detalhe localizam o par IP:porta.',
    'Evidência informa TCP, UDP, ARP ou Modbus reconhecido.',
    'Contagem indica volume observado daquela relação.',
    'HTTPS e descoberta de nomes coexistem com o ensaio industrial.'
], 'Páginas26a33sãotabelaspara auditoria, não resumoexecutivo. Os490pacotes de.21para104.18.32.47:443sãodotráfegovisível, não490leiturasModbus. Osprotocolos5353/137/1900podemsertráfegodedescobertanormal. Paraindicarofensorénecessáriocorrelacionarvolumecomcapacidade,contadoreseimpacto. Volumemaiorisoladonãoindicasobrecarga.',height=230)
rpt('Mapa completo: confiança e notas para auditoria',34,None,[
    'Endpoint/UID/FC identifica a área sondada.',
    'Início e fim definem intervalo inclusivo aceito.',
    'Modo ativo/client indica leitura, sem escrita.',
    'Confiança e notas distinguem resposta válida de significado.'
], 'Tabela finalcom16faixasrepeteoresumocomnotascompletas. Nãodescobretagnames,escala,endianidade,permissaodeescritaoucomandosseguros. UmaalteraçãodoUID/FCéreensaiocomcoberturadiferente. Serverobservadopassivamenteproduzconfiançadiferentedafaixalidamenteativa. GuardarPDFjunto àconfiguraçãoFFDparareproduzir.',height=240)
rpt('Diagnóstico deste exemplo: síntese e próximos ensaios',2,'Cobertura e limitacoes',[
    'O bloco local passou. Os alertas TCP não comprovam falha Modbus.',
    'A NIC Ethernet não captura o alvo loopback diretamente.',
    'Incluir .200 e .202 como alvos permite validar seus blocos.',
    'Investigar .34 e a identidade dos MACs conforme autorização.'
], 'Conclusãoformativa: leitura local15/15, p954,4ms e sinais443semcausaraizModbusconfirmada. FramesModbuszeronasjanelasnãoanulamasrespostasativas, nemcontradizemModbusobservadoduranteoutrasetapas. Novosensaios: selecionarcapturaloopbackparalocaloualvorealnaNICindustrial, validarmapaseUIDdosremotos,aumentaramostraspara intermitência. NãoháevidênciadesaturaçãofísicaounecessidadedemudarPLC.',end='Trafego observado nas janelas',height=190)

config_notes = {
    10: 'As portas precisam estar entre 1 e 65535. A aplicação verifica somente as portas listadas. A confirmação Modbus exige resposta coerente, não apenas o handshake TCP. Demonstrar a diferença entre 502 e uma porta alternativa como 1501.',
    11: 'A consulta usa uma requisição por TTL e não consulta DNS. Switches de camada 2 não reduzem TTL. Os anúncios LLDP/CDP observados não comprovam que o equipamento anunciado participa do caminho até cada servidor. A aplicação não consulta SNMP.',
    12: 'Limite superior aproximado de espera: quantidade de alvos vezes saltos vezes timeout. Com 8 alvos, 10 saltos e 200 ms, pode chegar a cerca de 16 s se todos ficarem silenciosos. O rastreamento termina antes quando o destino responde. Destinos omitidos reduzem a cobertura.',
    13: 'A descoberta usa FC01 a FC04, sem escrita automática. No papel servidor, os ranges observados correspondem às solicitações dos clientes, sem revelar necessariamente todo o mapa do PLC original. No cliente, a cobertura depende dos limites, funções e IDs escolhidos.',
    14: 'Máximo 100 e bloco 10 produz os ranges 0 a 9, 10 a 19, até 90 a 99 e depois somente o endereço 100. A quantidade inicial por combinação é teto((máximo + 1) / bloco). Máximo 120 produz 13 leituras. Uma leitura atravessando lacuna pode falhar mesmo que parte dos pontos exista.',
    15: 'Um gateway pode usar Unit ID para selecionar um escravo serial. Se o ID já for conhecido, restringir a varredura reduz carga. A faixa 1 a 10 representa dez IDs testados e não dez dispositivos físicos confirmados. Neste relatório, somente UID 1 participou da descoberta de mapa.',
    16: 'FC01 e FC02 retornam bits. FC03 e FC04 retornam palavras de 16 bits. Endereço 0 de HR é independente do endereço 0 de IR. Selecionar quatro funções multiplica as combinações. Exceção 01 pode indicar função não suportada e 02 pode indicar range inválido. Valores zero podem ser dados válidos.',
    17: 'Exemplo: a leitura 10 a 19 falha porque somente 10 a 14 existe. O fallback individual pode revelar os pontos aceitos. Esse recurso aumenta carga e duração, especialmente quando a maior parte da faixa não existe. Neste PDF, 208 requisições iniciais antes do fallback resultaram em 1016 requests totais. A descoberta não identifica nomes, escalas ou comandos seguros.'
}
for index, text in config_notes.items():
    config[index-1]['notes'] = '\n\n'.join(config[index-1]['body']) + '\n\n' + text

report_notes = [
    'O resultado pertence aos alvos, blocos e janelas desta execução. Atenção significa investigação pendente e não uma causa raiz confirmada. Separar ensaio local de descoberta na rede. A duração total inclui a varredura e o fallback.',
    'A tabela inclui somente Alvo 1. Em cancelamento, comparar planejadas, tentadas e válidas. Para testar repetidamente os servidores remotos encontrados, cadastrar seus mapas e incluí-los como alvos, conforme autorização.',
    'RST pode corresponder a encerramento esperado, rejeição ou interrupção. Segmentos repetidos são indícios e podem refletir retransmissão, duplicação de captura ou reuso de conexão. Zero-window, quando presente, indica falta de espaço anunciado pelo receptor. Neste PDF, os detalhes dos sinais apontam para conexões na porta 443.',
    'A razão 4,4 / 1000 equivale a 0,44%. Essa comparação por transação não mede utilização do PLC nem valida o ciclo inteiro. A espera pelo socket participa da latência. Quinze respostas não caracterizam falhas raras ou a cauda de latência. p95 elevado exige analisar espera, caminho e carga antes de atribuir processamento lento ao PLC.',
    'Confirmar protocolo em uma sondagem não valida a estabilidade de todos os blocos. IPs locais diferentes podem representar o mesmo serviço. Mesmo IPs remotos distintos não garantem equipamentos físicos distintos. Limites de UID, função, endereço, interface e BPF reduzem o alcance da conclusão.',
    'Comparar taxas por segundo em vez de totais de janelas com durações diferentes. O pico 296 está abaixo do limiar 5000 configurado, mas esse limiar não mede a capacidade física. O aumento em bytes pode refletir tráfego HTTPS externo. A comparação não estabelece causalidade entre sondagem e carga.',
    'Esta seção apresenta uma relação lógica em texto. O vizinho anunciado não é um intermediário fisicamente verificado. Anúncios podem vir de hosts, PLCs ou equipamentos de rede. Sem inventário ou consulta autorizada, cabeamento, VLANs e portas permanecem desconhecidos.',
    'Os quatro destinos incluem endereços do computador local. TTL 1 não revela switches transparentes de camada 2. Timeout ICMP pode ocorrer por ACL ou filtragem mesmo com TCP funcional. Um roteador que responde TTL expirado oferece uma evidência de salto, sem validar a saúde do enlace.',
    'TTL do anúncio significa validade em segundos e difere do TTL IP. LLDP não é exclusivo de switches. O PDF inclui Mx80 Ethernet: CPU e MK-AUTVIX-BORDA. A mesma origem pode aparecer por LLDP e CDP ou em contextos de porta diferentes. Campo ausente significa informação não disponível naquele anúncio.',
    'Reproduzir um ensaio exige guardar interface, BPF, janelas, CIDR, portas, limites e configuração dos alvos. O destino 127.0.0.1 precisa de interface loopback para captura passiva. A leitura direta da aplicação continua possível pela API de socket, independentemente da NIC capturada.',
    'A soma 210,2 + 206,9 representa 417,1 s, aproximadamente 87,5% do total. Reduzir o CIDR ou o limite de mapa pode reduzir duração e também cobertura. ARP não se aplica ao loopback. Conclusão é uma agregação, sem nova transação. Cancelamento pode deixar etapas pendentes ou interrompidas.',
    'O recorte mostra duas conversas HTTPS com três RST cada. O PDF não comprova qual aplicação ou condição os gerou. Esses sinais não pertencem ao endpoint local Modbus repetidamente testado. Correlacionar horários e respostas do serviço investigado antes de afirmar impacto industrial.',
    'Separar fila da UI, descarte do driver e contadores do sistema operacional. Zero nesses contadores não valida perdas em portas de switches ou caminhos não visíveis. A análise de segmentos repetidos pode ser parcial quando os limites de detalhes são atingidos. Confrontar com contadores de infraestrutura se autorizado.',
    'O detector usa sequência e comprimento, sem remontagem completa de stream TCP. Não converter 21 segmentos repetidos em 21 perdas físicas. Para confirmar impacto, analisar a conexão relevante e os tempos de request/response, além de ACKs e contadores. A causa raiz permanece não confirmada.',
    'Para .34, considerar serviço diferente, UID inadequado, resposta lenta, ACL de aplicação ou gateway. Essas hipóteses não foram confirmadas. A porta 502, isoladamente, não autoriza chamar o host de PLC. Consultar documentação e repetir apenas as sondagens autorizadas.',
    'As 16 faixas incluem quatro funções por quatro endpoints, com dois endereços para o mesmo servidor local. Não representam 16 dispositivos. Zeros podem ser respostas válidas sem revelar significado funcional. Comparar com o mapa de engenharia e não inferir ausência acima do máximo sondado.',
    'Os três grupos consolidados incluem o serviço local e dois IPs remotos. A tabela ARP mostra o mesmo MAC para .200 e .202, portanto sua identidade física exige confirmação. O timeout de .34 não distingue falta de suporte de atraso ou filtragem. Ampliar UID ou timeout somente conforme documentação e autorização.',
    'Treze blocos vezes quatro funções vezes quatro endpoints geram 208 requests iniciais. O total 1016 inclui requisições adicionais do procedimento, sem indicar 808 retransmissões TCP. Máximo 120 cobre 121 endereços. O fallback nas áreas inexistentes do servidor local ajuda a explicar o custo do procedimento.',
    'Recusa imediata e timeout são sintomas diferentes. Conferir listener, porta e firewall antes de analisar mapa. Se TCP abre e Modbus falha, investigar UID, FC, range, gateway e conformidade. A abertura local em 2 ms não valida o caminho até um PLC remoto.',
    'Possibilidades que não ocorreram neste exemplo: exceção 01 por função não suportada, 02 por range inválido, 03 por parâmetro inválido e 04 por falha ao executar a operação. Confirmar a interpretação com a documentação do destino. Timeout ou falha TCP não equivalem a exceção Modbus. Resposta com TID/UID/FC incoerentes exige analisar a conformidade da transação.',
    'Resposta válida comprova a transação, sem garantir interpretação de engenharia. Valores podem exigir escala, sinal, ordem de palavras ou conversão para float. Offset incorreto pode retornar dados plausíveis do ponto errado. O exemplo local usa valores simulados de 1000 a 1009 e não medições reais do processo.',
    'Falhas observadas usa eventos das sessões testadas desde o início da execução. Conclusão agrega também as atenções das janelas gerais de captura. Por isso zero avisos do alvo e Atenção global podem coexistir. A ausência de eventos só se refere aos sintomas e ao período monitorados.',
    'Mesmo MAC em dois IPs pode envolver alias de uma CPU, gateway, proxy ARP ou configuração particular. O relatório não resolve essa identidade física. Usar inventário e anúncios para correlação. A lista completa contém também Internet, multicast e endereços especiais, sem equivaler a uma relação de PLCs.',
    'As páginas 26 a 33 preservam conversas para auditoria. Contagem de quadros HTTPS não é quantidade de leituras Modbus. Tráfego nas portas 5353, 137 ou 1900 pode corresponder a descoberta normal. Volume alto precisa de capacidade e impacto correlacionados antes de caracterizar sobrecarga.',
    'A tabela final preserva UID, função, intervalo, modo, confiança e notas. Não identifica nomes de tags, escala, endianidade, permissão de escrita ou comandos seguros. Uma faixa observada passivamente tem confiança diferente de uma leitura ativa válida. Arquivar o PDF e a configuração FFD para repetir o cenário.',
    'Síntese: o bloco local passou nas 15 leituras e os sinais HTTPS não confirmam falha Modbus. Zero frames Modbus nas janelas Ethernet não invalida respostas obtidas pela aplicação em loopback. Próximos ensaios autorizados: captura apropriada, mapas dos remotos como alvos, mais amostras para intermitência e verificação de .34. O PDF não demonstra saturação física.'
]
assert len(report_notes) == len(report)
for item, text in zip(report, report_notes):
    item['notes'] = item['caption'] + '\n\n' + '\n\n'.join(item['body']) + '\n\n' + text

rpt('Contexto: operações ativas e escolha da interface',9,'Servidor local:',[
    'Servidor local já escutava em 0.0.0.0:1502.',
    'A Realtek venceu a seleção por maior tráfego em 2 s.',
    'Maior tráfego não garante visibilidade do alvo loopback.',
    'O escopo identifica serviços e carga já existentes.'
], 'Esta seção permite identificar o ambiente antes das novas sondagens. O software comparou interfaces, mas a NIC com maior tráfego era Ethernet e o alvo validado era 127.0.0.1. Essa diferença explica por que respostas ativas locais e ausência de frames passivos Modbus podem coexistir nas janelas. Escolher interface adequada ou um alvo LAN para ensaio real.',end='Seguranca:',height=220)
rpt('ARP e interfaces: vizinho conhecido versus serviço',11,'Interface: 172.27.20.21',[
    'ARP relaciona IP e MAC no segmento local.',
    'Entrada dinâmica registra resolução, sem confirmar Modbus.',
    'Loopback não utiliza ARP e recebe Não aplicável.',
    'Rotas e interface precisam corresponder à VLAN investigada.'
], 'As evidências de interfaces e rotas estão nas etapas 1 e 2. Verificar IP, máscara, gateway e interface usada para o destino. ARP ausente pode decorrer de falta de comunicação recente ou de um destino roteado, sem significar host inexistente. ARP presente não informa porta, função, mapa ou saúde atual. Roteamento incorreto exige confrontar a tabela de rotas com o inventário.',height=150,x1=500)
rpt('Varredura de hosts: candidatos e limites de alcance',11,'Etapa 5: Varredura de hosts',[
    '34/255 candidatos responderam no CIDR avaliado.',
    'Cinco endpoints tinham portas TCP abertas.',
    'O procedimento levou 210,2 segundos.',
    'Silêncio não distingue host ausente de filtragem.'
], 'A contagem de candidatos é a registrada pelo software e não uma contagem de PLCs. Somente portas 502, 1501 e 1502 participaram. Um host pode responder ICMP sem abrir essas portas, ou abrir TCP sem responder ICMP. Confirmar protocolo na etapa seguinte. O CIDR, timeout e limites de ritmo explicam duração e cobertura.',end='Etapa 6:',height=130)
rpt('Conclusão: agregação e validade do resultado',15,'Etapa 14: Conclusao',[
    '0 falhas e 2 atenções motivam a conclusão Atenção.',
    'Uma etapa Não aplicável não representa defeito.',
    'A conclusão vale para alvos, blocos e janelas exercitados.',
    'Nova execução com maior cobertura pode mudar o diagnóstico.'
], 'A conclusão não executa outra requisição. Ela agrega o que as etapas coletaram. Falha de um alvo não elimina resultados independentes de outros alvos. Inconclusivo, caso presente, aponta instrumentação ou dados insuficientes. Neste caso, ampliar o ensaio dos remotos e adequar a interface oferece mais informação do que tentar corrigir os sinais HTTPS como se fossem falha do PLC.',end='Inventario de IPs',height=210)

# Keep the final case synthesis after the supporting examples.
report.append(report.pop(25))

# Preserve the approved sequence, replacing only the compact settings section.
expanded = original[:26] + config + original[34:46] + report + original[46:]
(ROOT / 'slides-ampliada.json').write_text(json.dumps(expanded, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'SLIDES: {len(expanded)} CONFIG: {len(config)} REPORT: {len(report)}')
print('REPORT_SLIDES_START:', 26 + len(config) + 12 + 1)
