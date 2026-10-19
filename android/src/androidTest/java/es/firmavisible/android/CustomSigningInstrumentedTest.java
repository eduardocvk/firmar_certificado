package es.firmavisible.android;

import android.content.*;
import android.content.pm.ActivityInfo;
import android.graphics.*;
import android.graphics.pdf.PdfRenderer;
import android.os.*;
import android.view.*;
import androidx.core.content.FileProvider;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import com.tom_roush.pdfbox.android.PDFBoxResourceLoader;
import com.tom_roush.pdfbox.pdmodel.*;
import com.tom_roush.pdfbox.pdmodel.common.PDRectangle;
import es.firmavisible.core.*;
import es.firmavisible.core.Identity;
import es.firmavisible.pdf.PdfEngine;
import es.firmavisible.test.TestTsa;
import java.io.*;
import java.math.BigInteger;
import java.security.*;
import java.security.cert.X509Certificate;
import java.util.*;
import org.bouncycastle.asn1.x500.X500Name;
import org.bouncycastle.cert.jcajce.*;
import org.bouncycastle.operator.jcajce.JcaContentSignerBuilder;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class CustomSigningInstrumentedTest {
    private Context context(){return InstrumentationRegistry.getInstrumentation().getTargetContext();}
    private Identity identity()throws Exception {
        KeyPairGenerator generator=KeyPairGenerator.getInstance("RSA");generator.initialize(2048);KeyPair pair=generator.generateKeyPair();
        X500Name name=new X500Name("CN=Firma personalizada TEST - NO REAL");long now=System.currentTimeMillis();
        var builder=new JcaX509v3CertificateBuilder(name,BigInteger.ONE,new Date(now-60000),new Date(now+86400000),name,pair.getPublic());
        X509Certificate cert=new JcaX509CertificateConverter().getCertificate(builder.build(new JcaContentSignerBuilder("SHA256withRSA").build(pair.getPrivate())));
        return new Identity(pair.getPrivate(),new X509Certificate[]{cert});
    }
    private byte[] redImage(){Bitmap bitmap=Bitmap.createBitmap(180,60,Bitmap.Config.ARGB_8888);bitmap.eraseColor(Color.RED);ByteArrayOutputStream out=new ByteArrayOutputStream();bitmap.compress(Bitmap.CompressFormat.PNG,100,out);bitmap.recycle();return out.toByteArray();}
    private byte[] exampleImage(){Bitmap bitmap=Bitmap.createBitmap(240,90,Bitmap.Config.ARGB_8888);Canvas canvas=new Canvas(bitmap);Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);paint.setColor(0xFF2848A8);paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(3);Path path=new Path();path.moveTo(20,62);path.cubicTo(100,-10,55,100,72,42);path.cubicTo(95,10,78,85,109,48);path.cubicTo(145,10,110,83,155,38);path.cubicTo(190,11,140,95,214,48);canvas.drawPath(path,paint);canvas.drawLine(23,78,220,65,paint);ByteArrayOutputStream out=new ByteArrayOutputStream();bitmap.compress(Bitmap.CompressFormat.PNG,100,out);bitmap.recycle();return out.toByteArray();}
    private void diagnostic(String name,String mime,byte[] bytes,boolean image)throws IOException {
        ContentValues values=new ContentValues();values.put(android.provider.MediaStore.MediaColumns.DISPLAY_NAME,name);values.put(android.provider.MediaStore.MediaColumns.MIME_TYPE,mime);values.put(android.provider.MediaStore.MediaColumns.RELATIVE_PATH,image?"Pictures/FirmaVisibleTests":"Download/FirmaVisibleTests");
        var uri=context().getContentResolver().insert(image?android.provider.MediaStore.Images.Media.EXTERNAL_CONTENT_URI:android.provider.MediaStore.Downloads.EXTERNAL_CONTENT_URI,values);assertNotNull(uri);
        try(OutputStream out=context().getContentResolver().openOutputStream(uri)){assertNotNull(out);out.write(bytes);}
    }
    @Test public void customizedImageTextAndTimestampRenderAtEveryRotation()throws Exception {
        PDFBoxResourceLoader.init(context());Identity identity=identity();TestTsa tsa=new TestTsa();
        File directory=new File(context().getCacheDir(),"custom-test-"+UUID.randomUUID());assertTrue(directory.mkdirs());
        try {
            for(int rotation:new int[]{0,90,180,270}) {
                File input=new File(directory,"original.pdf"),output=new File(directory,"signed.pdf");
                try(PDDocument doc=new PDDocument()){var page=new PDPage(new PDRectangle(650,850));page.setCropBox(new PDRectangle(20,30,600,780));page.setRotation(rotation);doc.addPage(page);doc.save(input);}
                byte[] original=java.nio.file.Files.readAllBytes(input.toPath());
                PdfEngine.sign(input,output,new Placement(0,.15f,.55f,.5f,.15f),identity,new SignatureAppearance("Revisado por {nombre}\nÁrea técnica · Málaga\n{fecha}",redImage(),true),tsa.client());
                var verification=PdfEngine.verify(output);assertEquals(1,verification.timestampedSignatures);assertNotNull(verification.latestTimestamp);assertTrue(verification.latestCoversWholeFile);assertArrayEquals(original,java.nio.file.Files.readAllBytes(input.toPath()));
                File display=PreviewDocument.prepare(output,directory);
                try(ParcelFileDescriptor fd=ParcelFileDescriptor.open(display,ParcelFileDescriptor.MODE_READ_ONLY);PdfRenderer renderer=new PdfRenderer(fd);PdfRenderer.Page page=renderer.openPage(0)) {
                    Bitmap bitmap=Bitmap.createBitmap(page.getWidth(),page.getHeight(),Bitmap.Config.ARGB_8888);bitmap.eraseColor(Color.WHITE);page.render(bitmap,null,null,PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);int red=0;
                    for(int y=(int)(bitmap.getHeight()*.55);y<(int)(bitmap.getHeight()*.7);y++)for(int x=(int)(bitmap.getWidth()*.15);x<(int)(bitmap.getWidth()*.65);x++){int c=bitmap.getPixel(x,y);if(Color.red(c)>220&&Color.green(c)<40&&Color.blue(c)<40)red++;}
                    assertTrue("Imagen personalizada visible en rotación "+rotation,red>100);
                    ByteArrayOutputStream image=new ByteArrayOutputStream();bitmap.compress(Bitmap.CompressFormat.PNG,100,image);diagnostic("custom-"+rotation+".png","image/png",image.toByteArray(),true);bitmap.recycle();
                }
                diagnostic("custom-"+rotation+".pdf","application/pdf",java.nio.file.Files.readAllBytes(output.toPath()),false);assertTrue(output.delete());
            }
        } finally {File[] files=directory.listFiles();if(files!=null)for(File file:files)file.delete();directory.delete();}
    }
    @Test public void importingAnImageBoundsSizeAndStoresOnlyTheDesign()throws Exception {
        SignatureAppearance previous=AppearanceSettings.load(context());
        try {
            Bitmap large=Bitmap.createBitmap(4096,2048,Bitmap.Config.ARGB_8888);large.eraseColor(Color.TRANSPARENT);new Canvas(large).drawColor(Color.argb(120,20,40,80));ByteArrayOutputStream out=new ByteArrayOutputStream();large.compress(Bitmap.CompressFormat.PNG,100,out);large.recycle();
            byte[] clean=AppearanceSettings.importImage(new ByteArrayInputStream(out.toByteArray()));Bitmap decoded=BitmapFactory.decodeByteArray(clean,0,clean.length);assertTrue(decoded.getWidth()<=1200);assertTrue(decoded.getHeight()<=1200);assertTrue(Color.alpha(decoded.getPixel(0,0))<255);decoded.recycle();
            SignatureAppearance custom=new SignatureAppearance("Área técnica\n{nombre}",clean,false);AppearanceSettings.save(context(),custom);SignatureAppearance loaded=AppearanceSettings.load(context());assertEquals(custom.template,loaded.template);assertFalse(loaded.decorated);assertArrayEquals(clean,loaded.image());
            AppearanceSettings.save(context(),SignatureAppearance.standard());assertNull(AppearanceSettings.load(context()).image());
            try{AppearanceSettings.importImage(new ByteArrayInputStream(new byte[]{1,2,3}));fail("Imagen no válida aceptada");}catch(IOException expected){}
        } finally {AppearanceSettings.save(context(),previous);}
    }
    @android.annotation.TargetApi(29)
    @Test public void applicationAndEditorRemainUsableInBothOrientations()throws Exception {
        var instrumentation=InstrumentationRegistry.getInstrumentation();Context context=context();PDFBoxResourceLoader.init(context);
        SignatureAppearance previous=AppearanceSettings.load(context);MainActivity activity=null;
        File folder=new File(context.getCacheDir(),"signed");assertTrue(folder.isDirectory()||folder.mkdirs());File source=new File(folder,"diseno-demostracion.pdf");
        try {
            AppearanceSettings.save(context,new SignatureAppearance("Firmado por {nombre}\n{fecha}",exampleImage(),true));
            try(PDDocument doc=new PDDocument()){PDPage page=new PDPage(PDRectangle.A4);doc.addPage(page);try(PDPageContentStream stream=new PDPageContentStream(doc,page)){stream.beginText();stream.setFont(com.tom_roush.pdfbox.pdmodel.font.PDType1Font.HELVETICA_BOLD,22);stream.newLineAtOffset(55,760);stream.showText("Documento de demostracion");stream.endText();stream.setNonStrokingColor(.85f,.88f,.94f);for(int i=0;i<11;i++){stream.addRect(55,680-i*28,480-(i%3)*60,5);stream.fill();}}doc.save(source);}
            var uri=FileProvider.getUriForFile(context,context.getPackageName()+".files",source);
            Intent intent=new Intent(context,MainActivity.class).setAction(Intent.ACTION_VIEW).setDataAndType(uri,"application/pdf").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_GRANT_READ_URI_PERMISSION);
            activity=(MainActivity)instrumentation.startActivitySync(intent);final MainActivity current=activity;
            for(int i=0;i<80;i++){instrumentation.waitForIdleSync();final boolean[] ready={false};instrumentation.runOnMainSync(()->{View view=current.getWindow().getDecorView().findViewWithTag("pdf-preview");ready[0]=view!=null&&view.isEnabled();});if(ready[0])break;SystemClock.sleep(100);}
            instrumentation.runOnMainSync(()->{PdfSelectionView view=current.getWindow().getDecorView().findViewWithTag("pdf-preview");assertTrue(view.getHeight()>100);view.resetBox();});instrumentation.waitForIdleSync();capture("android-design-portrait.png");
            instrumentation.runOnMainSync(()->current.setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE));SystemClock.sleep(700);instrumentation.waitForIdleSync();
            instrumentation.runOnMainSync(()->{View view=current.getWindow().getDecorView().findViewWithTag("pdf-preview");assertTrue(view.getWidth()>100);assertTrue(view.getHeight()>100);});capture("android-design-landscape.png");
            instrumentation.runOnMainSync(()->current.setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_PORTRAIT));SystemClock.sleep(700);instrumentation.waitForIdleSync();
            instrumentation.runOnMainSync(()->current.getWindow().getDecorView().findViewWithTag("customize").performClick());instrumentation.waitForIdleSync();SystemClock.sleep(350);capture("android-appearance-editor.png");
            instrumentation.runOnMainSync(()->{boolean found=false;for(View root:android.view.inspector.WindowInspector.getGlobalWindowViews()){android.widget.EditText field=root.findViewWithTag("appearance-text");if(field!=null){field.setText("Revisado y aprobado\n{nombre}\n{fecha}");root.findViewWithTag("save-design").performClick();found=true;break;}}assertTrue("Editor accesible",found);});
            for(int i=0;i<50;i++){if(AppearanceSettings.load(context).template.startsWith("Revisado y aprobado"))break;SystemClock.sleep(100);}
            assertTrue("El diseño editado se conserva",AppearanceSettings.load(context).template.startsWith("Revisado y aprobado"));
        } finally {if(activity!=null){final MainActivity close=activity;instrumentation.runOnMainSync(close::finish);}AppearanceSettings.save(context,previous);source.delete();}
    }
    private void capture(String name)throws IOException {Bitmap screenshot=InstrumentationRegistry.getInstrumentation().getUiAutomation().takeScreenshot();assertNotNull(screenshot);ByteArrayOutputStream out=new ByteArrayOutputStream();screenshot.compress(Bitmap.CompressFormat.PNG,100,out);diagnostic(name,"image/png",out.toByteArray(),true);screenshot.recycle();}
}
