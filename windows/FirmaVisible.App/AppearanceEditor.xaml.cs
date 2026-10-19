using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FirmaVisible.Core;

namespace FirmaVisible.App;

public partial class AppearanceEditor : Window
{
    private byte[]? image;
    private readonly string signerName, issuerName;
    private bool initialized;
    public SignatureDesign? Result { get; private set; }
    public AppearanceEditor(SignatureDesign design, string name, string issuer)
    {
        signerName = name; issuerName = issuer; image = design.ImagePng;
        InitializeComponent(); TemplateText.Text = design.Template; DecoratedCheck.IsChecked = design.Decorated; initialized = true; Refresh();
    }
    private SignatureDesign Current() => new(TemplateText.Text, image, DecoratedCheck.IsChecked == true);
    private void DraftChanged(object sender, RoutedEventArgs e) { if (initialized) Refresh(); }
    private void Refresh()
    {
        ImageState.Text = image is null ? "Sin imagen" : "Imagen preparada · se guardará con el diseño";
        try { DesignPreview.Update(Current(), signerName, issuerName); ValidationText.Text = ""; DesignPreview.Opacity = 1; }
        catch (ArgumentException ex) { ValidationText.Text = ex.Message; DesignPreview.Opacity = .3; }
    }
    private void InsertToken(object sender, RoutedEventArgs e)
    {
        string token = (string)((Button)sender).Tag;
        int cursor = TemplateText.SelectionStart; TemplateText.SelectedText = token; TemplateText.CaretIndex = cursor + token.Length; TemplateText.Focus();
    }
    private async void ChooseImage(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Imagen PNG o JPEG|*.png;*.jpg;*.jpeg", Title = "Imagen de la firma visible" };
        if (picker.ShowDialog(this) != true) return;
        IsEnabled = false;
        try { image = await Task.Run(() => ImageNormalizer.Import(picker.FileName)); Refresh(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo preparar la imagen", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { IsEnabled = true; }
    }
    private void RemoveImage(object sender, RoutedEventArgs e) { image = null; Refresh(); }
    private void SaveDesign(object sender, RoutedEventArgs e)
    { try { Result = Current(); DialogResult = true; } catch (ArgumentException ex) { ValidationText.Text = ex.Message; } }
}
