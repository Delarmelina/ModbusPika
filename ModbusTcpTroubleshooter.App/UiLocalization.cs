using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Data;
using System.Windows.Documents;

namespace ModbusTcpTroubleshooter.App;

public enum UiLanguage
{
    Portuguese,
    English
}

public static class UiLocalization
{
    private static readonly Dictionary<string, (string English, string Portuguese)> Entries = new(StringComparer.Ordinal)
    {
        ["_File"] = ("_File", "_Arquivo"),
        ["_Connections"] = ("_Connections", "_Conexoes"),
        ["Client (Master)"] = ("Client (Master)", "Cliente (Mestre)"),
        ["Configure Client..."] = ("Configure Client...", "Configurar cliente..."),
        ["Connect..."] = ("Connect...", "Conectar..."),
        ["Read Once"] = ("Read Once", "Leitura unica"),
        ["Disconnect"] = ("Disconnect", "Desconectar"),
        ["Server (Slave)"] = ("Server (Slave)", "Servidor (Escravo)"),
        ["Configure Server..."] = ("Configure Server...", "Configurar servidor..."),
        ["Start Server..."] = ("Start Server...", "Iniciar servidor..."),
        ["Stop Server"] = ("Stop Server", "Parar servidor"),
        ["_View"] = ("_View", "_Exibir"),
        ["Communication Map"] = ("Communication Map", "Mapa de comunicacao"),
        ["Timeline"] = ("Timeline", "Linha do tempo"),
        ["Issue Logs"] = ("Issue Logs", "Registro de avisos"),
        ["Full Test"] = ("Full Test", "Teste completo"),
        ["_Help"] = ("_Help", "_Ajuda"),
        ["Language"] = ("Language", "Idioma"),
        ["About"] = ("About", "Sobre"),
        ["Home"] = ("Home", "Inicio"),
        ["Operation"] = ("Operation", "Operacao"),
        ["Basic tutorial"] = ("Basic tutorial", "Tutorial basico"),
        ["1. Open Connection and choose Configure Client or Configure Server."] = ("1. Open Connection and choose Configure Client or Configure Server.", "1. Abra Conexoes e selecione Configurar cliente ou Configurar servidor."),
        ["2. Configure the IP address, port and Unit ID, then confirm with OK."] = ("2. Configure the IP address, port and Unit ID, then confirm with OK.", "2. Configure endereco IP, porta e Unit ID e confirme em OK."),
        ["3. Use Operation to configure the map and monitor Modbus communication."] = ("3. Use Operation to configure the map and monitor Modbus communication.", "3. Use Operacao para configurar o mapa e monitorar a comunicacao Modbus."),
        ["4. Use Capture for passive network traffic and Full Test for guided diagnostics."] = ("4. Use Capture for passive network traffic and Full Test for guided diagnostics.", "4. Use Capture para trafego passivo e Teste completo para diagnostico guiado."),
        ["Use the View menu to open Communication Map, Timeline, Issue Logs and Full Test."] = ("Use the View menu to open Communication Map, Timeline, Issue Logs and Full Test.", "Use o menu Exibir para abrir Mapa de comunicacao, Linha do tempo, Registro de avisos e Teste completo."),
        ["Run Full Test"] = ("Run Full Test", "Executar teste completo"),
        ["Cancel Test"] = ("Cancel Test", "Cancelar teste"),
        ["Exit"] = ("Exit", "Sair"),
        ["Test Scope..."] = ("Test Scope...", "Escopo do teste..."),
        ["Map Discovery..."] = ("Map Discovery...", "Descoberta de mapa..."),
        ["Export Report"] = ("Export Report", "Exportar relatorio"),
        ["What is tested?"] = ("What is tested?", "O que e testado?"),
        ["Overview"] = ("Overview", "Visao geral"),
        ["Checklist"] = ("Checklist", "Checklist"),
        ["Devices / network"] = ("Devices / network", "Dispositivos / rede"),
        ["Map discovery"] = ("Map discovery", "Mapa descoberto"),
        ["Report"] = ("Report", "Relatorio"),
        ["Test scope"] = ("Test scope", "Escopo do teste"),
        ["The test starts the selected Modbus role when necessary, selects a capture interface by traffic, analyzes TCP/IP traffic, route, ARP, bandwidth and configured Modbus communication."] = ("The test starts the selected Modbus role when necessary, selects a capture interface by traffic, analyzes TCP/IP traffic, route, ARP, bandwidth and configured Modbus communication.", "O teste inicia o papel Modbus selecionado quando necessario, seleciona interface de captura por trafego e analisa trafego TCP/IP, rota, ARP, banda e comunicacao Modbus configurada."),
        ["Use Test Scope... to adjust network traffic collection. Use Map Discovery... only when read-only probing of endpoints is authorized."] = ("Use Test Scope... to adjust network traffic collection. Use Map Discovery... only when read-only probing of endpoints is authorized.", "Use Escopo do teste... para ajustar a coleta de trafego. Use Descoberta de mapa... somente quando a sondagem read-only estiver autorizada."),
        ["Status geral"] = ("Overall status", "Status geral"),
        ["Rede / hosts"] = ("Network / hosts", "Rede / hosts"),
        ["Modbus descoberto"] = ("Discovered Modbus", "Modbus descoberto"),
        ["Rotas / gateway"] = ("Routes / gateway", "Rotas / gateway"),
        ["Banda / carga"] = ("Bandwidth / load", "Banda / carga"),
        ["Detalhe da etapa selecionada"] = ("Selected step detail", "Detalhe da etapa selecionada"),
        ["Relatorio final exportavel"] = ("Exportable final report", "Relatorio final exportavel"),
        ["Connection settings"] = ("Connection settings", "Configuracao da conexao"),
        ["Configure Modbus TCP Client"] = ("Configure Modbus TCP Client", "Configurar cliente Modbus TCP"),
        ["Configure Modbus TCP Server"] = ("Configure Modbus TCP Server", "Configurar servidor Modbus TCP"),
        ["IP address"] = ("IP address", "Endereco IP"),
        ["Listen address"] = ("Listen address", "Endereco de escuta"),
        ["Service port"] = ("Service port", "Porta de servico"),
        ["Scan rate"] = ("Scan rate", "Intervalo de leitura"),
        ["Keep one TCP connection open for all requests"] = ("Keep one TCP connection open for all requests", "Manter uma conexao TCP aberta para todas as requisicoes"),
        ["Uses one TCP socket for the polling session. Disconnect closes it."] = ("Uses one TCP socket for the polling session. Disconnect closes it.", "Usa um socket TCP durante o polling. Desconectar encerra a sessao."),
        ["Cancel"] = ("Cancel", "Cancelar"),
        ["Full Test Scope"] = ("Full Test Scope", "Escopo do teste completo"),
        ["Network discovery"] = ("Network discovery", "Descoberta de rede"),
        ["Enable active subnet scan"] = ("Enable active subnet scan", "Habilitar varredura ativa da sub-rede"),
        ["Passive observation"] = ("Passive observation", "Observacao passiva"),
        ["Probe timeout"] = ("Probe timeout", "Timeout da sonda"),
        ["Parallel probes"] = ("Parallel probes", "Sondas paralelas"),
        ["Observation window"] = ("Observation window", "Janela de observacao"),
        ["Map Discovery Settings"] = ("Map Discovery Settings", "Configuracoes de descoberta de mapa"),
        ["Enable map discovery"] = ("Enable map discovery", "Habilitar descoberta de mapa"),
        ["Probe Modbus maps during Full Test"] = ("Probe Modbus maps during Full Test", "Sondar mapas Modbus durante o Teste completo"),
        ["Address range"] = ("Address range", "Faixa de enderecos"),
        ["Maximum address"] = ("Maximum address", "Endereco maximo"),
        ["Block size"] = ("Block size", "Tamanho do bloco"),
        ["Unit IDs and function codes"] = ("Unit IDs and function codes", "IDs de unidade e codigos de funcao"),
        ["Sweep Unit IDs"] = ("Sweep Unit IDs", "Varrer Unit IDs"),
        ["Unit ID range"] = ("Unit ID range", "Faixa de Unit ID"),
        ["Read functions:"] = ("Read functions:", "Funcoes de leitura:"),
        ["Fallback"] = ("Fallback", "Alternativa"),
        ["Use point-by-point fallback"] = ("Use point-by-point fallback", "Tentar endereco por endereco se um bloco falhar"),
        ["Connection Settings"] = ("Connection Settings", "Configuracao da conexao"),
        ["Attempts TCP connections to candidate hosts in the local subnet. Use only on networks where active probing is authorized."] = ("Attempts TCP connections to candidate hosts in the local subnet. Use only on networks where active probing is authorized.", "Tenta conexoes TCP com dispositivos candidatos na sub-rede local. Use somente em redes onde a sondagem ativa esteja autorizada."),
        ["ms per endpoint"] = ("ms per endpoint", "ms por dispositivo"),
        ["simultaneous TCP attempts"] = ("simultaneous TCP attempts", "tentativas TCP simultaneas"),
        ["seconds captured for timing and load analysis"] = ("seconds captured for timing and load analysis", "segundos de captura para analise de intervalo e carga"),
        ["Client role: executes read-only FC01-FC04 requests against candidate endpoints. Server role: records ranges requested by connected clients. No write function is used."] = ("Client role: executes read-only FC01-FC04 requests against candidate endpoints. Server role: records ranges requested by connected clients. No write function is used.", "No modo cliente, envia requisicoes FC01-FC04 somente para leitura aos dispositivos candidatos. No modo servidor, registra as faixas requisitadas pelos clientes conectados. Nenhuma funcao de escrita e usada."),
        ["tests addresses from 0 through this value"] = ("tests addresses from 0 through this value", "testa enderecos de 0 ate este valor"),
        ["registers/bits per read request"] = ("registers/bits per read request", "registradores ou bits por requisicao de leitura"),
        ["through"] = ("through", "ate"),
        ["FC01 Coils"] = ("FC01 Coils", "FC01 Bobinas"),
        ["FC02 Discrete"] = ("FC02 Discrete", "FC02 Entradas discretas"),
        ["FC03 Holding"] = ("FC03 Holding", "FC03 Registradores holding"),
        ["FC04 Input"] = ("FC04 Input", "FC04 Registradores de entrada"),
        ["When a block fails, individual addresses are retried to identify smaller valid ranges. This increases the number of requests."] = ("When a block fails, individual addresses are retried to identify smaller valid ranges. This increases the number of requests.", "Se um bloco falhar, cada endereco sera tentado individualmente para localizar as faixas validas. Isso aumenta a quantidade de requisicoes."),
        ["Industrial communication diagnostics"] = ("Industrial communication diagnostics", "Diagnostico de comunicacao industrial"),
        ["ROLE"] = ("ROLE", "MODO"),
        ["Workspace overview"] = ("Workspace overview", "Visao geral"),
        ["Choose an operating role, configure the endpoint, then open the views you need from the View menu."] = ("Choose an operating role, configure the endpoint, then open the views you need from the View menu.", "Escolha o modo de operacao, configure o dispositivo e abra as telas necessarias pelo menu Exibir."),
        ["Client / Master"] = ("Client / Master", "Cliente / Mestre"),
        ["Remote Modbus TCP endpoint"] = ("Remote Modbus TCP endpoint", "Dispositivo Modbus TCP remoto"),
        ["Port "] = ("Port ", "Porta "),
        ["   Unit ID "] = ("   Unit ID ", "   ID da unidade "),
        ["Connect / Start Scan"] = ("Connect / Start Scan", "Conectar / iniciar leitura"),
        ["Server / Slave"] = ("Server / Slave", "Servidor / Escravo"),
        ["Local Modbus TCP endpoint"] = ("Local Modbus TCP endpoint", "Ponto Modbus TCP local"),
        ["Server status: "] = ("Server status: ", "Estado do servidor: "),
        ["Start Server"] = ("Start Server", "Iniciar servidor"),
        ["Open Communication Map"] = ("Open Communication Map", "Abrir mapa de comunicacao"),
        ["Diagnostic workspace"] = ("Diagnostic workspace", "Areas de diagnostico"),
        ["Tip: use Connections to configure and control one role at a time. Full Test uses the selected role and the endpoint shown in the status bar."] = ("Tip: use Connections to configure and control one role at a time. Full Test uses the selected role and the endpoint shown in the status bar.", "Dica: use Conexoes para configurar e operar um modo por vez. O teste completo usa o modo selecionado e o endereco indicado na barra de status."),
        ["Interface"] = ("Interface", "Interface de rede"),
        ["Filter Source"] = ("Filter Source", "Filtro de origem"),
        ["Filter Destination"] = ("Filter Destination", "Filtro de destino"),
        ["Filter Protocol"] = ("Filter Protocol", "Filtro de protocolo"),
        ["Filter Info"] = ("Filter Info", "Filtro de informacao"),
        ["No."] = ("No.", "Nº"),
        ["Time"] = ("Time", "Tempo"),
        ["Source"] = ("Source", "Origem"),
        ["Destination"] = ("Destination", "Destino"),
        ["Protocol"] = ("Protocol", "Protocolo"),
        ["Length"] = ("Length", "Tamanho"),
        ["Info"] = ("Info", "Informacao"),
        ["Active role: "] = ("Active role: ", "Modo ativo: "),
        ["   |   Endpoint: "] = ("   |   Endpoint: ", "   |   Endereco: "),
        ["   |   Network scan: "] = ("   |   Network scan: ", "   |   Varredura de rede: "),
        ["   |   Map discovery: "] = ("   |   Map discovery: ", "   |   Descoberta de mapa: "),
        ["Mode: "] = ("Mode: ", "Modo: "),
        ["Target: "] = ("Target: ", "Alvo: "),
        ["Listen: "] = ("Listen: ", "Escuta: "),
        ["Unit ID: "] = ("Unit ID: ", "ID da unidade: "),
        ["Scan: "] = ("Scan: ", "Leitura: "),
        ["Server: "] = ("Server: ", "Servidor: "),
        ["Capture: "] = ("Capture: ", "Captura: "),
        ["Running"] = ("Running", "Em execucao"),
        ["Stopped"] = ("Stopped", "Parado"),
        ["Off"] = ("Off", "Inativa"),
        ["Active"] = ("Active", "Ativa")
    };

