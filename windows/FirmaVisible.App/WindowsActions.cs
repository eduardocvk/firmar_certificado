using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace FirmaVisible.App;

public static class WindowsActions
{
    public static void ShowExplorer(Window owner)
    {
        var integration=ExplorerIntegration.ForCurrentUser();string executable=ExplorerIntegration.ExecutablePath;
        var panel=new StackPanel{Margin=new Thickness(22),MaxWidth=440};
        panel.Children.Add(new TextBlock{Text="Añade «Firmar con FirmaPDF» al botón derecho de los PDF. En Windows 11 está en «Mostrar más opciones».\n\nSin administrador y sin cambiar el lector habitual. Si mueves esta carpeta, activa el menú desde la nueva ubicación.",TextWrapping=TextWrapping.Wrap});
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
        if(MessageBox.Show(owner,"FirmaPDF 0.4.0 · C#/.NET y WPF\n\nMotor PDF: iText Community, Copyright Apryse Group NV.\nWindows se distribuye bajo AGPL-3.0-or-later, sin garantía.\nFuente Noto Sans: SIL Open Font License 1.1.\n\n¿Abrir el código fuente y las licencias en GitHub?","Código fuente y licencias",MessageBoxButton.YesNo)==MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo("https://github.com/eduardocvk/firmar_certificado/tree/main/windows"){UseShellExecute=true});
    }
}
