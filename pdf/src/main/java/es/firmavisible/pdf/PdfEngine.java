package es.firmavisible.pdf;

import es.firmavisible.core.*;
import java.io.*;
import java.nio.file.*;
import java.time.*;
import java.time.format.DateTimeFormatter;
import java.util.*;
import org.apache.pdfbox.cos.*;
import org.apache.pdfbox.pdmodel.*;
import org.apache.pdfbox.pdmodel.common.PDRectangle;
import org.apache.pdfbox.pdmodel.font.PDType1Font;
import org.apache.pdfbox.pdmodel.interactive.annotation.*;
import org.apache.pdfbox.pdmodel.interactive.digitalsignature.*;
import org.apache.pdfbox.pdmodel.interactive.form.*;
import org.bouncycastle.cms.*;
import org.bouncycastle.cms.jcajce.JcaSimpleSignerInfoVerifierBuilder;
import org.bouncycastle.cert.X509CertificateHolder;
import org.bouncycastle.jce.provider.BouncyCastleProvider;
import org.bouncycastle.operator.jcajce.JcaDigestCalculatorProviderBuilder;

/** Shared verbatim with Android through a namespace-only generated source task. */
public final class PdfEngine {
    private PdfEngine() {}
    public static final String VERIFICATION_NOTICE = "Integridad criptográfica comprobada. Confianza, revocación y cualificación no comprobadas. Sin sello de tiempo.";

    public static final class Verification {
        public final int signatures;
        public final boolean latestCoversWholeFile;
        Verification(int count, boolean whole) { signatures = count; latestCoversWholeFile = whole; }
        @Override public String toString() { return signatures + " firma(s). " + VERIFICATION_NOTICE
            + (latestCoversWholeFile ? "" : " Hay cambios posteriores a la última firma."); }
    }

    public static void sign(File input, File output, Placement placement, Identity identity) throws Exception {
        identity.checkUsable();
        if (input.getCanonicalFile().equals(output.getCanonicalFile())) throw new IOException("Guarda una copia; no sobrescribas el original.");
        if (output.exists()) throw new IOException("El archivo de destino ya existe. Elige otro nombre.");
        Path temp = Files.createTempFile(output.toPath().toAbsolutePath().getParent(), ".firma-", ".pdf");
        try {
            try (PDDocument doc = PDDocument.load(input); SignatureOptions options = new SignatureOptions()) {
                checkDocument(doc);
                if (placement.page >= doc.getNumberOfPages()) throw new IOException("La página ya no existe.");
                if (!doc.getSignatureDictionaries().isEmpty()) {
                    Verification previous = verify(input);
                    if (!previous.latestCoversWholeFile) throw new IOException("El PDF tiene cambios posteriores a la última firma. Revísalo antes de volver a firmar.");
                }
                PDPage page = doc.getPage(placement.page);
                PDRectangle crop = page.getCropBox();
                float[] rect = placement.pdfRectangle(crop.getLowerLeftX(), crop.getLowerLeftY(), crop.getWidth(), crop.getHeight(), page.getRotation());
                int rotation = Math.floorMod(page.getRotation(), 360);
                float visualW = rotation % 180 == 0 ? rect[2] : rect[3];
                float visualH = rotation % 180 == 0 ? rect[3] : rect[2];
                if (visualW < 100 || visualH < 42) throw new IOException("El recuadro es demasiado pequeño: mínimo 100 × 42 puntos PDF.");
                Calendar now = Calendar.getInstance();
                PDSignature signature = new PDSignature();
                signature.setFilter(PDSignature.FILTER_ADOBE_PPKLITE);
                signature.setSubFilter(PDSignature.SUBFILTER_ETSI_CADES_DETACHED);
                signature.setName(identity.name());
                signature.setSignDate(now);
                options.setPage(placement.page);
                options.setPreferredSignatureSize(32768);
                doc.addSignature(signature, stream -> {
                    try { return CmsSigner.sign(stream, identity); }
                    catch (Exception e) { throw new IOException("No se pudo utilizar la clave del certificado.", e); }
                }, options);
                PDSignatureField field = null;
                for (PDField item : doc.getDocumentCatalog().getAcroForm().getFieldTree()) {
                    if (item instanceof PDSignatureField && ((PDSignatureField)item).getSignature() != null
                        && ((PDSignatureField)item).getSignature().getCOSObject().equals(signature.getCOSObject())) {
                        field = (PDSignatureField)item; break;
                    }
                }
                if (field == null) throw new IOException("No se pudo crear el campo de firma.");
                field.setPartialName("FirmaVisible_" + UUID.randomUUID().toString().replace("-", ""));
                PDAnnotationWidget widget = field.getWidgets().get(0);
                widget.setRectangle(new PDRectangle(rect[0], rect[1], rect[2], rect[3]));
                widget.setPrinted(true);
                widget.setPage(page);
                PDAppearanceStream appearance = new PDAppearanceStream(doc);
                appearance.setResources(new PDResources());
                appearance.setBBox(new PDRectangle(visualW, visualH));
                float[] matrix;
                switch (rotation) {
                    case 90: matrix = new float[]{0,1,-1,0,rect[2],0}; break;
                    case 180: matrix = new float[]{-1,0,0,-1,rect[2],rect[3]}; break;
                    case 270: matrix = new float[]{0,-1,1,0,0,rect[3]}; break;
                    default: matrix = new float[]{1,0,0,1,0,0};
                }
                COSArray transform = new COSArray();
                for (float value : matrix) transform.add(new COSFloat(value));
                appearance.getCOSObject().setItem(COSName.MATRIX, transform);
                drawAppearance(doc, appearance, visualW, visualH, identity.name(), now.toInstant());
                PDAppearanceDictionary ap = new PDAppearanceDictionary();
                ap.setNormalAppearance(appearance); widget.setAppearance(ap);
                widget.getCOSObject().setNeedToBeUpdated(true);
                page.getCOSObject().setNeedToBeUpdated(true);
                try (OutputStream out = Files.newOutputStream(temp)) { doc.saveIncremental(out); }
            }
            Verification result = verify(temp.toFile());
            if (result.signatures == 0 || !result.latestCoversWholeFile) throw new IOException("La firma generada no supera la verificación.");
            Files.move(temp, output.toPath());
        } finally { Files.deleteIfExists(temp); }
    }

