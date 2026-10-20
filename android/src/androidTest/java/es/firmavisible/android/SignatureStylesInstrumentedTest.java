package es.firmavisible.android;

import android.content.*;
import android.graphics.*;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import es.firmavisible.core.SignatureAppearance;
import java.io.*;
import java.util.*;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class SignatureStylesInstrumentedTest {
    @Test public void migratesSingleDesignAndKeepsIndependentNamedStylesAfterRestart()throws Exception {
        Context base=InstrumentationRegistry.getInstrumentation().getTargetContext();String scope="styles-test-"+UUID.randomUUID();File directory=new File(base.getCacheDir(),scope);assertTrue(directory.mkdirs());
        Context isolated=new ContextWrapper(base){@Override public File getFilesDir(){return directory;}@Override public SharedPreferences getSharedPreferences(String name,int mode){return base.getSharedPreferences(scope+name,mode);}};
        try{
            Bitmap bitmap=Bitmap.createBitmap(20,10,Bitmap.Config.ARGB_8888);bitmap.eraseColor(Color.RED);ByteArrayOutputStream out=new ByteArrayOutputStream();bitmap.compress(Bitmap.CompressFormat.PNG,100,out);bitmap.recycle();byte[] png=out.toByteArray();
            SignatureAppearance original=new SignatureAppearance("Original {nombre}",png,false,5.5f);AppearanceSettings.save(isolated,original);
            SignatureStyles first=new SignatureStyles(isolated);String originalId=first.selected().id;assertEquals("Mi firma",first.selected().name);assertEquals(original.template,first.appearance(first.selected()).template);assertArrayEquals(png,first.appearance(first.selected()).image());
            SignatureStyles.Style second=first.add("Trabajo",new SignatureAppearance("Revisado {fecha}",null,true,7));first.rename(second.id,"Empresa");
            SignatureStyles loaded=new SignatureStyles(isolated);assertEquals(2,loaded.list().size());assertEquals("Empresa",loaded.selected().name);assertEquals(7f,loaded.appearance(loaded.selected()).textSize,0);assertNull(loaded.appearance(loaded.selected()).image());
            loaded.select(originalId);loaded.update(originalId,new SignatureAppearance("Solo imagen",png,false,4));
            SignatureStyles restarted=new SignatureStyles(isolated);assertEquals(originalId,restarted.selected().id);assertArrayEquals(png,restarted.appearance(restarted.selected()).image());assertEquals("Revisado {fecha}",restarted.appearance(restarted.find(second.id)).template);
            try{restarted.add("empresa",SignatureAppearance.standard());fail("Nombre duplicado aceptado");}catch(IOException expected){}
            try{restarted.rename(originalId," ");fail("Nombre vacío aceptado");}catch(IOException expected){}
            restarted.remove(originalId);SignatureStyles remaining=new SignatureStyles(isolated);assertEquals(second.id,remaining.selected().id);assertEquals(1,remaining.list().size());
            try{remaining.remove(second.id);fail("Último estilo eliminado");}catch(IOException expected){}
            for(int i=1;i<SignatureStyles.LIMIT;i++)remaining.add("Estilo "+i,SignatureAppearance.standard());
            try{remaining.add("Exceso",SignatureAppearance.standard());fail("Límite superado");}catch(IOException expected){}
            // The previous single appearance remains available for recovery; no certificate data is migrated.
            assertEquals(original.template,AppearanceSettings.load(isolated).template);
            String json=new String(java.nio.file.Files.readAllBytes(new File(directory,"signature-styles/styles.json").toPath()),java.nio.charset.StandardCharsets.UTF_8);assertFalse(json.contains("password"));assertFalse(json.contains("privateKey"));
        }finally{erase(directory);isolated.getSharedPreferences("appearance",Context.MODE_PRIVATE).edit().clear().commit();}
    }
    private static void erase(File directory){File[] entries=directory.listFiles();if(entries!=null)for(File entry:entries){if(entry.isDirectory())erase(entry);else entry.delete();}directory.delete();}
}
