using System.ComponentModel;
using System.IO;
using FirmaVisible.Core;

namespace FirmaVisible.App;

public sealed class DocumentSession(string original, string input, string preview, string directory, SignaturePreset preset) : INotifyPropertyChanged
{
    public string OriginalPath { get; } = original;
    public string InputPath { get; } = input;
    public string PreviewPath { get; } = preview;
    public string DirectoryPath { get; } = directory;
    public int CurrentPage { get; set; }
    public int PageCount { get; set; }
    public double PageWidth { get; set; } = 600;
    public double PageHeight { get; set; } = 800;
    public int SelectionPage { get; private set; }
    public double[]? Selection { get; private set; }
    public double SelectionPageWidth { get; private set; }
    public double SelectionPageHeight { get; private set; }
    public SignaturePreset Preset { get; private set; } = preset;
    public string? SignedOutput { get; private set; }
    public bool Ready => Selection is { Length:4 } s && s[2]*SelectionPageWidth>=SignatureArt.MinimumDimension && s[3]*SelectionPageHeight>=SignatureArt.MinimumDimension;
    public string Header => Path.GetFileName(OriginalPath) + (SignedOutput is not null ? "  ✓" : Ready ? "  •" : "");
    public void SetSelection(double[]? value)
    {
        Selection=value?.ToArray();SelectionPage=CurrentPage;SelectionPageWidth=PageWidth;SelectionPageHeight=PageHeight;SignedOutput=null;Notify();
    }
    public void SetPreset(SignaturePreset value)
    { bool same=value.Id==Preset.Id&&ReferenceEquals(value.Design,Preset.Design);Preset=value;if(!same)SignedOutput=null;Notify(); }
    public void MarkSigned(string path) { SignedOutput=path;Notify(); }
    public SigningJob Job(string? destination=null)
    {
        if(!Ready || Selection is not {} s)throw new ArgumentException(SignatureArt.SizeNotice + " " + Path.GetFileName(OriginalPath));
        return new(InputPath,destination??BatchSigner.OutputPath(OriginalPath),new(SelectionPage,s[0],s[1],s[2],s[3]),Preset.Design);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify() { PropertyChanged?.Invoke(this,new(nameof(Header)));PropertyChanged?.Invoke(this,new(nameof(Ready))); }
}
