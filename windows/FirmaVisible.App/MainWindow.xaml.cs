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
        DocumentTabs.ItemsSource=Documents;initialized=true;SyncProfileSelector();UpdateBatchSummary();
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
        var picker=new OpenFileDialog{Filter="Documento PDF|*.pdf",Title="Abrir PDF",Multiselect=true};
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
                    string path=LaunchDocument.Validate(requestedPath);
                    var existing=Documents.FirstOrDefault(d=>string.Equals(d.OriginalPath,path,StringComparison.OrdinalIgnoreCase));
                    if(existing is not null){await ActivateAsync(existing);continue;}
                    folder=Path.Combine(session,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                    string copy=Path.Combine(folder,"original.pdf");
                    string preview=await Task.Run(()=>{File.Copy(path,copy);return PdfEngine.PreparePreview(copy,folder);});
                    var rendered=await PdfPreview.RenderAsync(preview,0);
                    var doc=new DocumentSession(path,copy,preview,folder,preferences.SelectedDesign);
                    switchingTabs=true;try{Documents.Add(doc);}finally{switchingTabs=false;}
                    Activate(doc,rendered);folder=null;
                }
                catch(Exception ex){if(folder is not null)DeleteTemporaryDirectory(folder);errors.Add(Path.GetFileName(requestedPath)+": "+ex.Message);}
            }
            if(errors.Count>0)throw new IOException("No se pudieron abrir algunos archivos:\n"+string.Join("\n",errors.Take(10)));
        });
    }
    public void QueueOpenDocuments(IEnumerable<string> paths)
    {
        foreach(string path in paths)pendingOpen.Enqueue(path);
        if(busy){StatusLabel.Text="Los PDF se abrirán cuando termine la operación actual.";return;}
        DrainOpenQueue();
    }
    private async void DrainOpenQueue()
    {
        if(draining||busy)return;draining=true;
        try {while(pendingOpen.Count>0&&!busy){string[] paths=pendingOpen.ToArray();pendingOpen.Clear();await OpenDocumentsAsync(paths);}}
        finally{draining=false;}
    }
    public static string[] DroppedPdfPaths(IEnumerable<string> paths)=>paths.Where(p=>string.Equals(Path.GetExtension(p),".pdf",StringComparison.OrdinalIgnoreCase)&&File.Exists(p)).ToArray();
    private void FilesDragOver(object sender,DragEventArgs e)
    {e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)&&e.Data.GetData(DataFormats.FileDrop) is string[] paths&&DroppedPdfPaths(paths).Length>0?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
    private void FilesDrop(object sender,DragEventArgs e)
    {e.Handled=true;if(e.Data.GetData(DataFormats.FileDrop) is string[] paths){var pdfs=DroppedPdfPaths(paths);if(pdfs.Length>0)QueueOpenDocuments(pdfs);else StatusLabel.Text="Arrastra archivos PDF para abrirlos.";}}
    private void Activate(DocumentSession doc,RenderedPage rendered)
    {
        ActiveDocument=doc;switchingTabs=true;try{DocumentTabs.SelectedItem=doc;}finally{switchingTabs=false;}
        ShowPage(rendered);SyncProfileSelector();UpdateBatchSummary();
    }
    private async Task ActivateAsync(DocumentSession doc)=>Activate(doc,await PdfPreview.RenderAsync(doc.PreviewPath,doc.CurrentPage));
    public async Task SelectDocumentAsync(DocumentSession doc)
    {if(!Documents.Contains(doc))throw new ArgumentException("El PDF no está abierto.");await WorkAsync("Preparando documento…",()=>ActivateAsync(doc));}
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
                else{ActiveDocument=null;PageImage.Source=null;PageSurface.Visibility=Visibility.Collapsed;EmptyState.Visibility=Visibility.Visible;DocumentLabel.Text="Abre o arrastra tus PDF";PageLabel.Text="Sin documento";SyncProfileSelector();}
            }
            switchingTabs=true;try{Documents.Remove(doc);}finally{switchingTabs=false;}
            DeleteTemporaryDirectory(doc.DirectoryPath);UpdateBatchSummary();
        });
    }
    private void ShowPage(RenderedPage rendered)
    {
        if(ActiveDocument is not {} doc)return;
        PageImage.Source=rendered.Image;doc.PageCount=rendered.Count;doc.PageWidth=rendered.Width;doc.PageHeight=rendered.Height;
        PageSurface.Visibility=Visibility.Visible;EmptyState.Visibility=Visibility.Collapsed;DocumentLabel.Text=Path.GetFileName(doc.OriginalPath);
        FitPage();UpdateZoom();DrawBox();Viewport.ScrollToTop();Viewport.ScrollToLeftEnd();PageLabel.Text=$"Página {page+1} de {pages}";
        StatusLabel.Text=doc.SignedOutput is {} path?"Copia firmada: "+path:"Dibuja el recuadro. Después pulsa Firmar para procesar los PDF preparados.";
    }
    public async Task NavigateToPageAsync(int index)
    {if(displayInput is null||index<0||index>=pages)return;await WorkAsync("Preparando página…",async()=>{var rendered=await PdfPreview.RenderAsync(displayInput,index);page=index;ShowPage(rendered);});}
    private async void PreviousClick(object sender,RoutedEventArgs e)=>await NavigateToPageAsync(page-1);
    private async void NextClick(object sender,RoutedEventArgs e)=>await NavigateToPageAsync(page+1);
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

    private void IdentityClick(object sender, RoutedEventArgs e) { if (Identity is { } identity) MessageBox.Show(this, identity.Details, "Identidad seleccionada"); }
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
        var pending=Documents.Where(d=>d.SignedOutput is null).ToArray();
        if(pending.Length==0)throw new ArgumentException(Documents.Count==0?"Abre uno o varios PDF.":"Todos los PDF abiertos ya tienen una copia firmada.");
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
            double width=request.Job.Placement.Width*document.SelectionPageWidth;
            double height=request.Job.Placement.Height*document.SelectionPageHeight;
            // Direct API callers can supply placement without an interactive selection.
            if(document.Selection is null) { width=request.Job.Placement.Width*document.PageWidth; height=request.Job.Placement.Height*document.PageHeight; }
            SignatureArt.Layout((float)width,(float)height,request.Job.Design.Resolve(identity.Name,IssuerName,DateTimeOffset.Now),request.Job.Design.HasImage,request.Job.Design.TextSize);
        }
        var jobs=requests.Select(r=>r.Job).ToArray();BatchSigner.CheckDestinations(jobs);
        bool timestamp=preferences.UseTimestamp;
        if(preferences.ConfirmBeforeSigning)
        {
            string names=string.Join("\n",requests.Take(15).Select(r=>Path.GetFileName(r.Document.OriginalPath)));
            string timeNotice=timestamp?"Se enviará solo la huella de cada firma a DigiCert para obtener su sello de tiempo.":"Sin sello de tiempo. La fecha procede del ordenador.";
            var notice=new SigningNotice(this,"Firmar documentos",$"Vas a firmar {jobs.Length} PDF con:\n\n{identity.Details}\n\n{names}\n\nSe crearán copias sin modificar los originales.\n{timeNotice}",true);
            if(notice.ShowDialog()!=true)return;
            if(notice.DoNotShowAgain){preferences=preferences with{ConfirmBeforeSigning=false};try{preferencesStore.Save(preferences);}catch(Exception ex){StatusLabel.Text="No se pudo guardar esta preferencia: "+ex.Message;}}
        }
        await WorkAsync("Firmando documentos…",async()=>
        {
            var progress=new Progress<(int Current,int Total)>(p=>StatusLabel.Text=$"Firmando y verificando PDF {p.Current} de {p.Total}…");
            IReadOnlyList<SignedDocument> result;
            void Mark(IEnumerable<SignedDocument> values)
            {foreach(var value in values)requests.First(r=>ReferenceEquals(r.Job,value.Job)).Document.MarkSigned(value.Job.Destination);UpdateBatchSummary();}
            try{result=await Task.Run(()=>BatchSigner.Sign(jobs,identity,timestamp?TimestampClient.DigiCert():null,(a,b)=>((IProgress<(int,int)>)progress).Report((a,b))));}
            catch(BatchSigningException ex){Mark(ex.Completed);throw;}
            Mark(result);preferences=preferences with{LastIdentityFingerprint=identity.Fingerprint};
            string summary=result.Count==1?"PDF firmado y guardado en "+result[0].Job.Destination:$"{result.Count} PDF firmados. Copias _firmado guardadas junto a los originales.";
            string date=result.Last().Verification.LatestTimestamp is {} time?$"Hora del último sello: {time.ToLocalTime():dd/MM/yyyy HH:mm:ss zzz}":"Sin sello de tiempo. Fecha declarada del ordenador.";
            StatusLabel.Text=summary+"\n"+date;
            if(preferences.ShowSuccessDialog)
            {var notice=new SigningNotice(this,"PDF firmados",summary+"\n\n"+date+"\n\n"+PdfEngine.VerificationNotice,false);notice.ShowDialog();if(notice.DoNotShowAgain)preferences=preferences with{ShowSuccessDialog=false};}
            try{var saved=preferences;await Task.Run(()=>preferencesStore.Save(saved));}catch(Exception ex){StatusLabel.Text+="\nLas copias están firmadas, pero no se pudieron guardar las preferencias: "+ex.Message;}
        });
    }
    private async void VerifyClick(object sender,RoutedEventArgs e)
    {if(input is null)return;await WorkAsync("Comprobando firmas…",async()=>{var result=await Task.Run(()=>PdfEngine.Verify(input));StatusLabel.Text="Comprobación terminada.";MessageBox.Show(this,result.ToString(),"Integridad de las firmas");});}
    private void ResetBoxClick(object sender,RoutedEventArgs e){if(input is not null){box=[.1,.72,.55,.14];DrawBox();UpdateBatchSummary();}}
    private void OpenSignedClick(object sender,RoutedEventArgs e)
    {try{if(ActiveDocument?.SignedOutput is {} path)Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,ex.Message,"No se pudo abrir el PDF");}}
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
        BatchSummary.Text=Documents.Count==0?"Abre uno o varios PDF para empezar.":$"{Documents.Count} PDF abiertos · {ready} preparados · {signed} firmados";
        LastSignedButton.Visibility=ActiveDocument?.SignedOutput is null?Visibility.Collapsed:Visibility.Visible;LastSignedButton.IsEnabled=ActiveDocument?.SignedOutput is not null;
    }
    private void ZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (PageImage is not null) UpdateZoom(); }
    private void ViewportSizeChanged(object sender,SizeChangedEventArgs e) { if(PageImage is not null&&ZoomSlider is not null&&ZoomSlider.Value==100)FitPage(); }
    private void FitClick(object sender, RoutedEventArgs e) => FitPage();
    private void FitPage()
    {
        if (PageImage.Source is not System.Windows.Media.Imaging.BitmapSource image) return;
        baseWidth = Math.Max(100, Math.Min(Viewport.ActualWidth - 56, (Viewport.ActualHeight - 56) * image.PixelWidth / image.PixelHeight));
        ZoomSlider.Value = 100; UpdateZoom();
    }
    private void UpdateZoom()
    {
        if (PageImage.Source is not System.Windows.Media.Imaging.BitmapSource bitmap) return;
        PageSurface.Width = baseWidth * ZoomSlider.Value/100;
        PageSurface.Height = PageSurface.Width * bitmap.PixelHeight/bitmap.PixelWidth;
        DrawBox();
    }
    private void SurfaceSizeChanged(object sender, SizeChangedEventArgs e) => DrawBox();
    private Point Normalized(MouseEventArgs e) { var p = e.GetPosition(PageSurface); return new(Math.Clamp(p.X/PageSurface.ActualWidth, 0, 1), Math.Clamp(p.Y/PageSurface.ActualHeight, 0, 1)); }
    private void SelectionDown(object sender, MouseButtonEventArgs e)
    {
        if (busy || input is null) return;
        start = Normalized(e); before = box?.ToArray(); dragMode = 0;
        if (box is not null && Math.Abs(start.X-box[0]-box[2])*PageSurface.ActualWidth < 18 && Math.Abs(start.Y-box[1]-box[3])*PageSurface.ActualHeight < 18) dragMode = 2;
        else if (box is not null && start.X >= box[0] && start.X <= box[0]+box[2] && start.Y >= box[1] && start.Y <= box[1]+box[3]) dragMode = 1;
        PageSurface.CaptureMouse(); e.Handled = true;
    }
    private void SelectionMove(object sender, MouseEventArgs e)
    {
        if (!PageSurface.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        var p = Normalized(e);
        if (dragMode == 1 && before is not null) box = [Math.Clamp(before[0]+p.X-start.X, 0, 1-before[2]), Math.Clamp(before[1]+p.Y-start.Y, 0, 1-before[3]), before[2], before[3]];
        else if (dragMode == 2 && before is not null) box = [before[0], before[1], Math.Clamp(p.X-before[0], 0, 1-before[0]), Math.Clamp(p.Y-before[1], 0, 1-before[1])];
        else box = [Math.Min(p.X,start.X), Math.Min(p.Y,start.Y), Math.Abs(p.X-start.X), Math.Abs(p.Y-start.Y)];
        DrawBox();
    }
    private void SelectionUp(object sender, MouseButtonEventArgs e) { PageSurface.ReleaseMouseCapture(); if (box is not null && (box[2] <= 0 || box[3] <= 0)) box = null; DrawBox(); }
    private void DrawBox()
    {
        if (SelectionBox is null) return;
        if(BatchSummary is not null)UpdateBatchSummary();
        SelectionBox.Visibility = ResizeHandle.Visibility = box is null ? Visibility.Collapsed : Visibility.Visible;
        DesignSummary.Foreground=System.Windows.Media.Brushes.SlateGray;
        if (box is null) { DesignSummary.Text="Elige un diseño y dibuja dónde colocarlo."; return; }
        try
        {
            var design=ActiveDocument!.Preset.Design;
            var layout=SignatureArt.Layout((float)(box[2]*pageWidth),(float)(box[3]*pageHeight),design.Resolve(Identity?.Name??"Tu nombre y apellidos",IssuerName,DateTimeOffset.Now),design.HasImage,design.TextSize);
            bool reduced=layout.Lines.Count>0&&layout.FontSize<design.TextSize;
            DesignSummary.Text=reduced?$"Texto ajustado a {layout.FontSize:0.##} pt para que quepa. Puedes ampliar el recuadro para leerlo mejor.":$"Preparada en la página {page+1}";
            if(reduced)DesignSummary.Foreground=System.Windows.Media.Brushes.DarkGoldenrod;
        }
        catch(ArgumentException ex) { DesignSummary.Text=ex.Message;DesignSummary.Foreground=System.Windows.Media.Brushes.Firebrick; }
        double w = PageSurface.Width, h = PageSurface.Height;
        if (!double.IsFinite(w) || !double.IsFinite(h)) return;
        Canvas.SetLeft(SelectionBox, box[0]*w); Canvas.SetTop(SelectionBox, box[1]*h); SelectionBox.Width = box[2]*w; SelectionBox.Height = box[3]*h;
        SelectedDesignPreview.Update(ActiveDocument!.Preset.Design, Identity?.Name ?? "Selecciona un certificado", IssuerName, box[2]*pageWidth, box[3]*pageHeight);
        Canvas.SetLeft(ResizeHandle, (box[0]+box[2])*w-5); Canvas.SetTop(ResizeHandle, (box[1]+box[3])*h-5);
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
