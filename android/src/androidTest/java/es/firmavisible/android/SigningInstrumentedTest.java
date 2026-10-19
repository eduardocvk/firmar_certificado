package es.firmavisible.android;

import android.content.Context;
import android.graphics.*;
import android.graphics.pdf.PdfRenderer;
import android.os.ParcelFileDescriptor;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import com.tom_roush.pdfbox.cos.*;
import com.tom_roush.pdfbox.pdmodel.*;
import com.tom_roush.pdfbox.pdmodel.common.PDRectangle;
import com.tom_roush.pdfbox.android.PDFBoxResourceLoader;
import es.firmavisible.core.Identity;
import es.firmavisible.core.Placement;
import es.firmavisible.pdf.PdfEngine;
import org.bouncycastle.asn1.x500.X500Name;
import org.bouncycastle.cert.jcajce.*;
import org.bouncycastle.operator.jcajce.JcaContentSignerBuilder;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.io.*;
import java.math.BigInteger;
import java.security.*;
import java.security.cert.X509Certificate;
import java.util.*;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class SigningInstrumentedTest {
    private void diagnostic(Context context,String name,String mime,byte[] bytes,boolean image)throws IOException {
        android.content.ContentValues values=new android.content.ContentValues();
        values.put(android.provider.MediaStore.MediaColumns.DISPLAY_NAME,name);
        values.put(android.provider.MediaStore.MediaColumns.MIME_TYPE,mime);
        values.put(android.provider.MediaStore.MediaColumns.RELATIVE_PATH,image?"Pictures/FirmaVisibleTests":"Download/FirmaVisibleTests");
        android.net.Uri collection=image?android.provider.MediaStore.Images.Media.EXTERNAL_CONTENT_URI:android.provider.MediaStore.Downloads.EXTERNAL_CONTENT_URI;
        android.net.Uri uri=context.getContentResolver().insert(collection,values);
        if(uri==null)throw new IOException("No se pudo guardar el diagnóstico de prueba.");
        try(OutputStream out=context.getContentResolver().openOutputStream(uri)){if(out==null)throw new IOException("No se pudo escribir el diagnóstico.");out.write(bytes);}
    }
    @Test public void existingFormValuesAreVisibleWithoutChangingTheOriginal() throws Exception {
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();PDFBoxResourceLoader.init(context);
        File directory=new File(context.getCacheDir(),"form-test-"+UUID.randomUUID());assertTrue(directory.mkdirs());
        File input=new File(directory,"form.pdf");
        try {
            try(PDDocument doc=new PDDocument()) {
                PDPage page=new PDPage(PDRectangle.A4);doc.addPage(page);
                var form=new com.tom_roush.pdfbox.pdmodel.interactive.form.PDAcroForm(doc);doc.getDocumentCatalog().setAcroForm(form);
                var field=new com.tom_roush.pdfbox.pdmodel.interactive.form.PDTextField(form);field.setPartialName("DatoExistente");form.getFields().add(field);
                var widget=field.getWidgets().get(0);widget.setPage(page);widget.setRectangle(new PDRectangle(100,600,200,40));page.getAnnotations().add(widget);
                var appearance=new com.tom_roush.pdfbox.pdmodel.interactive.annotation.PDAppearanceStream(doc);appearance.setBBox(new PDRectangle(200,40));appearance.setResources(new PDResources());
                try(PDPageContentStream cs=new PDPageContentStream(doc,appearance)){cs.setNonStrokingColor(1f,1f,0f);cs.addRect(0,0,200,40);cs.fill();cs.setNonStrokingColor(0f,0f,0f);cs.beginText();cs.setFont(com.tom_roush.pdfbox.pdmodel.font.PDType1Font.HELVETICA,12);cs.newLineAtOffset(5,12);cs.showText("Dato de formulario");cs.endText();}
                var ap=new com.tom_roush.pdfbox.pdmodel.interactive.annotation.PDAppearanceDictionary();ap.setNormalAppearance(appearance);widget.setAppearance(ap);doc.save(input);
            }
            byte[] original=java.nio.file.Files.readAllBytes(input.toPath());File display=PreviewDocument.prepare(input,directory);
            assertNotEquals(input.getCanonicalPath(),display.getCanonicalPath());assertArrayEquals(original,java.nio.file.Files.readAllBytes(input.toPath()));
            try(ParcelFileDescriptor fd=ParcelFileDescriptor.open(display,ParcelFileDescriptor.MODE_READ_ONLY);PdfRenderer renderer=new PdfRenderer(fd);PdfRenderer.Page page=renderer.openPage(0)) {
                Bitmap bitmap=Bitmap.createBitmap(page.getWidth(),page.getHeight(),Bitmap.Config.ARGB_8888);bitmap.eraseColor(Color.WHITE);page.render(bitmap,null,null,PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
                int pixel=bitmap.getPixel(110,page.getHeight()-630);assertTrue("Formulario visible en la vista previa",Color.red(pixel)>200&&Color.green(pixel)>200&&Color.blue(pixel)<100);bitmap.recycle();
            }
        } finally {File[] files=directory.listFiles();if(files!=null)for(File file:files)file.delete();directory.delete();}
    }
    @Test public void importedCertificateSignsVisiblePdfInEveryOrientation() throws Exception {
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();PDFBoxResourceLoader.init(context);
        KeyPairGenerator generator=KeyPairGenerator.getInstance("RSA");generator.initialize(2048);KeyPair pair=generator.generateKeyPair();
        X500Name name=new X500Name("CN=Android Test - NO REAL");long now=System.currentTimeMillis();
        var certBuilder=new JcaX509v3CertificateBuilder(name,BigInteger.valueOf(now),new Date(now-60000),new Date(now+3600000),name,pair.getPublic());
        X509Certificate cert=new JcaX509CertificateConverter().getCertificate(certBuilder.build(new JcaContentSignerBuilder("SHA256withRSA").build(pair.getPrivate())));
        KeyStore store=KeyStore.getInstance("PKCS12");store.load(null,null);char[] password="test-only".toCharArray();
        store.setKeyEntry("test",pair.getPrivate(),password,new java.security.cert.Certificate[]{cert});
        ByteArrayOutputStream p12=new ByteArrayOutputStream();store.store(p12,password);
        Identity identity=Identity.fromPkcs12(new ByteArrayInputStream(p12.toByteArray()),password).get(0);
        File directory=new File(context.getCacheDir(),"test-"+UUID.randomUUID());assertTrue(directory.mkdirs());
        try {
            for(int rotation:new int[]{0,90,180,270}){
                File input=new File(directory,"input.pdf"),output=new File(directory,"signed.pdf");
                try(PDDocument doc=new PDDocument()) { PDPage page=new PDPage(new PDRectangle(650,850));page.setCropBox(new PDRectangle(20,30,600,780));page.setRotation(rotation);doc.addPage(page);doc.save(input); }
                PdfEngine.sign(input,output,new Placement(0,.15f,.55f,.5f,.15f),identity);
                assertEquals(1,PdfEngine.verify(output).signatures);assertTrue(PdfEngine.verify(output).latestCoversWholeFile);
                try(PDDocument check=PDDocument.load(output)){assertEquals("Campo de firma registrado en la página",1,check.getPage(0).getAnnotations().size());}
                byte[] signedBytes=java.nio.file.Files.readAllBytes(output.toPath());
                File display=PreviewDocument.prepare(output,directory);
                assertNotEquals(output.getCanonicalPath(),display.getCanonicalPath());
                assertArrayEquals(signedBytes,java.nio.file.Files.readAllBytes(output.toPath()));
                try(ParcelFileDescriptor fd=ParcelFileDescriptor.open(display,ParcelFileDescriptor.MODE_READ_ONLY);PdfRenderer renderer=new PdfRenderer(fd);PdfRenderer.Page page=renderer.openPage(0)) {
                    Bitmap bitmap=Bitmap.createBitmap(page.getWidth(),page.getHeight(),Bitmap.Config.ARGB_8888);bitmap.eraseColor(Color.WHITE);page.render(bitmap,null,null,PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
                    ByteArrayOutputStream image=new ByteArrayOutputStream();bitmap.compress(Bitmap.CompressFormat.PNG,100,image);
                    diagnostic(context,"rotation-"+rotation+".png","image/png",image.toByteArray(),true);
                    diagnostic(context,"signed-"+rotation+".pdf","application/pdf",signedBytes,false);
                    diagnostic(context,"display-"+rotation+".pdf","application/pdf",java.nio.file.Files.readAllBytes(display.toPath()),false);
                    int ink=0;for(int y=(int)(bitmap.getHeight()*.55);y<(int)(bitmap.getHeight()*.7);y++)for(int x=(int)(bitmap.getWidth()*.15);x<(int)(bitmap.getWidth()*.65);x++){int pixel=bitmap.getPixel(x,y);if(Color.red(pixel)<160&&Color.green(pixel)<160)ink++;}assertTrue("Visible signature text at rotation "+rotation,ink>50);bitmap.recycle();
                }
                assertTrue(PdfEngine.verify(output).latestCoversWholeFile);assertTrue(display.delete());
                assertTrue(output.delete());assertTrue(input.delete());
            }
        } finally { File[] files=directory.listFiles();if(files!=null)for(File file:files)file.delete();directory.delete(); }
    }
}