    private static void checkDocument(PDDocument doc) throws IOException {
        if (doc.isEncrypted()) throw new IOException("Esta versión no firma PDF cifrados. Usa una copia sin cifrado.");
        if (doc.getNumberOfPages() == 0) throw new IOException("El PDF no tiene páginas.");
        COSDictionary permissions = doc.getDocumentCatalog().getCOSObject().getCOSDictionary(COSName.PERMS);
        if (permissions != null && permissions.containsKey(COSName.DOCMDP))
            throw new IOException("Este PDF está certificado y tiene restricciones de modificación. No se ha modificado.");
        PDAcroForm form = doc.getDocumentCatalog().getAcroForm(null);
        if (form != null) for (PDField field : form.getFieldTree()) {
            if (field.getCOSObject().containsKey(COSName.getPDFName("Lock")))
                throw new IOException("El PDF contiene campos con bloqueo de firma; esta versión no los modifica.");
        }
    }

    private static String printable(String value) {
        StringBuilder result = new StringBuilder();
        for (int i = 0; i < value.length(); i++) {
            String ch = value.substring(i, i+1);
            try { PDType1Font.HELVETICA.encode(ch); result.append(ch); }
            catch (Exception e) { result.append('?'); }
        }
        return result.toString();
    }
    private static void drawAppearance(PDDocument doc, PDAppearanceStream appearance, float width, float height, String name, Instant time) throws IOException {
        float size = Math.min(10, (height-12)/4.6f);
        String title = "Firmado digitalmente";
        String date = DateTimeFormatter.ofPattern("dd/MM/yyyy HH:mm XXX").withZone(ZoneId.systemDefault()).format(time);
        String clean = printable(name);
        float max = width-12;
        List<String> names = new ArrayList<>();
        String line = "";
        for (String word : clean.split("\\s+")) {
            String next = line.isEmpty() ? word : line + " " + word;
            if (PDType1Font.HELVETICA.getStringWidth(next)/1000*size > max && !line.isEmpty()) { names.add(line); line = word; }
            else line = next;
        }
        names.add(line);
        String first = names.get(0);
        String second = names.size() > 1 ? String.join(" ", names.subList(1, names.size())) : "";
        String[] lines = {title, first, second, date};
        try (PDPageContentStream cs = new PDPageContentStream(doc, appearance)) {
            cs.setNonStrokingColor(.95f, .97f, 1f); cs.addRect(0,0,width,height); cs.fill();
            cs.setStrokingColor(.16f,.35f,.65f); cs.setLineWidth(.7f); cs.addRect(.5f,.5f,width-1,height-1); cs.stroke();
            cs.setNonStrokingColor(.08f,.16f,.28f);
            for (int i=0; i<lines.length; i++) {
                float measured = PDType1Font.HELVETICA.getStringWidth(lines[i])/1000;
                float lineSize = measured == 0 ? size : Math.min(size, max/measured);
                cs.beginText(); cs.setFont(PDType1Font.HELVETICA, lineSize);
                cs.newLineAtOffset(6, height-7-size-i*size*1.1f); cs.showText(lines[i]); cs.endText();
            }
        }
    }

