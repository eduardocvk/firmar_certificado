package es.firmavisible.android;

import android.content.Context;
import android.graphics.*;
import androidx.exifinterface.media.ExifInterface;
import java.io.*;
import java.nio.file.*;
import es.firmavisible.core.SignatureAppearance;

final class AppearanceSettings {
    private AppearanceSettings(){}
    static SignatureAppearance load(Context context) {
        var prefs=context.getSharedPreferences("appearance",Context.MODE_PRIVATE);
        byte[] image=null;
        try{File file=new File(context.getFilesDir(),"signature-image.png");if(file.exists()&&file.length()<=4*1024*1024)image=Files.readAllBytes(file.toPath());}catch(IOException ignored){}
        try{return new SignatureAppearance(prefs.getString("text",SignatureAppearance.DEFAULT_TEXT),image,prefs.getInt("designVersion",1)>=2&&prefs.getBoolean("decorated",false),prefs.getFloat("textSize",8));}
        catch(IllegalArgumentException e){return SignatureAppearance.standard();}
    }
    static void save(Context context,SignatureAppearance style)throws IOException {
        File image=new File(context.getFilesDir(),"signature-image.png");byte[] bytes=style.image();
        if(bytes==null){if(image.exists()&&!image.delete())throw new IOException("No se pudo quitar la imagen guardada.");}
        else {
            File temp=File.createTempFile("appearance-",".png",context.getFilesDir());
            try{Files.write(temp.toPath(),bytes);Files.move(temp.toPath(),image.toPath(),StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE);}finally{temp.delete();}
        }
        if(!context.getSharedPreferences("appearance",Context.MODE_PRIVATE).edit().putString("text",style.template).putBoolean("decorated",style.decorated).putFloat("textSize",style.textSize).putInt("designVersion",2).commit())
            throw new IOException("No se pudo guardar la apariencia.");
    }
    static byte[] importImage(InputStream input)throws IOException {
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();byte[] buffer=new byte[16384];int n;
        while((n=input.read(buffer))!=-1){if(bytes.size()+n>16*1024*1024)throw new IOException("Elige una imagen de menos de 16 MB.");bytes.write(buffer,0,n);}
        byte[] original=bytes.toByteArray();BitmapFactory.Options bounds=new BitmapFactory.Options();bounds.inJustDecodeBounds=true;BitmapFactory.decodeByteArray(original,0,original.length,bounds);
        if(bounds.outWidth<=0||bounds.outHeight<=0||(long)bounds.outWidth*bounds.outHeight>100000000L)throw new IOException("Imagen no compatible o demasiado grande. Utiliza PNG o JPEG.");
        BitmapFactory.Options options=new BitmapFactory.Options();
        while(Math.max(bounds.outWidth,bounds.outHeight)/Math.max(1,options.inSampleSize)>1600)options.inSampleSize=Math.max(1,options.inSampleSize)*2;
        Bitmap bitmap=BitmapFactory.decodeByteArray(original,0,original.length,options);
        if(bitmap==null)throw new IOException("No se pudo leer la imagen.");
        try {
            int orientation=1;
            try{orientation=new ExifInterface(new ByteArrayInputStream(original)).getAttributeInt(ExifInterface.TAG_ORIENTATION,1);}catch(IOException ignored){}
            Matrix matrix=new Matrix();
            switch(orientation){case 2:matrix.setScale(-1,1);break;case 3:matrix.setRotate(180);break;case 4:matrix.setScale(1,-1);break;case 5:matrix.setRotate(90);matrix.postScale(-1,1);break;case 6:matrix.setRotate(90);break;case 7:matrix.setRotate(270);matrix.postScale(-1,1);break;case 8:matrix.setRotate(270);break;default:break;}
            float scale=Math.min(1,1200f/Math.max(bitmap.getWidth(),bitmap.getHeight()));matrix.postScale(scale,scale);
            Bitmap clean=Bitmap.createBitmap(bitmap,0,0,bitmap.getWidth(),bitmap.getHeight(),matrix,true);
            if(clean!=bitmap){bitmap.recycle();bitmap=clean;}
            ByteArrayOutputStream png=new ByteArrayOutputStream();
            if(!bitmap.compress(Bitmap.CompressFormat.PNG,100,png)||png.size()>4*1024*1024)throw new IOException("La imagen ocupa demasiado. Utiliza una firma o logotipo más sencillo.");
            return png.toByteArray();
        } finally {bitmap.recycle();}
    }
}
