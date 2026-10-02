# Ata preparatória e roteiro de aula: troubleshooting Modbus TCP

> **Treinamento previsto para 03/10/2026.** Este documento reúne o conteúdo que será ensinado e os testes propostos. Preencher dúvidas, resultados, decisões e participantes durante a sessão. Depois do treinamento, cada momento pode virar uma seção da apresentação.

## Identificação

| Campo | Registro |
|---|---|
| Data | 03/10/2026 |
| Horário e duração | A preencher |
| Local / formato | A preencher |
| Instrutor(es) | A preencher |
| Participantes | A preencher |
| Ambiente de demonstração | Preferencialmente laboratório ou simulação; registrar o utilizado |
| Versão do software | A preencher no início |
| Objetivo geral | Ensinar um método de diagnóstico de Modbus TCP com evidências de rede, protocolo e mapa de dados, usando o Modbus TCP Troubleshooter. |

## Preparação antes da turma chegar

1. Separar as perguntas, anotações e pontos de dificuldade do primeiro treinamento Modbus. Levar pelo menos um exemplo real anonimizado, se houver.
2. Preparar uma bancada local: servidor simulado em `127.0.0.1:1502`, Unit ID `1`, faixa de Holding Registers `0–9` aplicada; cliente com alvo `127.0.0.1:1502`, Unit ID `1`, bloco FC03 com início `0` e quantidade `10`. O manual integrado descreve esse ensaio. Iniciar o servidor antes da leitura.
3. Salvar uma configuração `.ffd` dessa bancada para reabrir rapidamente após qualquer demonstração de erro de configuração.
4. Verificar se a captura Npcap/loopback está disponível. Se não estiver, demonstrar a timeline Modbus da própria aplicação e explicar por que uma captura vazia não invalida uma leitura que recebeu resposta.
5. Preparar uma segunda configuração com **um** erro intencional por vez: porta errada, Unit ID divergente ou endereço fora do mapa. Registrar o valor correto para restaurar ao final.
6. Se houver uso de rede industrial real, registrar autorização, IPs, portas, faixa CIDR, janela de execução, limite de sondagem e responsável pelo processo antes de conectar a bancada. Não usar escrita no PLC durante a aula.

## Momento 1 — Revisão e dúvidas do primeiro treinamento Modbus

**Objetivo:** identificar o que a turma já compreende e corrigir dúvidas antes de apresentar a ferramenta.

**O que ensinar e discutir**

- Relembrar em linguagem operacional: quem faz a pergunta (cliente/mestre), quem responde (servidor/escravo), o que é uma função Modbus e por que IP, porta, Unit ID, função e endereço são parâmetros diferentes.
- Pedir que os participantes tragam uma dúvida ou exemplo do primeiro treinamento. Agrupar no quadro por **conexão**, **mapa/endereço**, **tempo de resposta**, **dados incorretos** e **escrita**. Responder com um pequeno experimento quando a dúvida puder ser reproduzida.
- Explicar que uma falha de comunicação precisa ser localizada por camada: caminho IP, serviço TCP, protocolo Modbus ou dado da aplicação. Uma mesma mensagem de “sem leitura” pode ter causas distintas.

**Perguntas de abertura para a turma**

1. Se o equipamento responde ao ping, é certo que a porta Modbus está acessível? **Esperado:** não; ICMP e TCP são verificações diferentes.
2. Se a porta TCP abre, isso confirma o mapa de registradores? **Esperado:** não; ainda faltam resposta Modbus, Unit ID, função e região.
3. Se apareceu exceção Modbus, houve comunicação? **Esperado:** sim, houve resposta de protocolo; a função/endereço/valor solicitado pode ter sido recusado.

**Demonstração curta:** mostrar três evidências diferentes na bancada ou em capturas preparadas: conexão TCP, leitura FC03 válida e exceção Modbus para região inválida. Pedir que a turma classifique cada uma antes de explicar.

