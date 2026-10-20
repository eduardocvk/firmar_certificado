package es.firmavisible.android;

import android.content.Context;
import android.util.AtomicFile;
import es.firmavisible.core.SignatureAppearance;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.*;

/** Named appearances only. Certificates, aliases and passwords never enter this store. */
final class SignatureStyles {
    static final int LIMIT=20;
    static final class Style {
        final String id,name,text,image;final boolean decorated;final float size;
        Style(String id,String name,String text,String image,boolean decorated,float size){this.id=id;this.name=name;this.text=text;this.image=image;this.decorated=decorated;this.size=size;}
        @Override public String toString(){return name;}
    }
    private final File directory;
    private final AtomicFile index;
    private List<Style> styles=new ArrayList<>();
    private String selected;
    SignatureStyles(Context context)throws IOException {
        directory=new File(context.getFilesDir(),"signature-styles");
        if(!directory.isDirectory()&&!directory.mkdirs())throw new IOException("No se pudieron abrir los estilos guardados.");
        index=new AtomicFile(new File(directory,"styles.json"));
        if(index.getBaseFile().exists()||new File(directory,"styles.json.bak").exists()){
            try(InputStream in=index.openRead()){
                ByteArrayOutputStream bytes=new ByteArrayOutputStream();byte[] buffer=new byte[8192];int n;
                while((n=in.read(buffer))!=-1){if(bytes.size()+n>256*1024)throw new IOException("Índice de estilos demasiado grande.");bytes.write(buffer,0,n);}
                JSONObject root=new JSONObject(bytes.toString("UTF-8"));JSONArray entries=root.getJSONArray("styles");
                if(entries.length()<1||entries.length()>LIMIT)throw new IOException("Lista de estilos no válida.");
                Set<String> ids=new HashSet<>();
                for(int i=0;i<entries.length();i++){
                    JSONObject item=entries.getJSONObject(i);String id=item.getString("id"),image=item.optString("image","");
                    if(!id.matches("[a-f0-9-]{36}")||!ids.add(id)||(!image.isEmpty()&&!image.matches("[a-f0-9-]{36}\\.png")))throw new IOException("Identificador de estilo no válido.");
                    String name=validName(item.getString("name"));String text=item.getString("text");float size=(float)item.getDouble("size");
                    if(text.length()>600||!Float.isFinite(size)||size<4||size>18)throw new IOException("Apariencia guardada no válida.");
                    styles.add(new Style(id,name,text,image,item.getBoolean("decorated"),size));
                }
                selected=root.optString("selected",styles.get(0).id);if(find(selected)==null)selected=styles.get(0).id;
            }catch(JSONException e){throw new IOException("No se pudieron leer los estilos guardados.",e);}
        }else{
            // Migrate the previous single appearance without deleting it.
            Style migrated=make("Mi firma",AppearanceSettings.load(context),UUID.randomUUID().toString());
            styles.add(migrated);selected=migrated.id;persist(styles,selected);
        }
    }
    List<Style> list(){return Collections.unmodifiableList(styles);}
    Style selected(){return find(selected);}
    Style find(String id){for(Style value:styles)if(value.id.equals(id))return value;return null;}
    SignatureAppearance appearance(Style style)throws IOException {
        byte[] image=null;if(!style.image.isEmpty()){
            File file=new File(directory,style.image);if(!file.isFile()||file.length()>4*1024*1024)throw new IOException("No se puede leer la imagen de «"+style.name+"».");image=Files.readAllBytes(file.toPath());
        }
        try{return new SignatureAppearance(style.text,image,style.decorated,style.size);}catch(IllegalArgumentException e){throw new IOException("El estilo no contiene una apariencia válida.",e);}
    }
    void select(String id)throws IOException {if(find(id)==null)throw new IOException("El estilo ya no existe.");persist(styles,id);selected=id;}
    Style add(String name,SignatureAppearance appearance)throws IOException {
        if(styles.size()>=LIMIT)throw new IOException("Puedes guardar hasta "+LIMIT+" estilos.");
        uniqueName(name,null);Style value=make(name,appearance,UUID.randomUUID().toString());List<Style> next=new ArrayList<>(styles);next.add(value);
        try{persist(next,value.id);}catch(IOException e){deleteImage(value.image);throw e;}styles=next;selected=value.id;return value;
    }
    void update(String id,SignatureAppearance appearance)throws IOException {
        Style old=find(id);if(old==null)throw new IOException("El estilo ya no existe.");Style value=make(old.name,appearance,id);List<Style> next=new ArrayList<>(styles);next.set(next.indexOf(old),value);
        try{persist(next,selected);}catch(IOException e){deleteImage(value.image);throw e;}styles=next;deleteImage(old.image);
    }
    void rename(String id,String name)throws IOException {
        Style old=find(id);if(old==null)throw new IOException("El estilo ya no existe.");uniqueName(name,id);
        List<Style> next=new ArrayList<>(styles);next.set(next.indexOf(old),new Style(id,validName(name),old.text,old.image,old.decorated,old.size));persist(next,selected);styles=next;
    }
    void remove(String id)throws IOException {
        if(styles.size()==1)throw new IOException("Conserva al menos un estilo de firma.");Style old=find(id);if(old==null)throw new IOException("El estilo ya no existe.");
        List<Style> next=new ArrayList<>(styles);next.remove(old);String nextSelected=id.equals(selected)?next.get(0).id:selected;persist(next,nextSelected);styles=next;selected=nextSelected;deleteImage(old.image);
    }
    private static String validName(String name)throws IOException {if(name==null||name.trim().isEmpty()||name.trim().length()>40||name.matches("(?s).*[\\p{Cntrl}].*"))throw new IOException("Escribe un nombre de entre 1 y 40 caracteres.");return name.trim();}
    private void uniqueName(String name,String id)throws IOException {String clean=validName(name);for(Style style:styles)if(!style.id.equals(id)&&style.name.equalsIgnoreCase(clean))throw new IOException("Ya existe un estilo con ese nombre.");}
    private Style make(String name,SignatureAppearance appearance,String id)throws IOException {
        String clean=validName(name),image="";byte[] bytes=appearance.image();
        if(bytes!=null){image=UUID.randomUUID()+".png";Files.write(new File(directory,image).toPath(),bytes);}
        return new Style(id,clean,appearance.template,image,appearance.decorated,appearance.textSize);
    }
    private void deleteImage(String image){if(!image.isEmpty())new File(directory,image).delete();}
    private void persist(List<Style> entries,String selection)throws IOException {
        FileOutputStream output=null;
        try{
            JSONArray array=new JSONArray();for(Style style:entries)array.put(new JSONObject().put("id",style.id).put("name",style.name).put("text",style.text).put("image",style.image).put("decorated",style.decorated).put("size",style.size));
            byte[] bytes=new JSONObject().put("version",1).put("selected",selection).put("styles",array).toString().getBytes(StandardCharsets.UTF_8);
            output=index.startWrite();output.write(bytes);index.finishWrite(output);
        }catch(JSONException e){if(output!=null)index.failWrite(output);throw new IOException("No se pudieron guardar los estilos.",e);}
        catch(IOException e){if(output!=null)index.failWrite(output);throw e;}
    }
}