    /** Checks CMS and PDF byte ranges, NOT PKI trust, revocation or legal qualification. */
    @SuppressWarnings("unchecked")
    public static Verification verify(File file) throws Exception {
        try (PDDocument doc = PDDocument.load(file)) {
            int count = 0; long latestEnd = 0;
            for (PDSignature sig : doc.getSignatureDictionaries()) {
                int[] r = sig.getByteRange();
                if (r == null || r.length != 4 || r[0] != 0 || r[1] <= 0 || r[2] <= r[1] || r[3] < 0 || (long)r[2]+r[3] > file.length())
                    throw new IOException("ByteRange de firma incorrecto.");
                // Only /Contents may be excluded from the signed revision.
                try (RandomAccessFile raf = new RandomAccessFile(file, "r")) {
                    raf.seek(r[1]); if (raf.read() != '<') throw new IOException("Hueco de firma incorrecto.");
                    if ((long)r[2]-r[1] > 2*1024*1024) throw new IOException("Contenido de firma excesivo.");
                    byte[] gap = new byte[32768];
                    long remaining = (long)r[2]-r[1]-2;
                    while (remaining>0) {
                        int n=raf.read(gap,0,(int)Math.min(gap.length,remaining));
                        if(n<0)throw new EOFException("PDF truncado.");
                        for(int i=0;i<n;i++) {
                            int c=gap[i]&255;
                            if (!((c>='0'&&c<='9') || (c>='a'&&c<='f') || (c>='A'&&c<='F') || c==0 || c==9 || c==10 || c==12 || c==13 || c==32))
                                throw new IOException("El hueco de firma contiene datos ajenos a /Contents.");
                        }
                        remaining-=n;
                    }
                    raf.seek(r[2]-1L); if (raf.read() != '>') throw new IOException("Hueco de firma incorrecto.");
                }
                byte[] contents;
                try (InputStream source = new FileInputStream(file)) { contents = sig.getContents(source); }
                try (InputStream ranges = new Ranges(file, r)) {
                    var parser = new CMSSignedDataParser(new JcaDigestCalculatorProviderBuilder().setProvider(new BouncyCastleProvider()).build(), new CMSTypedStream(ranges), contents);
                    try {
                        parser.getSignedContent().drain();
                        var certificates = parser.getCertificates();
                        var signers = parser.getSignerInfos().getSigners();
                        if (signers.isEmpty()) throw new IOException("Firma CMS sin firmante.");
                        for (SignerInformation signer : signers) {
                            Collection<X509CertificateHolder> matches = certificates.getMatches(signer.getSID());
                            if (matches.isEmpty() || !signer.verify(new JcaSimpleSignerInfoVerifierBuilder().setProvider(new BouncyCastleProvider()).build(matches.iterator().next())))
                                throw new IOException("La integridad de una firma no es correcta.");
                        }
                    } finally { parser.close(); }
                }
                latestEnd = Math.max(latestEnd, (long)r[2]+r[3]); count++;
            }
            return new Verification(count, count > 0 && latestEnd == file.length());
        }
    }
    private static final class Ranges extends InputStream {
        private final RandomAccessFile file;
        private final int[] ranges;
        private int segment = 0;
        private long remaining;
        Ranges(File source, int[] ranges) throws IOException { file = new RandomAccessFile(source,"r"); this.ranges = ranges; remaining = ranges[1]; }
        @Override public int read() throws IOException { byte[] b=new byte[1]; return read(b,0,1)==-1 ? -1 : b[0]&255; }
        @Override public int read(byte[] b, int off, int len) throws IOException {
            if (len == 0) return 0;
            if (remaining == 0) { if (segment == 1) return -1; segment=1; file.seek(ranges[2]); remaining=ranges[3]; if (remaining==0) return -1; }
            int n=file.read(b,off,(int)Math.min(remaining,len));
            if(n<0) throw new EOFException("PDF truncado."); remaining-=n; return n;
        }
        @Override public void close() throws IOException { file.close(); }
    }
}