**Registrar na ata:** dúvidas herdadas do primeiro treinamento; respostas dadas; questões que exigem validação posterior.

**Base para slides:** “O que já sabemos?”, “O que ainda está confuso?” e o diagrama `Cliente -> TCP -> Servidor -> Mapa -> Resposta`.

## Momento 2 — Como uma transação Modbus TCP funciona

**Objetivo:** dar um modelo mental para ler a timeline e entender o motivo de cada configuração.

**O que ensinar**

- **Endpoint** é o serviço `IP:porta`. A porta `502` é convencional, mas a bancada usa `1502`; portar o número correto importa mais do que assumir o padrão.
- O **cliente** envia a requisição. O **servidor** interpreta função, endereço e quantidade e devolve dados ou uma exceção. Um PLC pode operar em diferentes papéis conforme a arquitetura.
- No cabeçalho MBAP, o **Transaction ID (TID)** ajuda a associar requisição e resposta na conexão. O **Unit ID (UID)** identifica um destino lógico; em gateways ele pode selecionar um equipamento serial. A **Function Code (FC)** indica a operação. Não usar o UID como substituto do IP/porta.
- Diferenciar “pacote TCP chegou”, “ADU Modbus reconhecida”, “leitura bem-sucedida” e “valor de engenharia faz sentido”. São verificações progressivas.
- Apresentar os códigos usados na aula: FC01 lê coils, FC02 lê entradas discretas, FC03 lê holding registers e FC04 lê input registers. FC05/06/15/16 são escritas; citar para reconhecimento de tráfego, sem demonstrar escrita em processo real.

**Teste guiado:** executar **Leitura única** no alvo de laboratório e localizar na timeline Modbus direção, endpoint, TID, UID, FC, endereço, quantidade e resposta. Relacionar as duas linhas da transação.

**Resultado esperado:** FC03 para endereço `0`, quantidade `10`, no endpoint e UID configurados, com resposta coerente. Se uma leitura não aparecer na captura TCP, conferir interface/filtro antes de concluir que a requisição não ocorreu.

**Pergunta de checagem:** “Se alterarmos somente a porta do cliente, o Unit ID continua correto; por que a leitura pode falhar antes de chegar ao Modbus?” Resposta esperada: o socket não alcança o serviço esperado.

**Registrar na ata:** termos que causaram confusão, exemplos adotados e dúvidas sobre TID/UID.

**Base para slides:** um frame ilustrativo `MBAP [TID | Protocol ID | Length | UID] + PDU [FC | Endereço | Quantidade]` e um par request/response da ferramenta.

## Momento 3 — Montagem da bancada, conexão e papéis do software

**Objetivo:** operar o software como cliente e servidor e entender a configuração de cada estação.

**O que ensinar**

- No **Servidor/Escravo**, definir IP de escuta, porta e Unit ID. O IP de escuta deve pertencer à estação ou ser `0.0.0.0` para todas as interfaces locais; `0.0.0.0` não é endereço de destino de um cliente.
- No **Cliente/Mestre**, cada alvo tem IP, porta, Unit ID, nome e mapa próprios. É possível trabalhar com múltiplos alvos e manter o servidor simulado ativo ao mesmo tempo.
- Comparar as opções de conexão do cliente: TCP persistente (conexão mantida entre requisições) e conexão por requisição. Explicar o efeito esperado sobre handshakes, sessões e diagnóstico de equipamentos que encerram sockets ociosos.
- Mostrar a diferença entre **conectar/iniciar leitura cíclica**, **leitura única**, **desconectar cliente** e **parar servidor**. Conferir status real da sessão, em vez de inferir o estado pela aba atualmente selecionada.
- Salvar e reabrir a configuração `.ffd` para demonstrar que endpoint, alvos e mapa podem ser reaproveitados sem refazer a bancada.