    public static UiLanguage CurrentLanguage
    {
        get => UiLanguage.Portuguese;
        set { }
    }

    public static void Apply(DependencyObject root)
    {
        Apply(root, new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance));
    }

    private static void Apply(DependencyObject root, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root))
        {
            return;
        }

        TranslateElement(root);
        ApplyChildren(root, visited);
    }

    private static void ApplyChildren(DependencyObject root, HashSet<DependencyObject> visited)
    {
        if (root is Visual or Visual3D)
        {
            var visualChildCount = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < visualChildCount; index++)
            {
                Apply(VisualTreeHelper.GetChild(root, index), visited);
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().ToArray())
        {
            Apply(child, visited);
        }
    }

    private static void TranslateElement(DependencyObject element)
    {
        if (element is Window window)
        {
            window.Title = Translate(window.Title);
        }

        if (element is HeaderedItemsControl menu && menu.Header is string menuHeader &&
            !BindingOperations.IsDataBound(menu, HeaderedItemsControl.HeaderProperty))
        {
            menu.Header = Translate(menuHeader);
        }

        if (element is HeaderedContentControl headered && headered.Header is string header &&
            !BindingOperations.IsDataBound(headered, HeaderedContentControl.HeaderProperty))
        {
            headered.Header = Translate(header);
        }

        if (element is ContentControl content && content.Content is string contentText &&
            !BindingOperations.IsDataBound(content, ContentControl.ContentProperty))
        {
            content.Content = Translate(contentText);
        }

        if (element is TextBlock textBlock && !BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty))
        {
            foreach (var run in textBlock.Inlines.OfType<Run>().ToArray())
            {
                if (!BindingOperations.IsDataBound(run, Run.TextProperty))
                    run.Text = Translate(run.Text);
            }
        }

        if (element is DataGrid grid)
            foreach (var column in grid.Columns)
                if (column.Header is string title) column.Header = Translate(title);
    }

    private static string Translate(string value)
    {
        foreach (var entry in Entries.Values)
        {
            if (value == entry.English || value == entry.Portuguese)
            {
                return entry.Portuguese;
            }
        }

        return value;
    }
}
