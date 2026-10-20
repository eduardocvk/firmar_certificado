using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace FirmaVisible.App;

public static class WindowsActions
{
    public static void ShowPrivacy(Window owner)
    {
        if(MessageBox.Show(owner,"Los documentos y las claves se procesan en tu ordenador. La aplicación no incluye publicidad ni telemetría propia.\n\nSe guardan localmente tus diseños, preferencias y la huella pública del último certificado, nunca su contraseña ni su clave privada.\n\nSi activas el sello de tiempo, DigiCert recibe una huella y ve tu IP. Al comprobar confianza y revocación, Windows puede consultar a los emisores y enviar datos públicos del certificado, como su número de serie. No se envían los documentos ni las claves privadas.\n\n¿Abrir la política completa en el navegador?","Privacidad de FirmaPDF",MessageBoxButton.YesNo,MessageBoxImage.Information)==MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo("https://github.com/eduardocvk/firmar_certificado/blob/main/windows/PRIVACY.md"){UseShellExecute=true});
    }
    public static void ShowExplorer(Window owner)
    {
        if(PackageEnvironment.IsPackaged)
        {
            MessageBox.Show(owner,"Esta versión registra su compatibilidad con documentos y firmas .p7s al instalarse.\n\nPara enviar un archivo a FirmaPDF, haz clic derecho → Abrir con → FirmaPDF. Windows también gestiona el verbo «Firmar con FirmaPDF» para la asociación del archivo; su aparición depende del menú y del lector predeterminado.\n\nNo necesitas activar un menú en el registro. Tu lector habitual no se cambia automáticamente.","Abrir PDF desde el Explorador",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        var integration=ExplorerIntegration.ForCurrentUser();string executable=ExplorerIntegration.ExecutablePath;
        var panel=new StackPanel{Margin=new Thickness(22),MaxWidth=440};
        panel.Children.Add(new TextBlock{Text="Añade «Firmar con FirmaPDF» al botón derecho de PDF, documentos, imágenes y archivos compatibles. En Windows 11 está en «Mostrar más opciones».\n\nSin administrador y sin cambiar el lector habitual. Si mueves esta carpeta, activa el menú desde la nueva ubicación.",TextWrapping=TextWrapping.Wrap});
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,12)};panel.Children.Add(status);
        var dialog=new Window{Owner=owner,Title="Menú del botón derecho",Content=panel,SizeToContent=SizeToContent.WidthAndHeight,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        void Refresh()=>status.Text=integration.IsRegisteredFor(executable)?"Menú activado para esta copia.":"Menú no activado para esta copia.";
        void Change(Action action){try{action();Refresh();}catch(Exception ex){status.Text=ex.Message;}}
        var add=new Button{Content="Activar menú"};add.Click+=(_,_)=>Change(()=>integration.Register(executable));panel.Children.Add(add);
        var remove=new Button{Content="Quitar menú"};remove.Click+=(_,_)=>Change(()=>integration.Remove(executable));panel.Children.Add(remove);
        var close=new Button{Content="Cerrar",IsCancel=true};close.Click+=(_,_)=>dialog.Close();panel.Children.Add(close);
        Change(()=>{});dialog.ShowDialog();
    }
    public static void ShowLicenses(Window owner)
    {
        if(MessageBox.Show(owner,"FirmaPDF 0.5.0 · C#/.NET y WPF\n\nMotor PDF: iText Community, Copyright Apryse Group NV.\nWindows se distribuye bajo AGPL-3.0-or-later, sin garantía.\nFuente Noto Sans: SIL Open Font License 1.1.\n\n¿Abrir el código fuente y las licencias en GitHub?","Código fuente y licencias",MessageBoxButton.YesNo)==MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo("https://github.com/eduardocvk/firmar_certificado/tree/main/windows"){UseShellExecute=true});
    }
}