**Teste A — caminho normal:** iniciar o servidor local, configurar o alvo cliente de loopback, fazer uma leitura única e iniciar alguns ciclos de leitura. Conferir no mapa do cliente se valor, qualidade e horário mudam.

**Teste B — porta incorreta:** alterar apenas a porta do alvo para uma porta sem serviço e repetir a leitura. Voltar à porta correta após observar a falha. Explicar a diferença entre “conexão recusada/timeout TCP” e uma exceção Modbus.

**Resultado esperado:** o Teste A produz respostas. No Teste B, não há resposta Modbus válida porque a conexão ao serviço não se completa.

**Registrar na ata:** parâmetros da bancada, estado do servidor/cliente, mensagens do teste de porta errada e como a turma interpretou o resultado.

**Base para slides:** tela de configuração de cada papel, árvore lateral de alvos e uma tabela “qual botão muda qual estado?”.

## Momento 4 — Mapa, áreas de dados e endereçamento

**Objetivo:** ensinar a distinguir erro de conexão de erro de mapa.

**O que ensinar**

- Um ponto Modbus é identificado por **área/função + offset + quantidade + UID + endpoint**. Endereço `0` em HR não é o mesmo dado que endereço `0` em IR ou COIL.
- O software recebe **offset base zero** nos campos de endereço. Em uma documentação que use `40001` para o primeiro HR, o offset pode ser `0`; confirmar a convenção do fabricante antes de converter.
- Intervalo solicitado inclui todos os endereços de `início` até `início + quantidade - 1`. Uma leitura pode começar dentro do mapa e terminar fora dele.
- Qualidade e horário são parte do resultado. Um número retido na tela após parar a leitura não significa que a comunicação ainda está ativa.
- Descoberta de mapa encontra regiões que responderam às leituras feitas ou foram requisitadas na janela observada. Ela não devolve automaticamente nome da tag, unidade física, escala ou autorização de escrita.

**Teste A — faixa válida:** manter HR `0–9` no servidor e ler FC03, início `0`, quantidade `10`. Conferir valores e timestamp no Mapa de comunicação.

**Teste B — atravessar limite:** solicitar FC03, início `9`, quantidade `2` contra a faixa HR `0–9`. O segundo endereço é `10`; se o servidor responder com exceção `02`, mostrar que a comunicação ocorreu e o **range solicitado** foi recusado. Restaurar o bloco válido.

**Teste C — área diferente:** tentar FC04 em uma região IR não configurada na bancada. Comparar a resposta com FC03. Se a configuração de IR tiver sido alterada, confirmar o mapa efetivo antes da demonstração.

**Perguntas de checagem:** “40001 vai no campo endereço como 40001?” e “Se recebi exceção 02, ainda devo investigar cabo primeiro?”

**Registrar na ata:** função, offset, quantidade, exceção/resultado e convenção de endereçamento adotada no exemplo.

**Base para slides:** matriz das quatro áreas; linha numérica `0...9` com requisição `9 + 2` ultrapassando o limite; exemplo 40001 -> offset 0 condicionado ao manual do fabricante.

## Momento 5 — Captura, timeline Modbus e timeline TCP

**Objetivo:** diferenciar eventos que o software gerou ou recebeu dos quadros visíveis na interface de rede.

**O que ensinar**

- A timeline **Modbus** ajuda a correlacionar requisições, respostas, exceções e tempos da aplicação. A timeline **TCP** depende de captura passiva na interface escolhida e do filtro aplicado.
- Antes de clicar em **Iniciar captura**, identificar a interface que carrega o tráfego de interesse. Loopback exige interface adequada; tráfego entre dois terceiros em uma rede comutada pode exigir porta espelhada/SPAN ou TAP.
- Explicar o filtro de captura (BPF): excluir protocolo, IP ou porta pode produzir uma tabela vazia mesmo com comunicação funcional. Revisar filtro antes de declarar ausência de pacotes.
- Ler origem, destino, protocolo, comprimento, direção e horário. Um `RST`, repetição de sequência ou timeout é um sinal para correlacionar, não a causa raiz automaticamente.
- Distinguir horário de uma requisição de **período de polling**: requisições a blocos diferentes podem sair a poucos milissegundos, embora o ciclo de cada bloco seja de 1 segundo.

