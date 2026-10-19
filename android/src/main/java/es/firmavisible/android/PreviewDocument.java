package es.firmavisible.android;

import com.tom_roush.pdfbox.pdmodel.PDDocument;
import com.tom_roush.pdfbox.cos.COSName;
import com.tom_roush.pdfbox.pdmodel.interactive.form.*;
import java.io.*;
import java.util.*;

/** Display-only copy: Android's legacy PdfRenderer omits form widgets. */
public final class PreviewDocument {
    private PreviewDocument() {}
    public static File prepare(File original, File directory) throws IOException {
        try (PDDocument doc=PDDocument.load(original)) {
            PDAcroForm form=doc.getDocumentCatalog().getAcroForm(null);
            if(form==null)return original;
            if(form.getCOSObject().containsKey(COSName.XFA))throw new IOException("Los formularios XFA no son compatibles con esta versión.");
            List<PDField> fields=new ArrayList<>();
            for(PDField field:form.getFieldTree())fields.add(field);
            if(fields.isEmpty())return original;
            File preview=File.createTempFile("preview-only-",".pdf",directory);
            try {
                // The signed original is never saved or flattened. Only this disposable
                // rendering copy is changed; it must never be signed, exported or verified.
                form.flatten(fields,form.getNeedAppearances());
                doc.save(preview);
                return preview;
            } catch(IOException|RuntimeException e) { preview.delete();throw e; }
        }
    }
}
