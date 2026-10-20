using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using FirmaVisible.Core;

namespace FirmaVisible.App;

public partial class MainWindow : Window
{
    private readonly string session=Path.Combine(Path.GetTempPath(),"FirmaPDF",Guid.NewGuid().ToString("N"));
    private readonly List<CertificateIdentity> identities=[];
    public ObservableCollection<DocumentSession> Documents { get; }=[];
    public DocumentSession? ActiveDocument { get; private set; }
    private readonly Queue<string> pendingOpen=[];
    private bool busy,initialized,switchingTabs,draining;
    private double[]? before;
    private Point start;
    private int dragMode;
    private double baseWidth=800;
    private RenderedPage? firstRendered,followingRendered;
    private int spreadStart;
    private Grid? dragSurface;
    public bool TwoPageMode=>followingRendered is not null&&Viewport.ActualWidth>=1040&&Viewport.ActualHeight>=360;
    private int LeftPageIndex=>TwoPageMode?spreadStart:page;
    private RenderedPage? LeftRendered=>LeftPageIndex==spreadStart?firstRendered:followingRendered;
    private readonly UserPreferencesStore preferencesStore;
    private readonly Func<string,CertificateIdentity?> identityLookup;
    private UserPreferences preferences=UserPreferences.Default();
    private string? input=>ActiveDocument?.InputPath;
    private string? displayInput=>ActiveDocument?.PreviewPath;
    private string? originalPath=>ActiveDocument?.OriginalPath;
    private int page { get=>ActiveDocument?.CurrentPage??0;set { if(ActiveDocument is {} d)d.CurrentPage=value; } }
    private int pages=>ActiveDocument?.PageCount??0;
    private double pageWidth=>ActiveDocument?.PageWidth??600;
    private double pageHeight=>ActiveDocument?.PageHeight??800;
    private double[]? box { get=>ActiveDocument is {} d&&d.SelectionPage==page?d.Selection:null;set=>ActiveDocument?.SetSelection(value); }
    private CertificateIdentity? Identity=>CertificateCombo.SelectedItem as CertificateIdentity;
    private string IssuerName=>Identity?.Certificate.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName,true)??"Emisor del certificado";
    public MainWindow():this(new UserPreferencesStore()){}
    public MainWindow(UserPreferencesStore store,Func<string,CertificateIdentity?>? identityLookup=null)
    {
        this.identityLookup=identityLookup??CertificateIdentity.FindInWindowsStore;preferencesStore=store;
        InitializeComponent();Directory.CreateDirectory(session);
        Height=Math.Min(Height,SystemParameters.WorkArea.Height);Width=Math.Min(Width,SystemParameters.WorkArea.Width);
        try { preferences=store.Load(); }catch(Exception ex){StatusLabel.Text=$"No se pudo cargar la configuración: {ex.Message} El archivo se conserva.";}
        DocumentTabs.ItemsSource=DocumentList.ItemsSource=Documents;ViewerToolbar.Visibility=Visibility.Collapsed;initialized=true;SyncProfileSelector();UpdateBatchSummary();
    }
    public async Task RestoreLastIdentityAsync()
    {
        if(preferences.LastIdentityFingerprint is not {} fingerprint)return;
        await WorkAsync("Recuperando el último certificado…",async()=>
        {
            var identity=await Task.Run(()=>identityLookup(fingerprint));
            if(identity is not null){SetIdentities([identity]);StatusLabel.Text="Último certificado utilizado preseleccionado.";}
            else StatusLabel.Text="El último certificado no está disponible. Selecciónalo o abre tu .p12/.pfx.";
        });
    }
    private async Task WorkAsync(string message,Func<Task> work)
    {
        if(busy)return;busy=true;Sidebar.IsEnabled=Workspace.IsEnabled=false;WorkingProgress.Visibility=Visibility.Visible;StatusLabel.Text=message;
        try { await work(); }
        catch(Exception ex){StatusLabel.Text="Operación no completada: "+ex.Message;MessageBox.Show(this,ex.Message,"FirmaPDF",MessageBoxButton.OK,MessageBoxImage.Error);}
        finally {busy=false;Sidebar.IsEnabled=Workspace.IsEnabled=true;WorkingProgress.Visibility=Visibility.Collapsed;UpdateBatchSummary();if(pendingOpen.Count>0&&!draining)_=Dispatcher.BeginInvoke(DrainOpenQueue);}
    }
    private async void OpenPdfClick(object sender,RoutedEventArgs e)
    {
        var picker=new OpenFileDialog{Filter="Documentos y firmas|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.odt;*.ods;*.xml;*.txt;*.csv;*.jpg;*.jpeg;*.png;*.zip;*.7z;*.p7s|Todos los archivos|*.*",Title="Abrir archivos",Multiselect=true};
        if(picker.ShowDialog(this)==true)await OpenDocumentsAsync(picker.FileNames);
    }
    public Task OpenDocumentAsync(string path)=>OpenDocumentsAsync([path]);
    public async Task OpenDocumentsAsync(IEnumerable<string> paths)
    {
        string[] requested=paths.ToArray();
        await WorkAsync("Abriendo documentos…",async()=>
        {
            var errors=new List<string>();
            foreach(string requestedPath in requested)
            {
                string? folder=null;
                try
                {
                    string path=LaunchDocument.ValidateAny(requestedPath);
                    var existing=Documents.FirstOrDefault(d=>string.Equals(d.OriginalPath,path,StringComparison.OrdinalIgnoreCase));
                    if(existing is not null){await ActivateAsync(existing);continue;}
                    folder=Path.Combine(session,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                    if(!BatchSigner.IsPdf(path))
                    {
                        long maximum=string.Equals(Path.GetExtension(path),".p7s",StringComparison.OrdinalIgnoreCase)?DetachedSignature.MaximumSignatureSize:DetachedSignature.MaximumFileSize;
                        if(new FileInfo(path).Length>maximum)throw new IOException($"El archivo supera {maximum/1024/1024} MiB, el límite para este tipo de firma.");
                        string snapshot=Path.Combine(folder,"original"+Path.GetExtension(path));await Task.Run(()=>File.Copy(path,snapshot));
                        var generic=new DocumentSession(path,snapshot,"",folder,preferences.SelectedDesign){HasExistingSignatures=string.Equals(Path.GetExtension(path),".p7s",StringComparison.OrdinalIgnoreCase)};
                        switchingTabs=true;try{Documents.Add(generic);}finally{switchingTabs=false;}
                        ActivateGeneric(generic);folder=null;continue;
                    }
                    string copy=Path.Combine(folder,"original.pdf");
                    string preview=await Task.Run(()=>{File.Copy(path,copy);return PdfEngine.PreparePreview(copy,folder);});
                    var rendered=await PdfPreview.RenderAsync(preview,0);
                    var following=rendered.Count>1?await PdfPreview.RenderAsync(preview,1):null;
                    var doc=new DocumentSession(path,copy,preview,folder,preferences.SelectedDesign){HasExistingSignatures=await Task.Run(()=>PdfEngine.HasSignatures(copy))};
                    switchingTabs=true;try{Documents.Add(doc);}finally{switchingTabs=false;}
                    Activate(doc,rendered,following);folder=null;
                }
                catch(Exception ex){if(folder is not null)DeleteTemporaryDirectory(folder);errors.Add(Path.GetFileName(requestedPath)+": "+ex.Message);}
            }
            if(errors.Count>0)throw new IOException("No se pudieron abrir algunos archivos:\n"+string.Join("\n",errors.Take(10)));
        });
    }
    public void QueueOpenDocuments(IEnumerable<string> paths)
    {
        foreach(string path in paths)pendingOpen.Enqueue(path);
        if(busy){StatusLabel.Text="Los archivos se abrirán cuando termine la operación actual.";return;}
        DrainOpenQueue();
    }
    private async void DrainOpenQueue()
    {
        if(draining||busy)return;draining=true;
        try {while(pendingOpen.Count>0&&!busy){string[] paths=pendingOpen.ToArray();pendingOpen.Clear();await OpenDocumentsAsync(paths);}}
        finally{draining=false;}
    }
    public static string[] DroppedPdfPaths(IEnumerable<string> paths)=>paths.Where(p=>string.Equals(Path.GetExtension(p),".pdf",StringComparison.OrdinalIgnoreCase)&&File.Exists(p)).ToArray();
    public static string[] DroppedFilePaths(IEnumerable<string> paths)=>paths.Where(File.Exists).ToArray();
    private void FilesDragOver(object sender,DragEventArgs e)
    {e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)&&e.Data.GetData(DataFormats.FileDrop) is string[] paths&&DroppedFilePaths(paths).Length>0?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
    private void FilesDrop(object sender,DragEventArgs e)
    {e.Handled=true;if(e.Data.GetData(DataFormats.FileDrop) is string[] paths){var files=DroppedFilePaths(paths);if(files.Length>0)QueueOpenDocuments(files);else StatusLabel.Text="Arrastra archivos para abrirlos.";}}
    private void ActivateGeneric(DocumentSession doc)
    {
        ActiveDocument=doc;switchingTabs=true;try{DocumentTabs.SelectedItem=DocumentList.SelectedItem=doc;}finally{switchingTabs=false;}
        firstRendered=followingRendered=null;PageImage.Source=SecondPageImage.Source=null;
        PageSurface.Visibility=SecondPageSurface.Visibility=NavigationPanel.Visibility=ViewerToolbar.Visibility=EmptyState.Visibility=SignatureCard.Visibility=Visibility.Collapsed;
        GenericState.Visibility=Visibility.Visible;GenericName.Text=Path.GetFileName(doc.OriginalPath);
        GenericDescription.Text=doc.IsSignatureFile?"Firma separada (.p7s). Pulsa Comprobar firmas y selecciona el archivo original si se solicita.":"Se creará una firma separada (.p7s) junto al archivo. Conserva ambos archivos para compartirlos y comprobar la firma. No hace falta poner un recuadro.";
        DocumentLabel.Text=GenericName.Text;StatusLabel.Text="";SyncProfileSelector();UpdateBatchSummary();
    }
    private void Activate(DocumentSession doc,RenderedPage rendered,RenderedPage? following,int? startIndex=null)
    {
        ActiveDocument=doc;switchingTabs=true;try{DocumentTabs.SelectedItem=DocumentList.SelectedItem=doc;}finally{switchingTabs=false;}
        ShowPage(rendered,following,startIndex);SyncProfileSelector();UpdateBatchSummary();
    }
    private async Task ActivateAsync(DocumentSession doc)
    {
        if(!doc.IsPdf){ActivateGeneric(doc);return;}
        var spread=await RenderSpreadAsync(doc.PreviewPath,doc.CurrentPage);
        Activate(doc,spread.First,spread.Following,spread.Start);
    }
    private static async Task<(RenderedPage First,RenderedPage? Following,int Start)> RenderSpreadAsync(string path,int index)
    {
        var current=await PdfPreview.RenderAsync(path,index);
        // Keep the preceding page available when the requested page is the last one.
        if(index>0&&index==current.Count-1)return(await PdfPreview.RenderAsync(path,index-1),current,index-1);
        return(current,index+1<current.Count?await PdfPreview.RenderAsync(path,index+1):null,index);
    }
    private async void DocumentListChanged(object sender,SelectionChangedEventArgs e)
    {if(!initialized||switchingTabs||busy)return;if(DocumentList.SelectedItem is DocumentSession doc&&!ReferenceEquals(doc,ActiveDocument))await SelectDocumentAsync(doc);}
    public async Task SelectDocumentAsync(DocumentSession doc)
    {if(!Documents.Contains(doc))throw new ArgumentException("El archivo no está abierto.");await WorkAsync("Preparando documento…",()=>ActivateAsync(doc));}
    private async void DocumentTabChanged(object sender,SelectionChangedEventArgs e)
    {if(!initialized||switchingTabs||busy||!ReferenceEquals(e.Source,DocumentTabs))return;if(DocumentTabs.SelectedItem is DocumentSession doc&&!ReferenceEquals(doc,ActiveDocument))await SelectDocumentAsync(doc);}
    private async void CloseTabClick(object sender,RoutedEventArgs e)
    {e.Handled=true;if(((Button)sender).Tag is DocumentSession doc)await CloseDocumentAsync(doc);}
    public async Task CloseDocumentAsync(DocumentSession doc)
    {
        if(busy||!Documents.Contains(doc))return;
        await WorkAsync("Cerrando documento…",async()=>
        {
            bool active=ReferenceEquals(doc,ActiveDocument);int index=Documents.IndexOf(doc);
            if(active)
            {
                var others=Documents.Where(d=>!ReferenceEquals(d,doc)).ToArray();
                if(others.Length>0)await ActivateAsync(others[Math.Min(index,others.Length-1)]);
                else{ActiveDocument=null;firstRendered=followingRendered=null;PageImage.Source=SecondPageImage.Source=null;PageSurface.Visibility=SecondPageSurface.Visibility=NavigationPanel.Visibility=ViewerToolbar.Visibility=GenericState.Visibility=Visibility.Collapsed;EmptyState.Visibility=SignatureCard.Visibility=Visibility.Visible;DocumentLabel.Text="";PageLabel.Text="Sin documento";SyncProfileSelector();}
            }
            switchingTabs=true;try{Documents.Remove(doc);}finally{switchingTabs=false;}
            DeleteTemporaryDirectory(doc.DirectoryPath);UpdateBatchSummary();
        });
    }
    private void ShowPage(RenderedPage rendered,RenderedPage? following,int? startIndex=null)
    {
        if(ActiveDocument is not {} doc)return;
        GenericState.Visibility=Visibility.Collapsed;ViewerToolbar.Visibility=SignatureCard.Visibility=Visibility.Visible;
        spreadStart=startIndex??page;firstRendered=rendered;followingRendered=following;
        var active=page==spreadStart+1&&following is not null?following:rendered;
        PageImage.Source=active.Image;doc.PageCount=rendered.Count;doc.PageWidth=active.Width;doc.PageHeight=active.Height;
        PageSurface.Visibility=Visibility.Visible;EmptyState.Visibility=Visibility.Collapsed;DocumentLabel.Text=Path.GetFileName(doc.OriginalPath);
        NavigationPanel.Visibility=Visibility.Visible;
        FitPage();UpdateZoom();DrawBox();Viewport.ScrollToTop();Viewport.ScrollToLeftEnd();
        StatusLabel.Text=doc.SignedOutput is {} path?"Copia firmada: "+path:"";
    }
    public async Task NavigateToPageAsync(int index)
    {if(displayInput is null||index<0||index>=pages)return;await WorkAsync("Preparando página…",async()=>{var spread=await RenderSpreadAsync(displayInput,index);page=index;ShowPage(spread.First,spread.Following,spread.Start);});}
    private async void PreviousClick(object sender,RoutedEventArgs e)=>await NavigateToPageAsync(Math.Max(0,(TwoPageMode?spreadStart:page)-(TwoPageMode?2:1)));
    private async void NextClick(object sender,RoutedEventArgs e)=>await NavigateToPageAsync((TwoPageMode?spreadStart:page)+(TwoPageMode?2:1));
    private void SetIdentities(IEnumerable<CertificateIdentity> values)
    {
        ForgetIdentities(); identities.AddRange(values); CertificateCombo.ItemsSource = identities.ToArray();
        int remembered = identities.FindIndex(i => string.Equals(i.Fingerprint, preferences.LastIdentityFingerprint, StringComparison.OrdinalIgnoreCase));
        CertificateCombo.SelectedIndex = identities.Count == 0 ? -1 : Math.Max(0, remembered);
    }
    private async void StoreClick(object sender, RoutedEventArgs e) => await WorkAsync("Leyendo certificados de Windows…", async () =>
    {
        var values = await Task.Run(CertificateIdentity.FromWindowsStore); SetIdentities(values); StatusLabel.Text = "Selecciona la identidad que quieres utilizar.";
    });

    private async void ImportClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Certificado con clave privada|*.p12;*.pfx", Title = "Abrir certificado" };
        if (picker.ShowDialog(this) != true) return;
        var password = new PasswordBox { Margin = new Thickness(20, 12, 20, 8), MinWidth = 300 };
        var ok = new Button { Content = "Abrir", IsDefault = true, Margin = new Thickness(20, 8, 20, 16) };
        var content = new StackPanel(); content.Children.Add(new TextBlock { Text = "Contraseña del certificado · solo esta sesión", Margin = new Thickness(20, 16, 20, 0) }); content.Children.Add(password); content.Children.Add(ok);
        var dialog = new Window { Owner = this, Title = "Abrir .p12 / .pfx", Content = content, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true) { password.Clear(); return; }
        using var secure = password.SecurePassword;
        char[] chars = new char[secure.Length]; IntPtr pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
        try { Marshal.Copy(pointer, chars, 0, chars.Length); } finally { Marshal.ZeroFreeGlobalAllocUnicode(pointer); password.Clear(); }
        await WorkAsync("Abriendo certificado…", async () =>
        {
            try { var values = await Task.Run(() => CertificateIdentity.FromPkcs12(picker.FileName, chars)); SetIdentities(values); StatusLabel.Text = "Certificado abierto para esta sesión."; }
            finally { Array.Clear(chars); }
        });
    }

    private async void IdentityClick(object sender,RoutedEventArgs e)
    {
        if(Identity is not {} identity)return;
        await WorkAsync("Consultando confianza y revocación del certificado…",async()=>
        {
            var certificates=new System.Security.Cryptography.X509Certificates.X509Certificate2Collection();
            try
            {
                foreach(var bytes in identity.GetPublicChain())certificates.Add(System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(bytes));
                var result=await Task.Run(()=>CertificateValidation.Check(identity.Certificate,certificates));
                ShowReport("Comprobación del certificado",identity.Details+"\n\n"+result+"\n\nEstado actual según Windows; no acredita cualificación ni validez histórica. Las consultas de revocación pueden enviar datos públicos del certificado a su emisor.");
            }
            finally{foreach(var certificate in certificates)certificate.Dispose();}
        });
    }
    private void ShowReport(string title,string text)
    {
        var panel=new DockPanel{Margin=new Thickness(20)};
        var close=new Button{Content="Cerrar",IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        DockPanel.SetDock(close,Dock.Bottom);panel.Children.Add(close);
        panel.Children.Add(new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,BorderThickness=new Thickness(0),Background=System.Windows.Media.Brushes.Transparent});
        var report=new Window{Owner=this,Title=title,Width=760,Height=Math.Min(700,SystemParameters.WorkArea.Height),MinWidth=520,MinHeight=400,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=panel,Background=System.Windows.Media.Brushes.White};
        close.Click+=(_,_)=>report.Close();report.ShowDialog();
    }
    private void CertificateChanged(object sender, SelectionChangedEventArgs e) { if (initialized) UpdateDesignPreview(); }
    private async void ForgetClick(object sender, RoutedEventArgs e)
    {
        ForgetIdentities();
        var next = preferences with { LastIdentityFingerprint = null };
        await WorkAsync("Olvidando certificado…", async () => { await Task.Run(() => preferencesStore.Save(next)); preferences = next; StatusLabel.Text = "Certificado olvidado. No se preseleccionará al iniciar."; });
    }
    private void ForgetIdentities() { CertificateCombo.ItemsSource = null; foreach (var identity in identities) identity.Dispose(); identities.Clear(); }


    private async void SignClick(object sender,RoutedEventArgs e)
    {try{await SignAllAsync();}catch(Exception ex){StatusLabel.Text=ex.Message;MessageBox.Show(this,ex.Message,"Preparar firma",MessageBoxButton.OK,MessageBoxImage.Information);}}
    public async Task SignAllAsync()
    {
        var pending=Documents.Where(d=>d.SignedOutput is null&&!d.IsSignatureFile).ToArray();
        if(pending.Length==0)throw new ArgumentException(Documents.Count==0?"Abre uno o varios archivos.":"No hay documentos pendientes de firma. Los .p7s se abren para comprobarlos.");
        var missing=pending.Where(d=>!d.Ready).ToArray();
        if(missing.Length>0)throw new ArgumentException("Dibuja un recuadro en cada PDF antes de firmar:\n"+string.Join("\n",missing.Select(d=>Path.GetFileName(d.OriginalPath))));
        await SignJobsAsync(pending.Select(d=>(Document:d,Job:d.Job())).ToArray());
    }
    public Task SignDocumentAsync(string destination,Placement placement)
    {
        if(ActiveDocument is not {} doc)throw new InvalidOperationException("Abre un PDF.");
        return SignJobsAsync([(doc,new SigningJob(doc.InputPath,destination,placement,doc.Preset.Design))]);
    }
    private async Task SignJobsAsync(IReadOnlyList<(DocumentSession Document,SigningJob Job)> requests)
    {
        if(Identity is not {} identity)throw new InvalidOperationException("Elige un certificado para firmar los PDF.");
        // Validate the same resolved appearance used by the PDF writer before any batch starts.
        foreach(var request in requests)
        {
            var document=request.Document;
            if(!document.IsPdf)continue;
            var placement=request.Job.Placement??throw new ArgumentException("Falta el recuadro del PDF.");
            double width=placement.Width*document.SelectionPageWidth;
            double height=placement.Height*document.SelectionPageHeight;
            // Direct API callers can supply placement without an interactive selection.
            if(document.Selection is null) { width=placement.Width*document.PageWidth; height=placement.Height*document.PageHeight; }
            SignatureArt.Layout((float)width,(float)height,request.Job.Design.Resolve(identity.Name,IssuerName,DateTimeOffset.Now),request.Job.Design.HasImage,request.Job.Design.TextSize);
        }
        var jobs=requests.Select(r=>r.Job).ToArray();BatchSigner.CheckDestinations(jobs);
        bool timestamp=preferences.UseTimestamp;
        if(preferences.ConfirmBeforeSigning)
        {
            string names=string.Join("\n",requests.Take(15).Select(r=>Path.GetFileName(r.Document.OriginalPath)));
            string timeNotice=timestamp?"Se enviará solo la huella de cada firma a DigiCert para obtener su sello de tiempo.":"Sin sello de tiempo. La fecha procede del ordenador.";
            var notice=new SigningNotice(this,"Firmar documentos",$"Vas a firmar {jobs.Length} archivos con:\n\n{identity.Details}\n\n{names}\n\nLos PDF se guardan como copias firmadas. Los demás archivos generan firmas separadas .p7s; conserva el original junto a su firma.\n{timeNotice}",true);
            if(notice.ShowDialog()!=true)return;
            if(notice.DoNotShowAgain){preferences=preferences with{ConfirmBeforeSigning=false};try{preferencesStore.Save(preferences);}catch(Exception ex){StatusLabel.Text="No se pudo guardar esta preferencia: "+ex.Message;}}
        }
        await WorkAsync("Firmando documentos…",async()=>
        {
            var progress=new Progress<(int Current,int Total)>(p=>StatusLabel.Text=$"Firmando y verificando archivo {p.Current} de {p.Total}…");
            IReadOnlyList<SignedDocument> result;
            void Mark(IEnumerable<SignedDocument> values)
            {foreach(var value in values)requests.First(r=>ReferenceEquals(r.Job,value.Job)).Document.MarkSigned(value.Job.Destination);UpdateBatchSummary();}
            try{result=await Task.Run(()=>BatchSigner.Sign(jobs,identity,timestamp?TimestampClient.DigiCert():null,(a,b)=>((IProgress<(int,int)>)progress).Report((a,b))));}
            catch(BatchSigningException ex){Mark(ex.Completed);throw;}
            Mark(result);preferences=preferences with{LastIdentityFingerprint=identity.Fingerprint};
            string summary=result.Count==1?(BatchSigner.IsPdf(result[0].Job.Source)?"PDF firmado y guardado en ":"Firma separada guardada en ")+result[0].Job.Destination:$"{result.Count} firmas guardadas junto a sus originales (PDF firmados o archivos .p7s).";
            string date=result.Last().Verification.LatestTimestamp is {} time?$"Hora del último sello: {time.ToLocalTime():dd/MM/yyyy HH:mm:ss zzz}":"Sin sello de tiempo. Fecha declarada del ordenador.";
            StatusLabel.Text=summary+"\n"+date;
            if(preferences.ShowSuccessDialog)
            {var notice=new SigningNotice(this,"PDF firmados",summary+"\n\n"+date+"\n\n"+PdfEngine.VerificationNotice,false);notice.ShowDialog();if(notice.DoNotShowAgain)preferences=preferences with{ShowSuccessDialog=false};}
            try{var saved=preferences;await Task.Run(()=>preferencesStore.Save(saved));}catch(Exception ex){StatusLabel.Text+="\nLas copias están firmadas, pero no se pudieron guardar las preferencias: "+ex.Message;}
        });
    }
    private async void VerifyClick(object sender,RoutedEventArgs e)
    {
        if(ActiveDocument is not {} doc)return;
        string path=doc.SignedOutput??doc.InputPath;string? original=null;
        if(!doc.IsPdf)
        {
            if(!doc.IsSignatureFile)original=doc.InputPath;
            else
            {
                string adjacent=doc.OriginalPath[..^4];
                if(File.Exists(adjacent))original=adjacent;
                else{var picker=new OpenFileDialog{Title="Selecciona el archivo original de esta firma .p7s",Filter="Todos los archivos|*.*"};if(picker.ShowDialog(this)!=true)return;original=picker.FileName;}
            }
        }
        await WorkAsync("Comprobando integridad, confianza y revocación con las autoridades…",async()=>
        {var result=await Task.Run(()=>doc.IsPdf?SignatureInspector.InspectPdf(path):SignatureInspector.InspectDetached(original!,path));StatusLabel.Text="Comprobación terminada. El informe distingue integridad, confianza y revocación.";ShowReport("Comprobación de firmas",result.ToString());});
    }
    private void ResetBoxClick(object sender,RoutedEventArgs e){if(input is not null){box=[.1,.72,.55,.14];DrawBox();UpdateBatchSummary();}}
    private void OpenSignedClick(object sender,RoutedEventArgs e)
    {try{if(ActiveDocument?.SignedOutput is {} path){if(ActiveDocument.IsPdf)Process.Start(new ProcessStartInfo(path){UseShellExecute=true});else{var start=new ProcessStartInfo("explorer.exe"){UseShellExecute=false};start.ArgumentList.Add("/select,");start.ArgumentList.Add(path);Process.Start(start);}}}catch(Exception ex){MessageBox.Show(this,ex.Message,"No se pudo abrir la copia");}}
    private async void SettingsClick(object sender,RoutedEventArgs e)
    {
        var current=preferences.WithDesigns(preferences.Designs,ActiveDocument?.Preset.Id??preferences.SelectedDesign.Id);
        var settings=new SettingsWindow(current,Identity?.Name??"Tu nombre y apellidos",IssuerName){Owner=this};
        if(settings.ShowDialog()==true&&settings.Result is {} next)await ApplyPreferencesAsync(next);
    }
    public async Task ApplyPreferencesAsync(UserPreferences next)
    {
        await WorkAsync("Guardando configuración…",async()=>
        {
            await Task.Run(()=>preferencesStore.Save(next));preferences=next;
            foreach(var doc in Documents)doc.SetPreset(next.Designs.FirstOrDefault(p=>p.Id==doc.Preset.Id)??next.SelectedDesign);
            ActiveDocument?.SetPreset(next.SelectedDesign);SyncProfileSelector();StatusLabel.Text="Configuración y firmas personalizadas guardadas.";
        });
    }
    private async void SignatureChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!initialized||busy||SignatureCombo.SelectedItem is not SignaturePreset preset)return;
        await SelectSignatureAsync(preset.Id);
    }
    public async Task SelectSignatureAsync(string id)
    {
        var next=preferences.WithDesigns(preferences.Designs,id);var preset=next.SelectedDesign;
        await WorkAsync("Seleccionando firma…",async()=>{await Task.Run(()=>preferencesStore.Save(next));preferences=next;ActiveDocument?.SetPreset(preset);SyncProfileSelector();StatusLabel.Text="Firma seleccionada para este PDF: "+preset.Name;});
    }
    private void SyncProfileSelector()
    {
        bool ready=initialized;initialized=false;
        SignatureCombo.ItemsSource=preferences.Designs;string id=ActiveDocument?.Preset.Id??preferences.SelectedDesign.Id;
        SignatureCombo.SelectedItem=preferences.Designs.FirstOrDefault(p=>p.Id==id)??preferences.SelectedDesign;
        initialized=ready;UpdateDesignPreview();
    }
    private void UpdateDesignPreview()
    {
        var preset=ActiveDocument?.Preset??preferences.SelectedDesign;
        ProfilePreview.Update(preset.Design,Identity?.Name??"Tu nombre y apellidos",IssuerName);
        DesignSummary.Text=ActiveDocument?.Ready==true?$"Preparada en la página {ActiveDocument.SelectionPage+1}":"Elige un diseño y dibuja dónde colocarlo.";DrawBox();
    }
    private void UpdateBatchSummary()
    {
        DocumentTabs.Visibility=Documents.Count==0?Visibility.Collapsed:Visibility.Visible;
        int signed=Documents.Count(d=>d.SignedOutput is not null),ready=Documents.Count(d=>d.SignedOutput is null&&d.Ready);
        BatchSummary.Text=Documents.Count==0?"Abre uno o varios PDF para empezar.":$"{Documents.Count} archivos abiertos · {ready} preparados · {signed} firmados";
        LastSignedButton.Visibility=ActiveDocument?.SignedOutput is null?Visibility.Collapsed:Visibility.Visible;LastSignedButton.IsEnabled=ActiveDocument?.SignedOutput is not null;
        LastSignedButton.Content=ActiveDocument?.IsPdf==false?"Mostrar firma guardada":"Abrir copia firmada";
        VerifyButton.Visibility=ActiveDocument is {} active&&(active.HasExistingSignatures||active.SignedOutput is not null)?Visibility.Visible:Visibility.Collapsed;
    }
    private void ZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (PageImage is not null) UpdateZoom(); }
    private void ViewportSizeChanged(object sender,SizeChangedEventArgs e) { if(PageImage is not null&&ZoomSlider is not null) { if(ZoomSlider.Value==100)FitPage();else UpdateZoom(); } }
    private void FitClick(object sender, RoutedEventArgs e) => FitPage();
    private void FitPage()
    {
        if (LeftRendered is not {} left) return;
        double totalWidth=left.Width+(TwoPageMode?followingRendered!.Width:0);
        double totalHeight=Math.Max(left.Height,TwoPageMode?followingRendered!.Height:0);
        double scale=Math.Max(.05,Math.Min((Viewport.ActualWidth-56-(TwoPageMode?24:0))/totalWidth,(Viewport.ActualHeight-56)/totalHeight));
        baseWidth=left.Width*scale;
        ZoomSlider.Value = 100; UpdateZoom();
    }
    private void UpdateZoom()
    {
        if (LeftRendered is not {} left) return;
        PageImage.Source=left.Image;
        PageSurface.Width = baseWidth * ZoomSlider.Value/100;
        PageSurface.Height = PageSurface.Width*left.Height/left.Width;
        SecondPageSurface.Visibility=TwoPageMode?Visibility.Visible:Visibility.Collapsed;
        if(TwoPageMode)
        {
            SecondPageImage.Source=followingRendered!.Image;
            SecondPageSurface.Width=PageSurface.Width*followingRendered.Width/left.Width;
            SecondPageSurface.Height=PageSurface.Width*followingRendered.Height/left.Width;
        }
        PageLabel.Text=TwoPageMode?$"Páginas {spreadStart+1}–{spreadStart+2} de {pages}":$"Página {page+1} de {pages}";
        DrawBox();
    }
    private void SurfaceSizeChanged(object sender, SizeChangedEventArgs e) => DrawBox();
    private Point Normalized(MouseEventArgs e) { var surface=dragSurface??PageSurface;var p = e.GetPosition(surface); return new(Math.Clamp(p.X/surface.ActualWidth, 0, 1), Math.Clamp(p.Y/surface.ActualHeight, 0, 1)); }
    public void SelectVisiblePage(bool right)
    {
        if(ActiveDocument is not {} doc)return;
        if(right&&!TwoPageMode)throw new InvalidOperationException("No hay una segunda página visible.");
        var rendered=right?followingRendered:LeftRendered;
        if(rendered is null)return;
        page=right?spreadStart+1:LeftPageIndex;doc.PageWidth=rendered.Width;doc.PageHeight=rendered.Height;
        DrawBox();
    }
    private void SelectionDown(object sender, MouseButtonEventArgs e)
    {
        if (busy || input is null) return;
        dragSurface=(Grid)sender;SelectVisiblePage(ReferenceEquals(dragSurface,SecondPageSurface));
        start = Normalized(e); before = box?.ToArray(); dragMode = 0;
        if (box is not null && Math.Abs(start.X-box[0]-box[2])*dragSurface.ActualWidth < 18 && Math.Abs(start.Y-box[1]-box[3])*dragSurface.ActualHeight < 18) dragMode = 2;
        else if (box is not null && start.X >= box[0] && start.X <= box[0]+box[2] && start.Y >= box[1] && start.Y <= box[1]+box[3]) dragMode = 1;
        dragSurface.CaptureMouse(); e.Handled = true;
    }
    private void SelectionMove(object sender, MouseEventArgs e)
    {
        if (dragSurface?.IsMouseCaptured!=true || e.LeftButton != MouseButtonState.Pressed) return;
        var p = Normalized(e);
        if (dragMode == 1 && before is not null) box = [Math.Clamp(before[0]+p.X-start.X, 0, 1-before[2]), Math.Clamp(before[1]+p.Y-start.Y, 0, 1-before[3]), before[2], before[3]];
        else if (dragMode == 2 && before is not null) box = [before[0], before[1], Math.Clamp(p.X-before[0], 0, 1-before[0]), Math.Clamp(p.Y-before[1], 0, 1-before[1])];
        else box = [Math.Min(p.X,start.X), Math.Min(p.Y,start.Y), Math.Abs(p.X-start.X), Math.Abs(p.Y-start.Y)];
        DrawBox();
    }
    private void SelectionUp(object sender, MouseButtonEventArgs e) { dragSurface?.ReleaseMouseCapture();dragSurface=null; if (box is not null && (box[2] <= 0 || box[3] <= 0)) box = null; DrawBox(); }
    private void DrawBox()
    {
        if (SelectionBox is null) return;
        if(BatchSummary is not null)UpdateBatchSummary();
        DrawPageBox(PageSurface,SelectionBox,ResizeHandle,SelectedDesignPreview,LeftPageIndex);
        DrawPageBox(SecondPageSurface,SecondSelectionBox,SecondResizeHandle,SecondDesignPreview,spreadStart+1);
        DesignSummary.Foreground=System.Windows.Media.Brushes.SlateGray;
        DesignSummary.Visibility=Visibility.Collapsed;
        var selection=ActiveDocument?.Selection;
        if (selection is null) { DesignSummary.Text="";return; }
        try
        {
            var design=ActiveDocument!.Preset.Design;
            var layout=SignatureArt.Layout((float)(selection[2]*ActiveDocument.SelectionPageWidth),(float)(selection[3]*ActiveDocument.SelectionPageHeight),design.Resolve(Identity?.Name??"Tu nombre y apellidos",IssuerName,DateTimeOffset.Now),design.HasImage,design.TextSize);
            bool reduced=layout.Lines.Count>0&&layout.FontSize<design.TextSize;
            DesignSummary.Text=reduced?$"Texto ajustado a {layout.FontSize:0.##} pt para que quepa. Puedes ampliar el recuadro para leerlo mejor.":$"Preparada en la página {page+1}";
            if(reduced){DesignSummary.Foreground=System.Windows.Media.Brushes.DarkGoldenrod;DesignSummary.Visibility=Visibility.Visible;}
        }
        catch(ArgumentException ex) { DesignSummary.Text=ex.Message;DesignSummary.Foreground=System.Windows.Media.Brushes.Firebrick;DesignSummary.Visibility=Visibility.Visible; }
    }
    private void DrawPageBox(Grid surface,Border border,FrameworkElement handle,SignaturePreview preview,int index)
    {
        var selection=ActiveDocument?.Selection;
        bool show=selection is not null&&ActiveDocument!.SelectionPage==index&&surface.Visibility==Visibility.Visible;
        border.Visibility=handle.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        if(!show)return;
        var s=selection!;
        double w = surface.Width, h = surface.Height;
        if (!double.IsFinite(w) || !double.IsFinite(h)) return;
        Canvas.SetLeft(border,s[0]*w);Canvas.SetTop(border,s[1]*h);border.Width=s[2]*w;border.Height=s[3]*h;
        preview.Update(ActiveDocument!.Preset.Design,Identity?.Name??"Selecciona un certificado",IssuerName,s[2]*ActiveDocument.SelectionPageWidth,s[3]*ActiveDocument.SelectionPageHeight);
        Canvas.SetLeft(handle,(s[0]+s[2])*w-5);Canvas.SetTop(handle,(s[1]+s[3])*h-5);
    }

    private void DeleteTemporaryDirectory(string directory)
    {
        string root=Path.GetFullPath(session)+Path.DirectorySeparatorChar,target=Path.GetFullPath(directory);
        if(!target.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Ruta temporal fuera de la sesión.");
        try{if(Directory.Exists(target))Directory.Delete(target,true);}catch(IOException){}
    }
    private void WindowClosing(object? sender,CancelEventArgs e)
    {
        if(busy){e.Cancel=true;StatusLabel.Text="Espera a que termine la operación antes de cerrar.";return;}
        ForgetIdentities();
        string target=Path.GetFullPath(session),root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"FirmaPDF"))+Path.DirectorySeparatorChar;
        if(target.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Guid.TryParseExact(Path.GetFileName(target),"N",out _))
            try{if(Directory.Exists(target))Directory.Delete(target,true);}catch(IOException){}
    }
}