**Teste A — captura observável:** com a interface e o filtro adequados, iniciar captura, executar leituras por aproximadamente 30 segundos e comparar os eventos nas abas Modbus e TCP.

**Teste B — diagnóstico de captura vazia:** se a tabela TCP ficar vazia, verificar interface, Npcap, filtro, tráfego visível e fila/descarte. Confirmar em paralelo se a leitura Modbus da aplicação ainda respondeu; documentar a limitação da captura.

**Resultado esperado:** quando a interface vê o tráfego, há quadros com endpoints coerentes com o alvo. Ausência de quadros na captura, isoladamente, não invalida uma leitura com resposta registrada pela aplicação.

**Registrar na ata:** interface, filtro, duração da captura, total de quadros, possíveis descartes e diferenças entre as timelines.

**Base para slides:** duas capturas lado a lado, uma leitura Modbus e seus quadros TCP; checklist de “captura vazia”.

## Momento 6 — Aba Dados: volume, taxas e suspeita de sobrecarga

**Objetivo:** interpretar os gráficos da rede sem transformar volume observado em diagnóstico de saturação sem outras evidências.

**O que ensinar**

- No fluxo atual do software, a aba **Dados** fica indisponível enquanto a captura está ativa. **Parar captura** consolida os pacotes daquela sessão e habilita a análise.
- Explicar cada indicador: quantidade de pacotes, soma dos bytes capturados, taxa média de pacotes/s, taxa média de bytes/s e pico por segundo completo. Mostrar as séries temporais e os protocolos observados.
- Em **Dispositivos com mais tráfego**, comparar envio e recebimento; em **Conversas**, observar os pares de endpoints. A participação percentual de um dispositivo pode se sobrepor à de outro porque o mesmo pacote tem origem e destino.
- A amostra depende de interface, filtro, duração, capacidade de captura e limite de retenção visual de **2.000 pacotes**. Em alta taxa, 2.000 pacotes podem cobrir poucos segundos. Um gráfico bonito não substitui a informação de cobertura.
- Uma taxa observada de `54,6 KB/s` não é uma “faixa normal” universal nem utilização do link. Para avaliar sobrecarga, correlacionar com velocidade negociada, descartes/erros da NIC e da porta do switch, latência, retransmissões, RST e baseline do processo.

**Teste A — consolidar:** capturar um intervalo representativo, parar, abrir Dados e selecionar `30 s`, `1 min` e `Pacotes retidos`. Observar como período e quantidade analisada alteram médias e ranking.

**Teste B — participante dominante:** identificar a maior conversa e perguntar se ela é o cliente/servidor de interesse ou tráfego de outro serviço. Confrontar volume com comportamento do mapa, polling e filtros.

**Pergunta de checagem:** “O host com mais pacotes é necessariamente o responsável pela falha?” Resposta: não; é um candidato para investigação contextual.

**Registrar na ata:** duração, quadros analisados, período selecionado, taxas/picos, maior conversa, descartes e se a amostra permite ou não concluir algo sobre carga.

**Base para slides:** print da aba Dados com legendas numeradas e um exemplo de conclusão cautelosa: “houve pico observado; investigar contadores de porta e tempo de resposta”.

## Momento 7 — Teste completo: configurar, executar e ler o checklist

**Objetivo:** usar o fluxo guiado do software sem confundir o escopo do teste com a seleção lateral de um dispositivo.

**O que ensinar**

