using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ModbusTcpTroubleshooter.App;

internal static class Program
{
 static string Output = Path.GetFullPath("training/assets/pratica");
 [STAThread] static void Main()
 {
  Directory.CreateDirectory(Output);
  var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
  app.InitializeComponent();
  SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
  var w = new MainWindow { Width = 1420, Height = 940, WindowState = WindowState.Normal };
  w.Show(); Pump(w);
  var vm = (MainViewModel)w.DataContext;
  vm.SelectedClientSession.Name = "PLC de bancada";
  vm.AddClientSession("192.168.1.50",1502,1,1000,true).Name="PLC demonstrativo";
  vm.SelectedClientSession=vm.ClientSessions[0];
  MainTab(w,"ClientStationTab"); Save(w,"estacao");
  MainTab(w,"CommunicationMapTab");
  var map=(TabControl)w.FindName("MapPresentationTabs");
  map.SelectedIndex=0; Pump(w); Save((FrameworkElement)map,"dispositivo");
  map.SelectedIndex=1; Pump(w); Save((FrameworkElement)map,"blocos");
  var clientTabs=Children((DependencyObject)((TabItem)map.Items[1]).Content).OfType<TabControl>().First();
  clientTabs.SelectedIndex=1; Pump(w); Save((FrameworkElement)map,"mapa-tabela");
  map.SelectedItem=w.FindName("AddressMapTab"); Pump(w); Save((FrameworkElement)map,"mapa-visual");
  vm.SelectedMode="Server"; map.SelectedIndex=1; Pump(w); Save((FrameworkElement)map,"faixas-server");
  var serverTabs=Children((DependencyObject)((TabItem)map.Items[1]).Content).OfType<TabControl>().Last();
  serverTabs.SelectedIndex=1; Pump(w); Save((FrameworkElement)map,"valores-server");
  vm.SelectedMode="Client";
  MainTab(w,"TimelineTab"); var timelines=(TabControl)w.FindName("NetworkTimelineTabs");
  timelines.SelectedIndex=0; Pump(w); Save(timelines,"timeline-modbus");
  timelines.SelectedIndex=1; Pump(w); Save(timelines,"timeline-tcp");
  var start=DateTimeOffset.Now.AddSeconds(-15);
  for(int i=0;i<300;i++) vm.TcpTimeline.Add(new TcpTimelineRow{Number=i+1,Timestamp=start.AddMilliseconds(i*50),RelativeTime=i*.05,SourceHost=i%2==0?"192.168.1.10":"192.168.1.50",DestinationHost=i%2==0?"192.168.1.50":"192.168.1.10",Source=i%2==0?"192.168.1.10:51000":"192.168.1.50:1502",Destination=i%2==0?"192.168.1.50:1502":"192.168.1.10:51000",Protocol="Modbus TCP",Length=90,IsTcp=true,TcpAcknowledgment=true,TcpWindow=8192,TcpPayloadLength=20,Info="Exemplo didatico: leitura FC03",ModbusKind="FC03"});
  vm.HasCompletedNetworkCapture=true; timelines.SelectedIndex=2; Pump(w);
  Save(timelines,"dados-resumo");
  var dataView=Children(timelines).OfType<NetworkDataView>().First();
  Save(Children(dataView).OfType<GroupBox>().First(g=>g.Header?.ToString()=="Qualidade e limites da captura"),"dados-limites");
  var dataTabs=Children(dataView).OfType<TabControl>().First();
  dataTabs.SelectedIndex=1; Pump(w); Save(timelines,"dados-hosts");
  dataTabs.SelectedIndex=2; Pump(w); Save(timelines,"dados-conversas");
  MainTab(w,"IssueLogsTab"); Save((FrameworkElement)((TabItem)w.FindName("IssueLogsTab")).Content,"avisos");
  MainTab(w,"FullTestTab"); var sections=(TabControl)w.FindName("FullTestSections");
  sections.SelectedIndex=0; Pump(w); Save(w,"teste-geral");
  Dialog(new FullTestReviewDialog(vm.BuildTestReview()),"revisar-teste");
  Save((FrameworkElement)sections,"teste-execucao");
  for(int i=0;i<vm.FullTestSteps.Count;i++){
   vm.FullTestSteps[i].Status=i==2?"Inconclusivo":i==8?"Atencao":"OK";
   vm.FullTestSteps[i].Result=i==2?"EXEMPLO DIDATICO. Estatisticas do driver indisponiveis. Cobertura insuficiente para concluir sobre perdas de captura.":i==8?"EXEMPLO DIDATICO. 15/15 leituras validas. Mediana: 12 ms. p95: 1050 ms. Intervalo cadastrado: 1000 ms. Sinal de latencia elevada.":"EXEMPLO DIDATICO. Criterio da etapa atendido no cenario demonstrativo. Nenhum ensaio real foi executado.";
   vm.FullTestSteps[i].Recommendation=i==2?"Verificar interface e disponibilidade de contadores do driver. Nao concluir falha Modbus pela ausencia desses dados.":i==8?"Comparar latencia, ciclo total e carga. Repetir com a mesma janela e conferir limites do equipamento.":"Confrontar com a configuracao documentada e preservar os parametros da execucao.";
  }
  vm.SelectedFullTestStep=vm.FullTestSteps[Math.Min(8,vm.FullTestSteps.Count-1)];
  typeof(MainViewModel).GetMethod("UpdateFullTestSummaryCards",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,null);
  sections.SelectedIndex=0; Pump(w); Save(sections,"teste-evidencias");
  sections.SelectedIndex=1; Pump(w); Save(sections,"teste-resumo");
  vm.NetworkDiscoveryRows.Add(new NetworkDiscoveryRow{Ip="192.168.1.50",Mac="02-00-00-00-00-50",Source="Exemplo didatico",RoleGuess="Servidor Modbus confirmado",OpenTcpPorts="1502",ConfirmedModbusPorts="1502",IsModbusConfirmed=true,Notes="Leitura minima reconhecida. Dados demonstrativos."});
  vm.NetworkDiscoveryRows.Add(new NetworkDiscoveryRow{Ip="192.168.1.10",Source="Exemplo didatico",RoleGuess="Cliente Modbus observado",ObservedModbusPorts="1502",IsModbusClientObserved=true,Notes="Requisicoes observadas. Dados demonstrativos."});
  sections.SelectedIndex=2; Pump(w); Save(sections,"teste-rede");
  vm.TopologyLinks.Add(new("192.168.1.10","192.168.1.50","Captura Modbus TCP",300,"Demonstracao FC03"));
  vm.RelevantTopologyLinks.Add(vm.TopologyLinks[0]);
  vm.TopologyRouteHops.Add(new("192.168.1.50",1,"192.168.1.50","Success",1));
  vm.TopologySummary="EXEMPLO DIDATICO: um cliente e um servidor. Enlace de comunicacao observado.";
  vm.TopologyCoverage="Caminho fisico nao verificado. Switches L2 nao aparecem no TTL.";
  sections.SelectedIndex=3; Pump(w); Save(sections,"teste-topologia");
  var topoTabs=Children((DependencyObject)((TabItem)w.FindName("FullTestTopologyTab")).Content).OfType<TabControl>().First();
  foreach(var pair in new[]{(1,"topo-evidencias"),(2,"topo-rotas"),(3,"topo-vizinhos")}){topoTabs.SelectedIndex=pair.Item1;Pump(w);Save(sections,pair.Item2);}
  vm.DiscoveredMapRows.Add(new MapDiscoveryRow{Endpoint="192.168.1.50:1502",UnitId="1",Function="FC03 Holding Registers",StartAddress=0,EndAddress=9,Quantity=10,DiscoveryMode="Sondagem ativa",Confidence="Confirmado por leitura",Notes="Exemplo didatico. Nomes e escalas nao descobertos."});
  sections.SelectedIndex=4; Pump(w); Save(sections,"teste-mapa");
  vm.FullTestReport="# Exemplo didatico de relatorio\n\nResultado: ATENCAO\n\nAlvo: 192.168.1.50:1502, UID 1\nFC03, inicio 0, quantidade 10\nLeituras: 15/15 validas\nMediana: 12 ms\n\nCobertura: caminho fisico nao verificado.\n\nProxima verificacao: repetir o ensaio com a mesma janela e confrontar com o mapa documentado.\n\nEste texto e demonstrativo, nao e um resultado de campo.";
  sections.SelectedIndex=5; Pump(w); Save(sections,"teste-relatorio");
  Dialog(new ConnectionSettingsDialog(true,"127.0.0.1",1502,1,1000),"config-client");
  Dialog(new ConnectionSettingsDialog(false,"0.0.0.0",1502,1,1000),"config-server");
  Dialog(new FullTestTargetsDialog(vm.ClientSessions),"alvos-teste");
  Dialog(new MapDiscoverySettingsDialog(true,false,1,10,100,10,true,true,true,true,true),"descoberta");
  var scope=new FullTestScopeDialog(true,500,2,12,30,15,1000,"192.168.1.0/24");
  scope.Show(); Pump(scope);
  var scroller=Children(scope).OfType<ScrollViewer>().First();
  scroller.ScrollToTop(); Pump(scope); Save((FrameworkElement)scope.Content,"procedimentos-tempos");
  scroller.ScrollToVerticalOffset(340); Pump(scope); Save((FrameworkElement)scope.Content,"procedimentos-rede");
  scroller.ScrollToBottom(); Pump(scope); Save((FrameworkElement)scope.Content,"procedimentos-rotas");
  int gi=0;
  foreach(var group in Children(scope).OfType<GroupBox>().ToArray()){
   group.BringIntoView();Pump(scope);Save(group,"procedimento-grupo-"+(gi++));
  }
  scope.Close();
  Dialog(new WriteRegisterDialog("Bancada local: HR 0 a 1",0,1,100,"Escrita manual","Valor"),"escrita");
  Dialog(new ManualWindow("teste-completo"),"manual");
  vm.StopAllOperations(); w.Close(); app.Shutdown();
  Console.WriteLine("Capturas concluidas sem iniciar servicos ou sondagens.");
 }
 static void MainTab(MainWindow w,string name){var t=(TabItem)w.FindName(name);t.Visibility=Visibility.Visible;((TabControl)w.FindName("MainTabs")).SelectedItem=t;Pump(w);}
 static void Dialog(Window w,string name){w.Show();Pump(w);Save((FrameworkElement)w.Content,name);w.Close();}
 static void Pump(Window w){w.UpdateLayout();w.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);w.UpdateLayout();}
 static void Save(FrameworkElement e,string name){
  var viewportHeights = new Dictionary<string,int>{{"blocos",270},{"mapa-tabela",330},{"mapa-visual",420},{"faixas-server",310},{"valores-server",370},{"timeline-modbus",340},{"timeline-tcp",430},{"dados-hosts",340},{"dados-conversas",340},{"avisos",350},{"teste-rede",260},{"topo-evidencias",280},{"topo-rotas",290},{"topo-vizinhos",290},{"teste-mapa",270},{"teste-relatorio",410},{"dispositivo",530}};
  if(name=="estacao")viewportHeights[name]=450;
  if(name=="dados-resumo")viewportHeights[name]=425;
  var height=viewportHeights.TryGetValue(name,out var crop)?Math.Min(crop,e.ActualHeight):e.ActualHeight;
  var bmp=new RenderTargetBitmap((int)Math.Ceiling(e.ActualWidth),(int)Math.Ceiling(height),96,96,PixelFormats.Pbgra32);
  var visual=new DrawingVisual();using(var d=visual.RenderOpen()){d.DrawRectangle(Brushes.White,null,new Rect(0,0,e.ActualWidth,e.ActualHeight));d.DrawRectangle(new VisualBrush(e){Stretch=Stretch.Fill},null,new Rect(0,0,e.ActualWidth,e.ActualHeight));}bmp.Render(visual);
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var f=File.Create(Path.Combine(Output,name+".png"));encoder.Save(f);Console.WriteLine(name);
 }
 static IEnumerable<DependencyObject> Children(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);yield return c;foreach(var d in Children(c))yield return d;}}
}
