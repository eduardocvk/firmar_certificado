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
            SignatureAppearance custom=new SignatureAppearance("Área técnica\n{nombre}",clean,false,6.5f);AppearanceSettings.save(context(),custom);SignatureAppearance loaded=AppearanceSettings.load(context());assertEquals(custom.template,loaded.template);assertFalse(loaded.decorated);assertEquals(6.5f,loaded.textSize,0);assertArrayEquals(clean,loaded.image());
            context().getSharedPreferences("appearance",Context.MODE_PRIVATE).edit().remove("designVersion").remove("textSize").putBoolean("decorated",true).commit();SignatureAppearance legacy=AppearanceSettings.load(context());assertEquals(custom.template,legacy.template);assertArrayEquals(clean,legacy.image());assertFalse(legacy.decorated);assertEquals(8f,legacy.textSize,0);
            AppearanceSettings.save(context(),SignatureAppearance.standard());assertNull(AppearanceSettings.load(context()).image());
            try{AppearanceSettings.importImage(new ByteArrayInputStream(new byte[]{1,2,3}));fail("Imagen no válida aceptada");}catch(IOException expected){}
        } finally {AppearanceSettings.save(context(),previous);}
    }
    @android.annotation.TargetApi(29)
    @Test public void applicationAndEditorRemainUsableInBothOrientations()throws Exception {
        var instrumentation=InstrumentationRegistry.getInstrumentation();Context context=context();PDFBoxResourceLoader.init(context);
        SignatureStyles styleStore=new SignatureStyles(context);String originalStyle=styleStore.selected().id;SignatureAppearance previous=styleStore.appearance(styleStore.selected());MainActivity activity=null;
        var preferences=context.getSharedPreferences("appearance",Context.MODE_PRIVATE);boolean previousTimestamp=preferences.getBoolean("timestamp",true),previousConfirmation=preferences.getBoolean("confirmBeforeSigning",false);preferences.edit().putBoolean("timestamp",false).putBoolean("confirmBeforeSigning",false).commit();
        File folder=new File(context.getCacheDir(),"signed");assertTrue(folder.isDirectory()||folder.mkdirs());File source=new File(folder,"diseno-demostracion.pdf");
        try {
            styleStore.update(originalStyle,new SignatureAppearance("Firmado por {nombre}\n{fecha}",exampleImage(),true));
            try(PDDocument doc=new PDDocument()){PDPage page=new PDPage(PDRectangle.A4);doc.addPage(page);try(PDPageContentStream stream=new PDPageContentStream(doc,page)){stream.beginText();stream.setFont(com.tom_roush.pdfbox.pdmodel.font.PDType1Font.HELVETICA_BOLD,22);stream.newLineAtOffset(55,760);stream.showText("Documento de demostracion");stream.endText();stream.setNonStrokingColor(.85f,.88f,.94f);for(int i=0;i<11;i++){stream.addRect(55,680-i*28,480-(i%3)*60,5);stream.fill();}}doc.save(source);}
            var uri=FileProvider.getUriForFile(context,context.getPackageName()+".files",source);
            Intent intent=new Intent(context,MainActivity.class).setAction(Intent.ACTION_VIEW).setDataAndType(uri,"application/pdf").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_GRANT_READ_URI_PERMISSION);
            activity=(MainActivity)instrumentation.startActivitySync(intent);final MainActivity current=activity;
            for(int i=0;i<80;i++){instrumentation.waitForIdleSync();final boolean[] ready={false};instrumentation.runOnMainSync(()->{View view=current.getWindow().getDecorView().findViewWithTag("pdf-preview");ready[0]=view!=null&&view.isEnabled();});if(ready[0])break;SystemClock.sleep(100);}
            instrumentation.runOnMainSync(()->{PdfSelectionView view=current.getWindow().getDecorView().findViewWithTag("pdf-preview");assertTrue(view.getHeight()>100);view.resetBox();assertNotNull(view.selection());});instrumentation.waitForIdleSync();SystemClock.sleep(350);capture("android-design-portrait.png");
            instrumentation.runOnMainSync(()->current.getWindow().getDecorView().findViewWithTag("focus-view").performClick());instrumentation.waitForIdleSync();SystemClock.sleep(350);capture("android-document-focus.png");
            instrumentation.runOnMainSync(()->current.getWindow().getDecorView().findViewWithTag("focus-view").performClick());instrumentation.waitForIdleSync();
            instrumentation.runOnMainSync(()->current.setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE));SystemClock.sleep(700);instrumentation.waitForIdleSync();
            capture("android-design-landscape.png");instrumentation.runOnMainSync(()->{View view=current.getWindow().getDecorView().findViewWithTag("pdf-preview");assertTrue("Ancho útil horizontal: "+view.getWidth(),view.getWidth()>100);assertTrue("Alto útil horizontal: "+view.getHeight(),view.getHeight()>100);});
            instrumentation.runOnMainSync(()->current.setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_PORTRAIT));SystemClock.sleep(700);instrumentation.waitForIdleSync();
            instrumentation.runOnMainSync(()->{assertEquals("Firmar",((android.widget.Button)current.getWindow().getDecorView().findViewWithTag("sign")).getText().toString());assertNotNull(current.getWindow().getDecorView().findViewWithTag("style-selector"));assertNull(current.getWindow().getDecorView().findViewWithTag("timestamp"));current.getWindow().getDecorView().findViewWithTag("settings").performClick();});instrumentation.waitForIdleSync();SystemClock.sleep(250);capture("android-settings.png");
            instrumentation.runOnMainSync(()->{boolean found=false;for(View root:android.view.inspector.WindowInspector.getGlobalWindowViews()){View edit=root.findViewWithTag("customize");if(edit!=null){edit.performClick();found=true;break;}}assertTrue(found);});instrumentation.waitForIdleSync();SystemClock.sleep(350);capture("android-appearance-editor.png");
            instrumentation.runOnMainSync(()->{boolean found=false;for(View root:android.view.inspector.WindowInspector.getGlobalWindowViews()){android.widget.EditText field=root.findViewWithTag("appearance-text");if(field!=null){field.setText("Revisado y aprobado\n{nombre}\n{fecha}");((android.widget.SeekBar)root.findViewWithTag("appearance-text-size")).setProgress(4);root.findViewWithTag("save-design").performClick();found=true;break;}}assertTrue("Editor accesible",found);});
            for(int i=0;i<50;i++){SignatureStyles saved=new SignatureStyles(context);if(saved.appearance(saved.selected()).template.startsWith("Revisado y aprobado"))break;SystemClock.sleep(100);}
            SignatureStyles saved=new SignatureStyles(context);assertTrue("El diseño editado se conserva",saved.appearance(saved.selected()).template.startsWith("Revisado y aprobado"));assertEquals(6f,saved.appearance(saved.selected()).textSize,0);
            instrumentation.runOnMainSync(()->{for(View root:android.view.inspector.WindowInspector.getGlobalWindowViews()){View close=root.findViewWithTag("close-settings");if(close!=null){close.performClick();break;}}});instrumentation.waitForIdleSync();
            Identity testIdentity=identity();java.lang.reflect.Method setIdentity=MainActivity.class.getDeclaredMethod("setIdentity",Identity.class);setIdentity.setAccessible(true);java.lang.reflect.Field signedField=MainActivity.class.getDeclaredField("signed");signedField.setAccessible(true);
            instrumentation.runOnMainSync(()->{try{setIdentity.invoke(current,testIdentity);}catch(Exception e){throw new AssertionError(e);}PdfSelectionView preview=current.getWindow().getDecorView().findViewWithTag("pdf-preview");preview.resetBox();current.getWindow().getDecorView().findViewWithTag("sign").performClick();});
            File[] signed={null};for(int i=0;i<150;i++){instrumentation.waitForIdleSync();instrumentation.runOnMainSync(()->{try{signed[0]=(File)signedField.get(current);}catch(Exception e){throw new AssertionError(e);}});if(signed[0]!=null)break;SystemClock.sleep(100);}
            assertNotNull("Firmar produce el PDF sin confirmación previa",signed[0]);assertEquals(1,PdfEngine.verify(signed[0]).signatures);
            instrumentation.runOnMainSync(()->{boolean save=false,share=false,authorization=false;for(View root:android.view.inspector.WindowInspector.getGlobalWindowViews()){save|=containsText(root,"Guardar como…");share|=containsText(root,"Compartir");authorization|=containsText(root,"Autorizar firma");}assertTrue("Resultado ofrece guardar como",save);assertTrue("Resultado ofrece compartir",share);assertFalse("Sin diálogo previo",authorization);});capture("android-signed-actions.png");
        } finally {if(activity!=null){final MainActivity close=activity;instrumentation.runOnMainSync(close::finish);}SignatureStyles restore=new SignatureStyles(context);restore.update(originalStyle,previous);restore.select(originalStyle);preferences.edit().putBoolean("timestamp",previousTimestamp).putBoolean("confirmBeforeSigning",previousConfirmation).commit();source.delete();}
    }
    private boolean containsText(View view,String text){if(view instanceof android.widget.TextView&&text.contentEquals(((android.widget.TextView)view).getText()))return true;if(view instanceof ViewGroup){ViewGroup group=(ViewGroup)view;for(int i=0;i<group.getChildCount();i++)if(containsText(group.getChildAt(i),text))return true;}return false;}
    private void capture(String name)throws IOException {Bitmap screenshot=InstrumentationRegistry.getInstrumentation().getUiAutomation().takeScreenshot();assertNotNull(screenshot);ByteArrayOutputStream out=new ByteArrayOutputStream();screenshot.compress(Bitmap.CompressFormat.PNG,100,out);diagnostic(name,"image/png",out.toByteArray(),true);screenshot.recycle();}
}