- Escolher o **papel do ensaio** em Teste completo. Cliente/Mestre valida os servidores incluídos na seleção de alvos; Servidor/Escravo analisa o servidor simulado local e clientes que interagirem com ele.
- Revisar as configurações antes da execução: janela de referência passiva, monitoramento TCP, leituras por bloco, intervalo, portas para descoberta, interface/filtro e limites de taxa/concorrência. Esses parâmetros determinam a cobertura e o tempo do teste.
- A descoberta de mapa é opcional. Em modo cliente, percorre funções e faixas configuradas com leituras; em modo servidor, observa ranges requisitados pelos clientes no período. Explicar `Mapa até 100, bloco 10` como leitura em blocos sucessivos sujeitos a timeout, exceções e eventual fallback.
- Sondagem de rede pode gerar ICMP, abertura TCP e leituras Modbus. Confirmar previamente o CIDR/IP e as portas autorizadas; uma rede `/16` pode levar muito tempo mesmo com taxa limitada.
- A execução pode iniciar captura e operações Modbus necessárias. O **Teste completo não envia escritas automáticas**, mas suas leituras/sondagens são tráfego ativo e devem ser autorizadas em produção.
- Ler cada etapa: objetivo, evidência, resultado, interpretação, recomendação e motivo de **Inconclusivo** ou **Não aplicável**. Etapas processadas não são percentual de disponibilidade.

**Teste A — cliente local:** incluir somente o alvo `127.0.0.1:1502`, executar uma rodada controlada e acompanhar o checklist. Registrar status das etapas, leituras válidas/tentadas e se a captura estava disponível.

**Teste B — parâmetro intencionalmente errado:** em laboratório, mudar Unit ID ou mapa de leitura e executar nova rodada. Comparar as duas execuções pelo endpoint, função, endereço, exceção e tempo. Restaurar a configuração correta ao final.

**Resultado esperado:** a rodada válida demonstra cobertura dos passos aplicáveis; a rodada alterada evidencia a camada afetada. Se uma etapa for inconclusiva por falta de amostra/captura, não classificá-la como defeito do PLC.

**Registrar na ata:** papel, alvos, configurações principais, duração, etapas OK/Atenção/Falha/Inconclusivo, diferença entre as duas rodadas.

**Base para slides:** fluxo `Escopo -> Execução -> Checklist -> Evidências -> Relatório`, mais um exemplo real de etapa válida e uma inconclusiva.

## Momento 8 — Descoberta de dispositivos, rotas e topologia inferida

**Objetivo:** ensinar o que a ferramenta identifica na rede e onde a evidência termina.

**O que ensinar**

- Separar categorias: **vizinho ARP**, **host observado**, **porta TCP aberta**, **Modbus confirmado** e **infraestrutura anunciada**. Uma conexão TCP aceita não confirma Modbus; uma resposta Modbus válida ou exceção coerente fornece evidência de protocolo.
- Um cliente Modbus pode não escutar em porta TCP e pode ser identificado por requisições observadas, inclusive no servidor simulado. Um servidor ocioso, em porta desconhecida e fora do escopo de sondagem, pode permanecer invisível.
- Revisar IP/máscara, gateway e rota do Windows. Um alvo remoto pode sair por placa, VPN ou rota diferente da esperada; ping não substitui teste TCP.
- A topologia exibe **conversas**, **saltos ICMP** e **anúncios LLDP/CDP**, quando disponíveis. Switch L2 geralmente não aparece no traceroute. Diagrama de evidências não é desenho comprovado de cabeamento, porta física ou VLAN.
- Sem tráfego visível, SPAN/TAP, LLDP/CDP ou consulta gerenciável, a ausência de um switch no desenho não é evidência de que ele não exista.

**Teste A — inventário:** na rodada autorizada, comparar as colunas “TCP aberto”, “Modbus confirmado” e “Modbus observado” de um IP. Explicar o que a resposta comprova e o que fica pendente.

**Teste B — topologia:** abrir o diagrama e os detalhes da rota ICMP. Pedir à turma que identifique quais linhas são conversas observadas e quais são inferências de roteamento ou anúncios.

