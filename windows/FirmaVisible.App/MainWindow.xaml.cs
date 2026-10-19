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
    private readonly string session = Path.Combine(Path.GetTempPath(), "FirmaPDF", Guid.NewGuid().ToString("N"));
    private readonly List<CertificateIdentity> identities = [];
    private string? input, displayInput, originalPath;
    private int page, pages;
    private bool busy;
    private double[]? box, before;
    private Point start;
    private int dragMode;
    private double baseWidth = 800;
    private double pageWidth = 600, pageHeight = 800;
    private readonly UserPreferencesStore preferencesStore;
    private UserPreferences preferences = UserPreferences.Default();
    private bool initialized;
    private string? lastSigned;

    public MainWindow() : this(new UserPreferencesStore()) { }
    public MainWindow(UserPreferencesStore store)
    {
        preferencesStore = store; InitializeComponent(); Directory.CreateDirectory(session);
        MaxHeight = SystemParameters.WorkArea.Height; MaxWidth = SystemParameters.WorkArea.Width;
        try { preferences = store.Load(); }
        catch (Exception ex) { StatusLabel.Text = $"No se pudo cargar el diseño guardado: {ex.Message} El archivo se conserva; puedes crear otro diseño."; }
        TimestampCheck.IsChecked = preferences.UseTimestamp; initialized = true; UpdateDesignPreview();
    }
    private CertificateIdentity? Identity => CertificateCombo.SelectedItem as CertificateIdentity;

    private async Task WorkAsync(string message, Func<Task> work)
    {
        if (busy) return;
        busy = true; Sidebar.IsEnabled = NavigationPanel.IsEnabled = PageSurface.IsEnabled = false; WorkingProgress.Visibility = Visibility.Visible; StatusLabel.Text = message;
        try { await work(); }
        catch (Exception ex) { StatusLabel.Text = "Operación no completada."; MessageBox.Show(this, ex.Message, "FirmaPDF", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { busy = false; Sidebar.IsEnabled = NavigationPanel.IsEnabled = PageSurface.IsEnabled = true; WorkingProgress.Visibility = Visibility.Collapsed; }
    }

    private async void OpenPdfClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Documento PDF|*.pdf", Title = "Abrir PDF" };
        if (picker.ShowDialog(this) != true) return;
        await OpenDocumentAsync(picker.FileName);
    }

    public async Task OpenDocumentAsync(string path)
    {
        await WorkAsync("Abriendo documento…", async () =>
        {
            path = LaunchDocument.Validate(path);
            string copy = Path.Combine(session, $"original-{Guid.NewGuid():N}.pdf");
            string preview;
            try
            {
                preview = await Task.Run(() => { File.Copy(path, copy); return PdfEngine.PreparePreview(copy, session); });
                var rendered = await PdfPreview.RenderAsync(preview, 0);
                DeleteDocumentCopies(); input = copy; displayInput = preview; originalPath = path; page = 0;
                DocumentLabel.Text = Path.GetFileName(originalPath); ShowPage(rendered);
            }
            catch { if (File.Exists(copy)) File.Delete(copy); throw; }
        });
    }

    private void ExplorerClick(object sender, RoutedEventArgs e)
    {
        var integration = ExplorerIntegration.ForCurrentUser();
        string executable = ExplorerIntegration.ExecutablePath;
        var info = new TextBlock { Text = "Añade «Firmar con FirmaPDF» al botón derecho de los PDF para abrirlos directamente aquí.\n\nEn Windows 11 está en «Mostrar más opciones». No cambia tu lector habitual ni requiere administrador.\n\nConserva esta carpeta. Si la mueves, activa de nuevo el menú desde la nueva ubicación.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 16, 20, 12), MaxWidth = 430 };
        var status = new TextBlock { Margin = new Thickness(20, 0, 20, 12), TextWrapping = TextWrapping.Wrap };
        void Refresh() => status.Text = integration.IsRegisteredFor(executable) ? "Menú activado para esta copia de FirmaPDF." : "Menú no activado para esta copia.";
        var add = new Button { Content = "Activar menú", Margin = new Thickness(20, 4, 20, 4) };
        var remove = new Button { Content = "Quitar menú", Margin = new Thickness(20, 4, 20, 4) };
        var close = new Button { Content = "Cerrar", IsCancel = true, Margin = new Thickness(20, 4, 20, 16) };
        var content = new StackPanel(); content.Children.Add(info); content.Children.Add(status); content.Children.Add(add); content.Children.Add(remove); content.Children.Add(close);
        var dialog = new Window { Owner = this, Title = "Menú del botón derecho", Content = content, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        void Change(Action action) { try { action(); Refresh(); } catch (Exception ex) { status.Text = ex.Message; } }
        add.Click += (_, _) => Change(() => integration.Register(executable));
        remove.Click += (_, _) => Change(() => integration.Remove(executable));
        close.Click += (_, _) => dialog.Close();
        try { Refresh(); } catch (Exception ex) { status.Text = ex.Message; }
        dialog.ShowDialog();
    }

    private void ShowPage(RenderedPage rendered)
    {
        PageImage.Source = rendered.Image; pages = rendered.Count; pageWidth = rendered.Width; pageHeight = rendered.Height; box = null;
        PageSurface.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        FitPage();
        UpdateZoom(); DrawBox(); Viewport.ScrollToTop(); Viewport.ScrollToLeftEnd();
        PageLabel.Text = $"Página {page + 1} de {pages}";
        StatusLabel.Text = "Dibuja el recuadro donde quieres firmar. Vista previa orientativa del texto.";
    }

    private async Task NavigateAsync(int index)
    {
        if (displayInput is null || index < 0 || index >= pages) return;
        await WorkAsync("Preparando página…", async () => { var result = await PdfPreview.RenderAsync(displayInput, index); page = index; ShowPage(result); });
    }
    private async void PreviousClick(object sender, RoutedEventArgs e) => await NavigateAsync(page - 1);
    private async void NextClick(object sender, RoutedEventArgs e) => await NavigateAsync(page + 1);

    private void SetIdentities(IEnumerable<CertificateIdentity> values)
    {
        ForgetIdentities(); identities.AddRange(values); CertificateCombo.ItemsSource = identities.ToArray(); CertificateCombo.SelectedIndex = identities.Count == 0 ? -1 : 0;
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
    private void ForgetClick(object sender, RoutedEventArgs e) => ForgetIdentities();
    private void ForgetIdentities() { CertificateCombo.ItemsSource = null; foreach (var identity in identities) identity.Dispose(); identities.Clear(); }

    private async void SignClick(object sender, RoutedEventArgs e)
    {
        if (input is null || box is null || Identity is not { } identity) { MessageBox.Show(this, "Abre un PDF, elige un certificado y dibuja el recuadro."); return; }
        Placement placement;
        try { placement = new(page, box[0], box[1], box[2], box[3]); } catch (ArgumentException ex) { MessageBox.Show(this, ex.Message); return; }
        var picker = new SaveFileDialog { Filter = "Documento PDF|*.pdf", DefaultExt = ".pdf", AddExtension = true, Title = "Guardar copia firmada", FileName = Path.GetFileNameWithoutExtension(originalPath) + "_firmado.pdf", InitialDirectory = Path.GetDirectoryName(originalPath), OverwritePrompt = false };
        if (picker.ShowDialog(this) != true) return;
        if (File.Exists(picker.FileName)) { MessageBox.Show(this, "Ese archivo ya existe. Elige un nombre nuevo para conservarlo."); return; }
        bool timestamp = TimestampCheck.IsChecked == true; var design = preferences.Design;
        string timeNotice = timestamp ? "Se solicitará un sello de tiempo a DigiCert enviando solo la huella de la firma, sin el PDF ni tus claves. Si falla, no se guardará una copia sin sello." : "Sin sello de tiempo. La fecha procede del ordenador.";
        if (MessageBox.Show(this, $"Vas a firmar el PDF con:\n\n{identity.Details}\n\nSe creará una copia con tu diseño.\n{timeNotice}", "Autorizar firma", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        string source = input;
        await WorkAsync("Firmando y comprobando integridad…", async () =>
        {
            var result = await Task.Run(() => { PdfEngine.Sign(source, picker.FileName, placement, identity, design, timestamp ? TimestampClient.DigiCert() : null); return PdfEngine.Verify(picker.FileName); });
            lastSigned = picker.FileName; LastSignedButton.IsEnabled = true; LastSignedButton.Visibility = Visibility.Visible;
            StatusLabel.Text = $"Copia firmada: {picker.FileName}";
            string date = result.LatestTimestamp is { } time ? $"Hora acreditada por el sello: {time.ToLocalTime():dd/MM/yyyy HH:mm:ss zzz}" : "Sin sello de tiempo. Fecha declarada del ordenador.";
            MessageBox.Show(this, $"Guardado en:\n{picker.FileName}\n\n{date}\n\n{PdfEngine.VerificationNotice}\nConsulta la confianza completa en tu visor habitual.", "PDF firmado", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void VerifyClick(object sender, RoutedEventArgs e)
    {
        if (input is null) return;
        await WorkAsync("Comprobando firmas…", async () => { var result = await Task.Run(() => PdfEngine.Verify(input)); StatusLabel.Text = "Comprobación terminada."; MessageBox.Show(this, result.ToString(), "Integridad de las firmas"); });
    }
    private void ResetBoxClick(object sender, RoutedEventArgs e) { if (input is not null) { box = [.1, .72, .55, .14]; DrawBox(); } }
    private void OpenSignedClick(object sender, RoutedEventArgs e)
    {
        try { if (lastSigned is not null) Process.Start(new ProcessStartInfo(lastSigned) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo abrir el PDF"); }
    }
    private async void CustomizeClick(object sender, RoutedEventArgs e)
    {
        var editor = new AppearanceEditor(preferences.Design, Identity?.Name ?? "Tu nombre y apellidos", IssuerName) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is not { } design) return;
        var next = new UserPreferences(design, TimestampCheck.IsChecked == true);
        await WorkAsync("Guardando tu diseño…", async () =>
        {
            await Task.Run(() => preferencesStore.Save(next)); preferences = next; UpdateDesignPreview(); StatusLabel.Text = "Diseño guardado en tu perfil de Windows. Se reutilizará al abrir de nuevo la app.";
        });
    }
    private async void TimestampChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized || busy) return;
        var next = new UserPreferences(preferences.Design, TimestampCheck.IsChecked == true);
        await WorkAsync("Guardando preferencia de sellado…", async () => { await Task.Run(() => preferencesStore.Save(next)); preferences = next; StatusLabel.Text = next.UseTimestamp ? "Sello de tiempo activado. Requiere Internet; solo se envía una huella de la firma." : "Sello desactivado. Puedes firmar sin conexión con la fecha del ordenador."; });
        initialized = false; TimestampCheck.IsChecked = preferences.UseTimestamp; initialized = true;
    }
    private string IssuerName => Identity?.Certificate.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, true) ?? "Emisor del certificado";
    private void UpdateDesignPreview()
    {
        DesignSummary.Text = preferences.Design.HasImage ? string.IsNullOrWhiteSpace(preferences.Design.Template) ? "Solo imagen · diseño reutilizable" : "Texto e imagen · diseño reutilizable" : "Texto · diseño reutilizable";
        DrawBox();
    }
    private void AboutClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "FirmaPDF 0.3.3 · C#/.NET y WPF\n\nMotor PDF: iText Community, Copyright Apryse Group NV.\nEsta aplicación Windows se distribuye bajo AGPL-3.0-or-later, sin garantía.\nFuente Noto Sans: SIL Open Font License 1.1.\n\nEl código fuente y las instrucciones completas están disponibles en GitHub. ¿Abrirlos?", "Código fuente y licencias", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo("https://github.com/eduardocvk/firmar_certificado/tree/main/windows") { UseShellExecute = true });
    }

    private void ZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (PageImage is not null) UpdateZoom(); }
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
        SelectionBox.Visibility = ResizeHandle.Visibility = box is null ? Visibility.Collapsed : Visibility.Visible;
        if (box is null) return;
        double w = PageSurface.Width, h = PageSurface.Height;
        if (!double.IsFinite(w) || !double.IsFinite(h)) return;
        Canvas.SetLeft(SelectionBox, box[0]*w); Canvas.SetTop(SelectionBox, box[1]*h); SelectionBox.Width = box[2]*w; SelectionBox.Height = box[3]*h;
        SelectedDesignPreview.Update(preferences.Design, Identity?.Name ?? "Selecciona un certificado", IssuerName, box[2]*pageWidth, box[3]*pageHeight);
        Canvas.SetLeft(ResizeHandle, (box[0]+box[2])*w-5); Canvas.SetTop(ResizeHandle, (box[1]+box[3])*h-5);
    }
    private void DeleteDocumentCopies() { if (displayInput is not null && displayInput != input && File.Exists(displayInput)) File.Delete(displayInput); if (input is not null && File.Exists(input)) File.Delete(input); }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; StatusLabel.Text = "Espera a que termine la operación antes de cerrar."; return; }
        ForgetIdentities();
        try { foreach (string file in Directory.GetFiles(session)) File.Delete(file); Directory.Delete(session); } catch (IOException) { }
    }
}
