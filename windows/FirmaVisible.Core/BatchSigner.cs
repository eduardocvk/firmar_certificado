using iText.Signatures;

namespace FirmaVisible.Core;

public sealed record SigningJob(string Source, string Destination, Placement Placement, SignatureDesign Design);
public sealed record SignedDocument(SigningJob Job, Verification Verification);
public sealed class BatchSigningException(string message, IReadOnlyList<SignedDocument> completed, Exception inner) : IOException(message, inner)
{ public IReadOnlyList<SignedDocument> Completed { get; } = completed; }

public static class BatchSigner
{
    public static string OutputPath(string original) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(original))!, Path.GetFileNameWithoutExtension(original) + "_firmado.pdf");
    public static void CheckDestinations(IReadOnlyList<SigningJob> jobs)
    {
        if (jobs.Count == 0) throw new ArgumentException("No hay PDF pendientes para firmar.");
        var sources = jobs.Select(j => Path.GetFullPath(j.Source)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in jobs)
        {
            string path = Path.GetFullPath(job.Destination);
            if (sources.Contains(path)) throw new IOException("El destino coincide con un original. Se conservarán todos los originales.");
            if (!destinations.Add(path)) throw new IOException("Dos PDF tienen el mismo destino. Abre cada original una sola vez.");
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException($"Ya existe {path}. Mueve o renombra esa copia antes de firmar. No se ha iniciado el lote.");
            if (!Directory.Exists(Path.GetDirectoryName(path))) throw new DirectoryNotFoundException("No existe la carpeta de destino: " + path);
        }
    }
    public static IReadOnlyList<SignedDocument> Sign(IReadOnlyList<SigningJob> jobs, CertificateIdentity identity, ITSAClient? tsa = null, Action<int,int>? progress = null)
    {
        CheckDestinations(jobs); identity.CheckUsable();
        var staged = new List<(string Path, SignedDocument Result)>();
        var completed = new List<SignedDocument>();
        try
        {
            for (int index=0; index<jobs.Count; index++)
            {
                var job=jobs[index];
                string temp=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(job.Destination))!, $".firmapdf-lote-{Guid.NewGuid():N}.pdf");
                try
                {
                    progress?.Invoke(index+1,jobs.Count);
                    PdfEngine.Sign(job.Source,temp,job.Placement,identity,job.Design,tsa);
                    staged.Add((temp,new(job,PdfEngine.Verify(temp))));
                }
                catch { if(File.Exists(temp)) File.Delete(temp); throw; }
            }
            // Only publish after every signature has passed verification. Never replace an existing file.
            foreach(var item in staged)
            { File.Move(item.Path,item.Result.Job.Destination,overwrite:false); completed.Add(item.Result); }
            return completed;
        }
        catch(Exception ex)
        {
            throw new BatchSigningException(completed.Count == 0 ? $"No se han guardado copias del lote. {ex.Message}" : $"Se guardaron {completed.Count} copias; el resto no se guardó. {ex.Message}",completed.ToArray(),ex);
        }
        finally { foreach(var item in staged) if(File.Exists(item.Path)) File.Delete(item.Path); }
    }
}