**Pergunta de checagem:** “Dois IPs locais e `127.0.0.1` representam necessariamente três equipamentos?” Resposta: não; IPs diferentes podem pertencer ao mesmo computador e endpoint não é sinônimo de dispositivo físico.

**Registrar na ata:** IPs relevantes, interfaces/rotas, evidência de protocolo, anúncios recebidos, inferências e lacunas que exigem consulta ao switch.

**Base para slides:** escada de confiança `ARP -> TCP -> Modbus` e diagrama anotado com “observado”, “anunciado” e “inferido”.

## Momento 9 — Relatório: transformar dados em diagnóstico técnico

**Objetivo:** produzir uma conclusão rastreável e uma próxima ação útil para a equipe de campo.

**O que ensinar**

- Diferenciar os estados **OK**, **Atenção**, **Falha**, **Inconclusivo** e **Não aplicável**. “OK” numa etapa significa que o critério daquela etapa foi atendido na janela observada; não certifica toda a rede.
- Separar no relatório: **fato** (o que foi medido), **interpretação** (o que pode explicar), **limite** (o que não foi observado) e **próxima verificação** (como reduzir a incerteza).
- Priorizar achados por vínculo com o processo: endpoint, horário, UID, FC, início, quantidade, tentativas/sucessos, exceções e latências. Alto volume de eventos repetidos pode corresponder a uma única configuração incorreta.
- Comparar relatório **resumido** para comunicação rápida e **completo** para auditoria técnica. O relatório da topologia pode incluir evidências e diagramas; ao compartilhar MD com imagens auxiliares, enviar os arquivos associados.
- Para repetir um diagnóstico, registrar versão do software, configuração `.ffd`, interface, filtro, janela, portas, alvos e parâmetros de descoberta.

**Exercício de redação:** dar à turma uma evidência, por exemplo “FC03, UID 1, início 9, quantidade 2, exceção 02 em três tentativas contra HR 0–9”. Pedir uma conclusão de uma frase e uma próxima verificação.

**Exemplo de resposta técnica:** “O servidor respondeu às três requisições com exceção de endereço para a faixa 9–10; o mapa configurado termina no offset 9. Conferir o bloco requisitado e a convenção do endereço antes de repetir.”

**Registrar na ata:** relatório escolhido, principais achados, correções sugeridas e verificações ainda abertas.

**Base para slides:** print do resumo, print de uma etapa completa e modelo de frase `Evidência -> Interpretação -> Limitação -> Próximo teste`.

## Momento 10 — Estudo de caso, avaliação e fechamento

**Objetivo:** consolidar a sequência de diagnóstico de ponta a ponta.

**Cenário proposto para laboratório:** “O cliente indica falha de leitura no HR documentado como 40010. O IP responde a ping. Há serviço TCP aberto, porém a leitura solicitada recebe exceção ou timeout.” Informar aos grupos qual evidência está disponível e permitir que escolham a próxima verificação. Não revelar inicialmente se a falha preparada é porta, UID, FC ou offset.

**Roteiro do exercício**

1. Cada grupo descreve o que já está comprovado: ICMP, TCP, resposta Modbus ou nenhum deles.
2. Confere endpoint, UID, FC, offset e quantidade no software e na documentação da bancada.
3. Executa uma leitura mínima e segura; consulta mapa, timeline e, se houver, captura.
4. Propõe uma hipótese, uma evidência que a sustenta, outra que poderia refutá-la e o próximo teste autorizado.
5. Escreve uma conclusão curta para o relatório e apresenta aos demais.

**Critérios para observar a aprendizagem:** o grupo distingue camada TCP da camada Modbus; não interpreta exceção como perda total de conexão; verifica base zero e fim do range; não propõe escrita para “testar” um ponto desconhecido; registra limitação de captura e escopo.

