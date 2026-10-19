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
                try(ParcelFileDescriptor fd=ParcelFileDescriptor.open(output,ParcelFileDescriptor.MODE_READ_ONLY);PdfRenderer renderer=new PdfRenderer(fd);PdfRenderer.Page page=renderer.openPage(0)) {
                    Bitmap bitmap=Bitmap.createBitmap(page.getWidth(),page.getHeight(),Bitmap.Config.ARGB_8888);bitmap.eraseColor(Color.WHITE);page.render(bitmap,null,null,PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
                    int pixel=bitmap.getPixel(Math.round(bitmap.getWidth()*.17f),Math.round(bitmap.getHeight()*.68f));assertTrue("Visible signature at rotation "+rotation,Color.blue(pixel)>Color.red(pixel)&&Color.red(pixel)>200);bitmap.recycle();
                }
                assertTrue(output.delete());assertTrue(input.delete());
            }
        } finally { File[] files=directory.listFiles();if(files!=null)for(File file:files)file.delete();directory.delete(); }
    }
}
