using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FirmaVisible.App;
using FirmaVisible.Core;

internal static class WorkspaceFeatures
{
    private static void Complete(Task task)
    {var frame=new DispatcherFrame();var dispatcher=Dispatcher.CurrentDispatcher;_=task.ContinueWith(_=>dispatcher.BeginInvoke(()=>frame.Continue=false));Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();}
    private static void Capture(Window window,string path,double width,double height)
    {var content=(FrameworkElement)window.Content;content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();var bitmap=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);}
    public static void Run(string root,Action<bool,string> check,Action<Action,string> refuses)
    {
        var a=new SignaturePreset("a","Firma personal",new("Firmado por {nombre}",CustomFeatures.ExampleImage(),false,6));
        var b=new SignaturePreset("b","Firma profesional",new("Revisado · {nombre}\n{fecha}",null,false,8));
        var store=new UserPreferencesStore(Path.Combine(root,"multi-design-preferences"));
        var preferences=new UserPreferences(a.Design,false,null,false,false).WithDesigns([a,b],a.Id);store.Save(preferences);
        var loaded=store.Load();
        check(loaded.Designs.Count==2&&loaded.Designs[0].Name==a.Name&&loaded.Designs[1].Design.TextSize==8,"No se conservan los nombres y los tamaños de varias firmas.");
        check(loaded.Designs[0].Design.ImagePng!.SequenceEqual(a.Design.ImagePng!),"No se conserva la imagen de una firma guardada.");
        check(loaded.WithDesigns(loaded.Designs,"b").SelectedDesign.Name==b.Name,"No se puede elegir otra firma guardada.");
        refuses(()=>loaded.WithDesigns([a,a],"a"),"Se guardan firmas con identificadores duplicados.");
        string folder=Path.Combine(root,"workspace-pdfs");Directory.CreateDirectory(folder);
        string first=Path.Combine(folder,"Primer documento.pdf"),second=Path.Combine(folder,"Segundo documento á.pdf"),extra=Path.Combine(folder,"No es PDF.txt");
        File.Copy(Path.Combine(root,"original-0.pdf"),first,true);File.Copy(Path.Combine(root,"original-90.pdf"),second,true);File.WriteAllText(extra,"Ignored by PDF drop");
        foreach(string path in new[]{BatchSigner.OutputPath(first),BatchSigner.OutputPath(second)})if(File.Exists(path))File.Delete(path);
        check(MainWindow.DroppedPdfPaths([first,extra,second,folder]).SequenceEqual(new[]{first,second}),"El arrastre no selecciona únicamente los archivos PDF.");
        check(LaunchDocument.ParseMany(["--open",first,second,first]).SequenceEqual(new[]{first,second}),"La apertura de varios argumentos duplica o pierde archivos.");
        using var key=RSA.Create(2048);var request=new CertificateRequest("CN=Pestañas TEST - NO REAL",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var cert=request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1),DateTimeOffset.Now.AddDays(1));
        preferences=loaded with{LastIdentityFingerprint=cert.GetCertHashString(HashAlgorithmName.SHA256)};store.Save(preferences);
        var window=new MainWindow(store,_=>new CertificateIdentity(cert));var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            Complete(window.RestoreLastIdentityAsync());Complete(window.OpenDocumentsAsync(MainWindow.DroppedPdfPaths([first,extra,second])));
            check(window.Documents.Count==2&&window.ActiveDocument!.OriginalPath==second,"No se abren varios PDF en pestañas.");
            check(((ListBox)window.FindName("DocumentList")).Items.Count==2&&((Button)window.FindName("VerifyButton")).Visibility==Visibility.Collapsed,"La lista no muestra los PDF abiertos o aparece Comprobar firmas en un PDF sin firmar.");
            var one=window.Documents[0];var two=window.Documents[1];
            Complete(window.OpenDocumentAsync(first));check(window.Documents.Count==2&&ReferenceEquals(window.ActiveDocument,one),"Un PDF ya abierto crea una pestaña duplicada.");
            refuses(()=>window.SignAllAsync().GetAwaiter().GetResult(),"Se permite firmar un lote sin recuadros.");
            one.SetSelection([.15,.55,.005,.005]);Complete(window.SelectDocumentAsync(one));
            var liveSummary=(TextBlock)window.FindName("DesignSummary");
            check(liveSummary.Text==SignatureArt.SizeNotice&&liveSummary.Foreground==Brushes.Firebrick,"El tamaño imposible no se avisa antes de firmar.");
            one.SetSelection([.15,.55,.09,.025]);Complete(window.SelectDocumentAsync(one));
            check(one.Ready&&liveSummary.Text.StartsWith("Texto ajustado a "),"El recuadro compacto no ajusta el texto ni avisa mientras se redimensiona.");
            Capture(window,Path.Combine(root,"windows-compact-preview.png"),1260,840);
            one.SetSelection([.15,.55,.5,.15]);Complete(window.NavigateToPageAsync(1));
            check(one.CurrentPage==1&&one.SelectionPage==0&&one.Ready,"Cambiar de página borra el recuadro de firma.");
            check(((Border)window.FindName("SelectionBox")).Visibility==Visibility.Collapsed,"Se dibuja el recuadro en una página diferente.");
            Complete(window.SelectDocumentAsync(two));Complete(window.SelectSignatureAsync("b"));two.SetSelection([.15,.55,.5,.15]);
            Complete(window.SelectDocumentAsync(one));
            check(one.Preset.Id=="a"&&two.Preset.Id=="b"&&one.CurrentPage==1,"Las pestañas comparten la firma o pierden su página.");
            Complete(window.NavigateToPageAsync(0));check(((Border)window.FindName("SelectionBox")).Visibility==Visibility.Visible,"No se restaura el recuadro al volver a su página.");
            check(((ComboBox)window.FindName("SignatureCombo")).SelectedItem is SignaturePreset selected&&selected.Id=="a","El selector no muestra la firma de la pestaña activa.");
            check(((Button)window.FindName("SignAllButton")).Content.ToString()=="Firmar"&&window.FindName("TimestampCheck") is null&&window.FindName("ConfirmBeforeCheck") is null,"Las opciones no se han trasladado a configuración.");
            Capture(window,Path.Combine(root,"windows-tabs-prepared.png"),1260,840);
            Capture(window,Path.Combine(root,"store-screenshot-signing.png"),1600,1000);
            check(window.TwoPageMode&&((Grid)window.FindName("SecondPageSurface")).Visibility==Visibility.Visible,"No aparecen dos páginas cuando hay espacio.");
            window.SelectVisiblePage(true);one.SetSelection([.12,.58,.4,.11]);window.SelectVisiblePage(true);
            check(one.SelectionPage==1&&Math.Abs(one.SelectionPageWidth-595)<2&&((Border)window.FindName("SecondSelectionBox")).Visibility==Visibility.Visible&&((Border)window.FindName("SelectionBox")).Visibility==Visibility.Collapsed,"La firma en la página derecha usa la página, medidas o recuadro incorrectos.");
            Capture(window,Path.Combine(root,"windows-two-pages.png"),1600,1000);
            Complete(window.NavigateToPageAsync(1));
            check(window.TwoPageMode&&one.CurrentPage==1&&((Border)window.FindName("SecondSelectionBox")).Visibility==Visibility.Visible,"La última página no conserva la vista doble ni la firma al navegar.");
            Capture(window,Path.Combine(root,"windows-narrow-right-page.png"),1260,840);
            check(!window.TwoPageMode&&one.CurrentPage==1&&((Border)window.FindName("SelectionBox")).Visibility==Visibility.Visible,"Al estrechar la ventana se pierde la página derecha seleccionada.");
            Capture(window,Path.Combine(root,"store-screenshot-signing.png"),1600,1000);
            Complete(window.SignAllAsync());
            check(one.SignedOutput==BatchSigner.OutputPath(first)&&two.SignedOutput==BatchSigner.OutputPath(second),"El botón Firmar no guarda todas las copias _firmado.");
            check(PdfEngine.Verify(one.SignedOutput!).LatestCoversWholeFile&&PdfEngine.Verify(two.SignedOutput!).LatestCoversWholeFile,"Una copia de las pestañas no tiene firma íntegra.");
            using(var pdf=new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(one.SignedOutput!)))
            {
                var field=iText.Forms.PdfAcroForm.GetAcroForm(pdf,false).GetAllFormFields().Values.Single();
                check(pdf.GetPageNumber(field.GetWidgets()[0].GetPage())==2,"La firma dibujada a la derecha no se guarda en la segunda página.");
            }
            check(((Button)window.FindName("VerifyButton")).Visibility==Visibility.Visible,"No se ofrece comprobar la copia recién firmada.");
            Capture(window,Path.Combine(root,"windows-tabs-signed.png"),1260,840);
            Capture(window,Path.Combine(root,"store-screenshot-result.png"),1600,1000);
            var settings=new SettingsWindow(store.Load());Capture(settings,Path.Combine(root,"windows-settings.png"),768,750);
            check(((ListBox)settings.FindName("PresetList")).Items.Count==2&&settings.FindName("TimestampCheck") is CheckBox,"Configuración no contiene las firmas guardadas y el sello.");settings.Close();
            Complete(window.OpenDocumentAsync(one.SignedOutput!));var signedInput=window.ActiveDocument!;
            check(signedInput.HasExistingSignatures&&((Button)window.FindName("VerifyButton")).Visibility==Visibility.Visible,"No se detectan las firmas de un PDF que se vuelve a abrir.");
            Complete(window.CloseDocumentAsync(signedInput));
            string twoCopy=two.InputPath;Complete(window.CloseDocumentAsync(one));
            check(window.Documents.Count==1&&File.Exists(twoCopy)&&!File.Exists(one.InputPath),"Cerrar una pestaña borra la copia de otra o deja la propia.");
            Complete(window.CloseDocumentAsync(two));check(window.Documents.Count==0&&window.ActiveDocument is null,"No se puede cerrar la última pestaña.");
        }
        finally{window.Close();SynchronizationContext.SetSynchronizationContext(previous);}
        var received=new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using(var primary=new DocumentLaunchBroker("test-"+Guid.NewGuid().ToString("N")))
        {
            // Use an independent identity for the pipe; no real application instance or registry is changed.
            check(primary.IsPrimary,"No se crea una instancia primaria de pruebas.");
        }
        string brokerId="test-"+Guid.NewGuid().ToString("N");
        using(var primary=new DocumentLaunchBroker(brokerId))
        using(var secondary=new DocumentLaunchBroker(brokerId))
        {
            primary.Listen(paths=>received.TrySetResult(paths));
            check(primary.IsPrimary&&!secondary.IsPrimary,"Dos procesos se consideran primarios para la misma copia.");
            Complete(secondary.SendAsync([first,second]));
            check(received.Task.GetAwaiter().GetResult().SequenceEqual(new[]{first,second}),"La apertura de otra instancia pierde las rutas con espacios o acentos.");
        }
        Console.WriteLine("Firmas guardadas, arrastre, pestañas, lote y apertura en una sola ventana: correctos.");
    }
}
