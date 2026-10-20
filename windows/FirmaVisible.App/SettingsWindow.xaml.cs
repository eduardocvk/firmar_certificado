using System.Windows;
using System.Windows.Controls;
using FirmaVisible.Core;

namespace FirmaVisible.App;

public partial class SettingsWindow : Window
{
    private readonly UserPreferences initial;
    private readonly List<SignaturePreset> designs;
    private readonly string signer,issuer;
    public UserPreferences? Result { get; private set; }
    public SettingsWindow(UserPreferences preferences,string name="Tu nombre y apellidos",string issuer="Emisor del certificado")
    {
        initial=preferences;designs=preferences.Designs.ToList();signer=name;this.issuer=issuer;
        InitializeComponent();Height=Math.Min(Height,SystemParameters.WorkArea.Height);
        TimestampCheck.IsChecked=preferences.UseTimestamp;ConfirmBeforeCheck.IsChecked=preferences.ConfirmBeforeSigning;SuccessDialogCheck.IsChecked=preferences.ShowSuccessDialog;
        Refresh(preferences.SelectedDesign.Id);
    }
    private SignaturePreset? Selected => PresetList.SelectedItem as SignaturePreset;
    private void Refresh(string id)
    { PresetList.ItemsSource=null;PresetList.ItemsSource=designs.ToArray();PresetList.SelectedItem=designs.FirstOrDefault(d=>d.Id==id)??designs[0]; }
    private void PresetChanged(object sender,SelectionChangedEventArgs e)
    { if(Selected is not {} preset || NameText is null)return;NameText.Text=preset.Name;PresetPreview.Update(preset.Design,signer,issuer); }
    private void CreatePreset(object sender,RoutedEventArgs e)
    {
        if(designs.Count>=20){ValidationText.Text="Puedes guardar hasta 20 firmas personalizadas.";return;}
        var editor=new AppearanceEditor(SignatureDesign.Standard(),signer,issuer){Owner=this};
        if(editor.ShowDialog()!=true||editor.Result is not {} design)return;
        string name="Firma "+(designs.Count+1);int suffix=designs.Count+1;while(designs.Any(d=>d.Name==name))name="Firma "+(++suffix);
        var added=new SignaturePreset(Guid.NewGuid().ToString("N"),name,design);designs.Add(added);Refresh(added.Id);NameText.Focus();NameText.SelectAll();
    }
    private void EditPreset(object sender,RoutedEventArgs e)
    {
        if(Selected is not {} preset)return;
        var editor=new AppearanceEditor(preset.Design,signer,issuer){Owner=this};
        if(editor.ShowDialog()!=true||editor.Result is not {} design)return;
        designs[designs.IndexOf(preset)]=preset with {Design=design};Refresh(preset.Id);
    }
    private void RenamePreset(object sender,RoutedEventArgs e)
    {
        if(Selected is not {} preset)return;
        string name=NameText.Text.Trim();if(string.IsNullOrWhiteSpace(name)){ValidationText.Text="Escribe un nombre para la firma.";return;}
        designs[designs.IndexOf(preset)]=preset with {Name=name};ValidationText.Text="";Refresh(preset.Id);
    }
    private void DeletePreset(object sender,RoutedEventArgs e)
    {
        if(designs.Count==1){ValidationText.Text="Conserva al menos una firma personalizada.";return;}
        if(Selected is {} preset)designs.Remove(preset);Refresh(designs[0].Id);
    }
    private void SaveSettings(object sender,RoutedEventArgs e)
    {
        try
        {
            if(string.IsNullOrWhiteSpace(NameText.Text))throw new ArgumentException("Escribe un nombre para la firma.");
            if(Selected is {} preset)designs[designs.IndexOf(preset)]=preset with{Name=NameText.Text.Trim()};
            Result=(initial with{UseTimestamp=TimestampCheck.IsChecked==true,ConfirmBeforeSigning=ConfirmBeforeCheck.IsChecked==true,ShowSuccessDialog=SuccessDialogCheck.IsChecked==true}).WithDesigns(designs,Selected!.Id);
            DialogResult=true;
        }
        catch(ArgumentException ex){ValidationText.Text=ex.Message;}
    }
    private void ExplorerClick(object sender,RoutedEventArgs e)=>WindowsActions.ShowExplorer(this);
    private void AboutClick(object sender,RoutedEventArgs e)=>WindowsActions.ShowLicenses(this);
    private void PrivacyClick(object sender,RoutedEventArgs e)=>WindowsActions.ShowPrivacy(this);
}
