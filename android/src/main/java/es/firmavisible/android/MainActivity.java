package es.firmavisible.android;

import android.app.*;
import android.content.*;
import android.graphics.*;
import android.graphics.pdf.PdfRenderer;
import android.net.Uri;
import android.os.*;
import android.provider.OpenableColumns;
import android.security.KeyChain;
import android.text.InputType;
import android.view.*;
import android.widget.*;
import androidx.core.content.FileProvider;
import com.tom_roush.pdfbox.android.PDFBoxResourceLoader;
import es.firmavisible.core.*;
import es.firmavisible.pdf.PdfEngine;
import java.io.*;
import java.nio.file.Files;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.Consumer;

public final class MainActivity extends Activity {
    private static final int OPEN_PDF=10,OPEN_CERT=11,SAVE_PDF=12;
    private final ExecutorService executor=Executors.newSingleThreadExecutor();
    private final List<Button> buttons=new ArrayList<>();
    private PdfSelectionView preview;
    private TextView status,identityLabel,pageLabel;
    private Identity identity;
    private File session,input,displayInput,signed;
    private String originalName="documento.pdf";
    private String signedName="documento_firmado.pdf";
    private int page,pages;
    private boolean busy;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);PDFBoxResourceLoader.init(getApplicationContext());
        session=new File(getCacheDir(),"session-"+UUID.randomUUID());
        if(!session.mkdirs())throw new IllegalStateException("No se pudo crear el espacio temporal.");
        LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setPadding(dp(12),dp(8),dp(12),dp(8));
        root.setBackgroundColor(Color.WHITE);
        // Respect Android 15 edge-to-edge insets, including the software keyboard.
        root.setOnApplyWindowInsetsListener((v,insets)->{
            v.setPadding(dp(12)+insets.getSystemWindowInsetLeft(),dp(8)+insets.getSystemWindowInsetTop(),dp(12)+insets.getSystemWindowInsetRight(),dp(8)+insets.getSystemWindowInsetBottom());return insets;
        });
        TextView title=text("FirmaVisible",24);title.setTypeface(null,Typeface.BOLD);root.addView(title);
        LinearLayout open=row();addButton(open,"Abrir PDF",()->pick(OPEN_PDF,"application/pdf"));
        addButton(open,"Certificado",this::certificateMenu);root.addView(open);
        identityLabel=text("Sin certificado · pulsa Certificado",12);root.addView(identityLabel);
        LinearLayout nav=row();addButton(nav,"←",()->{if(input!=null&&page>0){page--;render();}});
        pageLabel=text("Sin documento",13);nav.addView(pageLabel,new LinearLayout.LayoutParams(0,-2,2));
        addButton(nav,"→",()->{if(input!=null&&page+1<pages){page++;render();}});
        addButton(nav,"Ajustar",()->preview.resetZoom());root.addView(nav);
        TextView help=text("Arrastra para dibujar o mover. Esquina para ajustar tamaño. Pellizca para ampliar; dos dedos para desplazar.",11);root.addView(help);
        preview=new PdfSelectionView(this);root.addView(preview,new LinearLayout.LayoutParams(-1,0,1));
        LinearLayout actions=row();addButton(actions,"Recuadro",()->preview.resetBox());addButton(actions,"Comprobar",this::verify);root.addView(actions);
        LinearLayout output=row();addButton(output,"Guardar última copia",()->{if(signed!=null)savePicker();else error("Primero firma un PDF.");});addButton(output,"Compartir última copia",this::share);root.addView(output);
        LinearLayout signing=row();addButton(signing,"Firmar y guardar copia",this::authorize);root.addView(signing);
        status=text("Firma local. Sin sello de tiempo ni comprobación de revocación.",11);root.addView(status);
        setContentView(root);
        Uri uri=incoming(getIntent());if(uri!=null)openPdf(uri);
    }
    private int dp(int value){return Math.round(value*getResources().getDisplayMetrics().density);}
    private TextView text(String value,int size){TextView t=new TextView(this);t.setText(value);t.setTextSize(size);t.setPadding(0,dp(3),0,dp(3));t.setTextColor(Color.rgb(25,47,74));return t;}
    private LinearLayout row(){LinearLayout row=new LinearLayout(this);row.setGravity(Gravity.CENTER_VERTICAL);return row;}
    private void addButton(LinearLayout parent,String title,Runnable action){Button b=new Button(this);b.setText(title);b.setTextSize(12);b.setAllCaps(false);b.setOnClickListener(v->{if(!busy)action.run();});buttons.add(b);parent.addView(b,new LinearLayout.LayoutParams(0,dp(46),1));}
    private void setBusy(boolean value,String message){busy=value;for(Button b:buttons)b.setEnabled(!value);preview.setEnabled(!value);status.setText(message);}
    private <T> void work(String message,Callable<T> action,Consumer<T> success){
        setBusy(true,message);
        executor.execute(()->{
            try{T value=action.call();runOnUiThread(()->{if(isDestroyed())return;setBusy(false,"Listo.");success.accept(value);});}
            catch(Exception e){runOnUiThread(()->{if(isDestroyed())return;setBusy(false,"Operación no completada.");error(e.getMessage()==null?"No se pudo completar la operación.":e.getMessage());});}
        });
    }
    private void error(String message){new AlertDialog.Builder(this).setTitle("FirmaVisible").setMessage(message).setPositiveButton("Aceptar",null).show();}
    private void pick(int request,String mime){Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT);intent.addCategory(Intent.CATEGORY_OPENABLE);intent.setType(mime);startActivityForResult(intent,request);}
    private Uri incoming(Intent intent){
        if(Intent.ACTION_SEND.equals(intent.getAction()))return intent.getParcelableExtra(Intent.EXTRA_STREAM);
        if(Intent.ACTION_VIEW.equals(intent.getAction()))return intent.getData();return null;
    }
    @Override protected void onNewIntent(Intent intent){super.onNewIntent(intent);if(busy){error("Espera a que termine la operación antes de abrir otro PDF.");return;}Uri uri=incoming(intent);if(uri!=null)openPdf(uri);}
    @Override protected void onActivityResult(int request,int result,Intent data){
        super.onActivityResult(request,result,data);if(result!=RESULT_OK||data==null||data.getData()==null)return;
        Uri uri=data.getData();
        if(request==OPEN_PDF)openPdf(uri);else if(request==OPEN_CERT)password(uri);else if(request==SAVE_PDF)export(uri);
    }
    private String displayName(Uri uri){
        try(android.database.Cursor c=getContentResolver().query(uri,new String[]{OpenableColumns.DISPLAY_NAME},null,null,null)){
            if(c!=null&&c.moveToFirst()){String name=c.getString(0);if(name!=null)return name.replaceAll("[\\\\/:*?\"<>|\\p{Cntrl}]","_");}
        }catch(Exception ignored){}return "documento.pdf";
    }
    private void openPdf(Uri uri){
        if(!"content".equals(uri.getScheme())){error("Abre el PDF desde el selector de archivos o comparte un archivo con permiso de lectura.");return;}
        String name=displayName(uri);
        work("Abriendo PDF…",()->{
            File file=File.createTempFile("original-",".pdf",session);
            try(InputStream in=getContentResolver().openInputStream(uri);OutputStream out=new FileOutputStream(file)){
                if(in==null)throw new IOException("No se pudo abrir el documento.");copy(in,out,100L*1024*1024);
            }catch(Exception e){file.delete();throw e;}
            File display;
            try { display=PreviewDocument.prepare(file,session); }
            catch(Exception e) { file.delete();throw e; }
            try(ParcelFileDescriptor fd=ParcelFileDescriptor.open(display,ParcelFileDescriptor.MODE_READ_ONLY);PdfRenderer renderer=new PdfRenderer(fd)){
                if(renderer.getPageCount()==0)throw new IOException("PDF sin páginas.");
            }catch(Exception e){file.delete();throw e;}
            return new File[]{file,display};
        },files->{if(displayInput!=null&&displayInput!=input)displayInput.delete();if(input!=null)input.delete();input=files[0];displayInput=files[1];originalName=name;page=0;render();});
    }
    private void render(){
        File current=displayInput;int index=page;
        work("Preparando página…",()->{
            try(ParcelFileDescriptor fd=ParcelFileDescriptor.open(current,ParcelFileDescriptor.MODE_READ_ONLY);PdfRenderer renderer=new PdfRenderer(fd);PdfRenderer.Page pdf=renderer.openPage(index)){
                pages=renderer.getPageCount();float scale=Math.min(2f,1600f/Math.max(pdf.getWidth(),pdf.getHeight()));
                Bitmap bitmap=Bitmap.createBitmap(Math.max(1,Math.round(pdf.getWidth()*scale)),Math.max(1,Math.round(pdf.getHeight()*scale)),Bitmap.Config.ARGB_8888);
                bitmap.eraseColor(Color.WHITE);pdf.render(bitmap,null,null,PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);return bitmap;
            }
        },bitmap->{preview.setBitmap(bitmap);pageLabel.setText((page+1)+" / "+pages);status.setText("Coloca el recuadro y selecciona tu certificado.");});
    }
    private void certificateMenu(){new AlertDialog.Builder(this).setTitle("Certificado para firmar").setItems(new String[]{"Elegir certificado de Android","Abrir .p12 / .pfx (solo esta sesión)","Olvidar certificado"},(dialog,index)->{
        if(index==0)KeyChain.choosePrivateKeyAlias(this,alias->{if(alias!=null)runOnUiThread(()->work("Leyendo certificado…",()->new Identity(KeyChain.getPrivateKey(this,alias),KeyChain.getCertificateChain(this,alias)),this::setIdentity));},new String[]{"RSA","EC"},null,null,-1,null);
        else if(index==1)pick(OPEN_CERT,"*/*");else{identity=null;identityLabel.setText("Sin certificado");preview.setSigner("Selecciona un certificado");}
    }).show();}
    private void setIdentity(Identity value){identity=value;identityLabel.setText(value.description());preview.setSigner(value.name());}
    private void password(Uri uri){
        if(!"content".equals(uri.getScheme())){error("Selecciona el certificado desde el selector de archivos.");return;}
        EditText field=new EditText(this);field.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_PASSWORD);field.setSingleLine(true);field.setHint("Contraseña del .p12 / .pfx");
        new AlertDialog.Builder(this).setTitle("Abrir certificado · solo esta sesión").setView(field).setNegativeButton("Cancelar",(d,w)->field.setText(""))
            .setPositiveButton("Abrir",(d,w)->{
                char[] password=new char[field.length()];field.getText().getChars(0,field.length(),password,0);field.setText("");
                work("Abriendo certificado…",()->{try(InputStream in=getContentResolver().openInputStream(uri)){
                    if(in==null)throw new IOException("No se pudo abrir el certificado.");
                    ByteArrayOutputStream bytes=new ByteArrayOutputStream();copy(in,bytes,4*1024*1024);
                    byte[] data=bytes.toByteArray();try{return Identity.fromPkcs12(new ByteArrayInputStream(data),password);}finally{Arrays.fill(data,(byte)0);}
                }finally{Arrays.fill(password,'\0');}},values->{
                    if(values.size()==1)setIdentity(values.get(0));else{String[] names=new String[values.size()];for(int i=0;i<names.length;i++)names[i]=values.get(i).description();new AlertDialog.Builder(this).setTitle("Selecciona identidad").setItems(names,(dialog,index)->setIdentity(values.get(index))).show();}
                });
            }).show();
    }
    private void authorize(){
        float[] box=preview.selection();if(input==null||identity==null||box==null){error("Abre un PDF, elige un certificado y dibuja el recuadro de firma.");return;}
        Placement placement;
        try{placement=new Placement(page,box[0],box[1],box[2],box[3]);}catch(IllegalArgumentException e){error(e.getMessage());return;}
        Identity selected=identity;File source=input;String sourceName=originalName;
        new AlertDialog.Builder(this).setTitle("Autorizar firma").setMessage("Vas a firmar el PDF con:\n"+selected.description()+"\n\nSe creará una copia. La fecha procede del dispositivo; no es un sello de tiempo.")
            .setNegativeButton("Cancelar",null).setPositiveButton("Firmar",(d,w)->work("Firmando y comprobando integridad…",()->{
                File folder=new File(getCacheDir(),"signed");if(!folder.isDirectory()&&!folder.mkdirs())throw new IOException("No se pudo crear la carpeta de salida.");
                File result=new File(folder,"firmado-"+UUID.randomUUID()+".pdf");PdfEngine.sign(source,result,placement,selected);return result;
            },result->{if(signed!=null)signed.delete();signed=result;signedName=sourceName.replaceFirst("(?i)\\.pdf$","")+"_firmado.pdf";
                new AlertDialog.Builder(this).setTitle("PDF firmado").setMessage(PdfEngine.VERIFICATION_NOTICE)
                    .setPositiveButton("Guardar copia",(dialog,which)->savePicker()).setNeutralButton("Compartir",(dialog,which)->share()).setNegativeButton("Cerrar",null).show();
                status.setText("PDF firmado. Puedes guardar o compartir la última copia.");
            })).show();
    }
    private void savePicker(){Intent intent=new Intent(Intent.ACTION_CREATE_DOCUMENT);intent.addCategory(Intent.CATEGORY_OPENABLE);intent.setType("application/pdf");intent.putExtra(Intent.EXTRA_TITLE,signedName);startActivityForResult(intent,SAVE_PDF);}
    private void export(Uri uri){File result=signed;if(result==null){error("La copia temporal ya no está disponible. Vuelve a firmar el documento.");return;}
        work("Guardando copia…",()->{try(InputStream in=new FileInputStream(result);OutputStream out=getContentResolver().openOutputStream(uri,"wt")){if(out==null)throw new IOException("No se pudo abrir el destino.");copy(in,out,Long.MAX_VALUE);}return true;},done->new AlertDialog.Builder(this).setTitle("Copia guardada").setMessage("El PDF firmado se ha guardado en la ubicación elegida.").setPositiveButton("Aceptar",null).setNeutralButton("Compartir",(d,w)->share()).show());
    }
    private void share(){if(signed==null)return;Uri uri=FileProvider.getUriForFile(this,getPackageName()+".files",signed);Intent intent=new Intent(Intent.ACTION_SEND);intent.setType("application/pdf");intent.putExtra(Intent.EXTRA_STREAM,uri);intent.setClipData(ClipData.newRawUri("PDF firmado",uri));intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);startActivity(Intent.createChooser(intent,"Compartir PDF firmado"));}
    private void verify(){if(input==null)return;work("Comprobando integridad…",()->PdfEngine.verify(input),result->error(result.signatures==0?"El PDF no contiene firmas digitales.":result.toString()));}
    private static void copy(InputStream in,OutputStream out,long limit)throws IOException{byte[] buffer=new byte[32768];int n;long size=0;while((n=in.read(buffer))!=-1){size+=n;if(size>limit)throw new IOException("Archivo demasiado grande para esta versión.");out.write(buffer,0,n);}}
    @Override protected void onDestroy(){
        identity=null;preview.release();File folder=session;
        executor.execute(()->{File[] files=folder.listFiles();if(files!=null)for(File file:files)file.delete();folder.delete();});executor.shutdown();super.onDestroy();
    }
}
