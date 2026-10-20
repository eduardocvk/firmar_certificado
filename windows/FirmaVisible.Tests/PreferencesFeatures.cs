using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using FirmaVisible.App;
using FirmaVisible.Core;

internal static class PreferencesFeatures
{
    [StructLayout(LayoutKind.Sequential)] private struct ClientRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle, out ClientRect rectangle);
    private static void Complete(Task task)
    {
        var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    public static void Run(string root, Action<bool,string> check)
    {
        var store = new UserPreferencesStore(Path.Combine(root,"remembered-identity"));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Última firma TEST - NO REAL",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature,true));
        var cert = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1),DateTimeOffset.Now.AddDays(1));
        string fingerprint = cert.GetCertHashString(HashAlgorithmName.SHA256);
        store.Save(new(SignatureDesign.Standard(), false, fingerprint, false, false));
        string? lookedUp = null;
        var window = new MainWindow(store, value => { lookedUp=value; return new CertificateIdentity(cert); });
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            Complete(window.RestoreLastIdentityAsync());
            check(lookedUp==fingerprint && ((ComboBox)window.FindName("CertificateCombo")).SelectedItem is CertificateIdentity selected && selected.Fingerprint==fingerprint,"No se preselecciona el último certificado utilizado.");
            var settings=new SettingsWindow(store.Load());
            check(((CheckBox)settings.FindName("ConfirmBeforeCheck")).IsChecked==false && ((CheckBox)settings.FindName("SuccessDialogCheck")).IsChecked==false,"No se restauran las opciones de diálogos en configuración.");settings.Close();
            Complete(window.OpenDocumentAsync(Path.Combine(root,"original-0.pdf")));
            string output=Path.Combine(root,"signed-without-dialogs.pdf"); if(File.Exists(output)) File.Delete(output);
            Complete(window.SignDocumentAsync(output,new(0,.15,.55,.5,.15)));
            check(File.Exists(output) && PdfEngine.Verify(output).LatestCoversWholeFile,"No se firma al desactivar los diálogos.");
            check(((TextBlock)window.FindName("StatusLabel")).Text.Contains("PDF firmado y guardado"),"El éxito no queda visible sin diálogo.");
            check(((Button)window.FindName("LastSignedButton")).IsEnabled,"No se puede abrir el resultado sin aviso de éxito.");
            var saved=store.Load();
            check(saved.LastIdentityFingerprint==fingerprint && !saved.ConfirmBeforeSigning && !saved.ShowSuccessDialog,"Firmar pierde la identidad o las preferencias de diálogos.");
            string json=File.ReadAllText(store.FilePath);
            check(!json.Contains("PRIVATE KEY") && !json.Contains("Password") && !json.Contains(cert.GetRawCertDataString()),"Se guarda material del certificado en las preferencias.");
            check(double.IsPositiveInfinity(window.MaxWidth) && double.IsPositiveInfinity(window.MaxHeight),"La ventana sigue limitando la maximización al área de trabajo.");
            foreach(var size in new[]{new Size(1920,1000),new Size(1536,800)})
            {
                var content=(FrameworkElement)window.Content; content.Measure(size);content.Arrange(new Rect(size));content.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
                byte[] pixel=new byte[4];bitmap.CopyPixels(new Int32Rect((int)size.Width-1,(int)size.Height-1,1,1),pixel,4,0);
                check(pixel[3]==255&&pixel[0]>200&&pixel[1]>200&&pixel[2]>200,"Hay un borde oscuro en el contenido expandido.");
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(root,$"expanded-{(int)size.Width}.png"));encoder.Save(file);
            }
            window.ShowInTaskbar=false;window.Opacity=0;window.WindowState=WindowState.Maximized;window.Show();window.UpdateLayout();
            var handle=new WindowInteropHelper(window).Handle;var visual=(FrameworkElement)window.Content;
            var transform=PresentationSource.FromVisual(window)!.CompositionTarget!.TransformToDevice;
            check(GetClientRect(handle,out var client) && Math.Abs(visual.ActualWidth*transform.M11-client.Right)<2 && Math.Abs(visual.ActualHeight*transform.M22-client.Bottom)<2,"El contenido maximizado no ocupa toda el área cliente nativa de Windows.");
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
        var unavailable=new MainWindow(store,_=>null);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(unavailable.Dispatcher));
        try { Complete(unavailable.RestoreLastIdentityAsync()); check(((ComboBox)unavailable.FindName("CertificateCombo")).SelectedItem is null,"Se cambia silenciosamente a otro certificado si falta el último."); }
        finally { unavailable.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
        var legacy=new UserPreferencesStore(Path.Combine(root,"legacy-dialog-preferences"));Directory.CreateDirectory(Path.GetDirectoryName(legacy.FilePath)!);
        File.WriteAllText(legacy.FilePath,"{\"Template\":\"Anterior\",\"Decorated\":false,\"ImagePng\":null,\"UseTimestamp\":false,\"Version\":2,\"TextSize\":6}");
        var migrated=legacy.Load();
        check(migrated.ConfirmBeforeSigning && migrated.ShowSuccessDialog && migrated.LastIdentityFingerprint is null && migrated.Design.TextSize==6,"No se conservan los valores predeterminados al migrar preferencias.");
        Console.WriteLine("Último certificado, firma sin diálogos y ventana expandida: correctos.");
    }
}