**Fechamento do instrutor:** retomar as dúvidas do Momento 1 e assinalar quais foram resolvidas. Registrar pendências do software, de documentação do PLC e de infraestrutura que precisam de outro responsável ou de acesso adicional.

**Registrar na ata:** hipóteses dos grupos, conclusão correta do cenário, dúvidas remanescentes, feedback da turma e encaminhamentos.

**Base para slides:** uma linha do tempo do caso, quatro evidências liberadas em sequência e um slide final com o método de diagnóstico.

## Cuidados operacionais a reforçar ao longo da aula

- Preferir simulação/bancada. Em produção, confirmar autorização, escopo e responsável pelo processo antes de captura ou sondagem ativa.
- O Teste completo e a descoberta de mapa fazem leituras e testes de rede; não executam escrita automática. Escrita manual em PLC exige ponto, valor, consequência e reversão aprovados. Se uma escrita der timeout, verificar o estado antes de repetir, pois ela pode ter sido aplicada.
- Para substituir um servidor real pelo software com o mesmo IP, retirar ou isolar o equipamento original primeiro; evitar IP duplicado e observar ARP, firewall, interface e porta.
- Distinguir “sem evidência capturada” de “equipamento ausente”. O filtro, a interface, a posição da captura e a duração limitam o que pode ser visto.
- Comparar testes antes/depois com parâmetros equivalentes. Mudanças de UID, mapa, duração ou filtro podem explicar diferenças aparentes sem que a rede tenha mudado.

## Registro formal da ata após o treinamento

### Participantes

| Nome | Área / função | Presença |
|---|---|---|
| A preencher | A preencher | A preencher |

### Síntese por momento

| Momento | Realizado? | Demonstração/resultado | Dúvida ou ajuste para os slides |
|---|---|---|---|
| 1. Revisão do treinamento anterior | A preencher | A preencher | A preencher |
| 2. Transação Modbus TCP | A preencher | A preencher | A preencher |
| 3. Bancada e papéis | A preencher | A preencher | A preencher |
| 4. Mapa e endereçamento | A preencher | A preencher | A preencher |
| 5. Captura e timelines | A preencher | A preencher | A preencher |
| 6. Dados da rede | A preencher | A preencher | A preencher |
| 7. Teste completo | A preencher | A preencher | A preencher |
| 8. Descoberta e topologia | A preencher | A preencher | A preencher |
| 9. Relatório | A preencher | A preencher | A preencher |
| 10. Estudo de caso | A preencher | A preencher | A preencher |

### Dúvidas, decisões e ações

| Tipo | Descrição / evidência | Responsável | Prazo / estado |
|---|---|---|---|
| Dúvida | A preencher | A preencher | A preencher |
| Decisão | A preencher | A preencher | A preencher |
| Ação | A preencher | A preencher | A preencher |

**Encerramento:** horário a preencher. **Avaliação da turma:** a preencher. **Pendências para próxima sessão:** a preencher.

## Fontes para preparar os slides

- Manual integrado do Modbus TCP Troubleshooter: tópicos de conexão, endereçamento, mapa, captura, Dados, Teste completo, descoberta, topologia e relatório.
- [[Deliverables/projects/2026-07-14-modbus-tcp-troubleshooter/docs/2026-09-29-teste-completo-independente]]
- [[Deliverables/projects/2026-07-14-modbus-tcp-troubleshooter/docs/2026-09-28-janelas-e-relatorio-tecnico]]
- [[Deliverables/projects/2026-07-14-modbus-tcp-troubleshooter/docs/2026-09-28-topologia-observada]]
- [Modbus Application Protocol Specification V1.1b3](https://www.modbus.org/docs/Modbus_Application_Protocol_V1_1b3.pdf): funções, endereços e exceções.
- [Modbus Messaging on TCP/IP Implementation Guide V1.0b](https://www.modbus.org/docs/Modbus_Messaging_Implementation_Guide_V1_0b.pdf): MBAP, TID/UID e tratamento de transações TCP.
