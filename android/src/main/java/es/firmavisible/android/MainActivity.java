package es.firmavisible.android;

import android.app.*;
import android.content.*;
import android.graphics.*;
import android.graphics.drawable.GradientDrawable;
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
    private static final int OPEN_PDF=10,OPEN_CERT=11,SAVE_PDF=12,OPEN_IMAGE=13;
    private final ExecutorService executor=Executors.newSingleThreadExecutor();
    private final List<Button> buttons=new ArrayList<>();
    private PdfSelectionView preview;
    private TextView status,identityLabel,pageLabel;
    private Identity identity;
    private File session,input,displayInput,signed;
    private String originalName="documento.pdf";
    private String signedName="documento_firmado.pdf";
    private int page,pages,previewPageWidth,previewPageHeight;
    private boolean busy;
    private SignatureAppearance appearance;
    private AppearanceEditor appearanceEditor;
    private Switch timeSwitch;
    private TextView documentLabel;
    private LinearLayout rootLayout,workspacePanel;
    private ScrollView controlsPanel;
    private Button viewMode;
    private boolean focusMode;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);PDFBoxResourceLoader.init(getApplicationContext());
        session=new File(getCacheDir(),"session-"+UUID.randomUUID());
        if(!session.mkdirs())throw new IllegalStateException("No se pudo crear el espacio temporal.");
        appearance=AppearanceSettings.load(this);
        LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setBackgroundColor(0xFFF5F7FC);root.setPadding(dp(18),dp(8),dp(18),dp(8));
        root.setOnApplyWindowInsetsListener((v,insets)->{v.setPadding(dp(18)+insets.getSystemWindowInsetLeft(),dp(8)+insets.getSystemWindowInsetTop(),dp(18)+insets.getSystemWindowInsetRight(),dp(8)+insets.getSystemWindowInsetBottom());return insets;});
        rootLayout=root;LinearLayout controls=new LinearLayout(this);controls.setOrientation(LinearLayout.VERTICAL);
        controlsPanel=new ScrollView(this){@Override protected void onMeasure(int width,int height){if(getResources().getConfiguration().orientation!=android.content.res.Configuration.ORIENTATION_LANDSCAPE){int available=rootLayout.getHeight()>0?rootLayout.getHeight():getResources().getDisplayMetrics().heightPixels;height=View.MeasureSpec.makeMeasureSpec(Math.round(available*.49f),View.MeasureSpec.AT_MOST);}super.onMeasure(width,height);}};controlsPanel.setVerticalScrollBarEnabled(false);controlsPanel.addView(controls);root.addView(controlsPanel);workspacePanel=new LinearLayout(this);workspacePanel.setOrientation(LinearLayout.VERTICAL);root.addView(workspacePanel);
        LinearLayout header=row();LinearLayout heading=new LinearLayout(this);heading.setOrientation(LinearLayout.VERTICAL);
        TextView title=text("FirmaPDF",27);title.setTypeface(null,Typeface.BOLD);heading.addView(title);
        header.addView(heading,new LinearLayout.LayoutParams(0,-2,1));
        Button more=button("•••",this::moreMenu,false);more.setContentDescription("Guardar, compartir y comprobar firmas");header.addView(more,new LinearLayout.LayoutParams(dp(48),dp(48)));controls.addView(header);
        LinearLayout document=card();LinearLayout documentRow=row();
        LinearLayout fileInfo=new LinearLayout(this);fileInfo.setOrientation(LinearLayout.VERTICAL);
        documentLabel=text("Abre el PDF que quieres firmar",13);documentLabel.setSingleLine(true);documentLabel.setEllipsize(android.text.TextUtils.TruncateAt.MIDDLE);fileInfo.addView(documentLabel);
        documentRow.addView(fileInfo,new LinearLayout.LayoutParams(0,-2,1));Button open=button("Abrir",()->pick(OPEN_PDF,"application/pdf"),false);documentRow.addView(open,new LinearLayout.LayoutParams(dp(72),dp(46)));document.addView(documentRow);
        LinearLayout certificateRow=row();identityLabel=text("Sin certificado",12);identityLabel.setSingleLine(true);identityLabel.setEllipsize(android.text.TextUtils.TruncateAt.MIDDLE);identityLabel.setTextColor(0xFF66758E);certificateRow.addView(identityLabel,new LinearLayout.LayoutParams(0,-2,1));
        Button certificate=button("Certificado",this::certificateMenu,false);certificateRow.addView(certificate,new LinearLayout.LayoutParams(dp(110),dp(44)));document.addView(certificateRow);addCard(controls,document);
        LinearLayout design=row();Button customize=button("Personalizar firma",this::editAppearance,false);customize.setTag("customize");design.addView(customize,new LinearLayout.LayoutParams(0,dp(46),1));
        Button place=button("Colocar recuadro",()->preview.resetBox(),false);design.addView(place,new LinearLayout.LayoutParams(0,dp(46),1));controls.addView(design);
        LinearLayout time=card();timeSwitch=new Switch(this);timeSwitch.setText("Sello de tiempo");timeSwitch.setTextSize(14);timeSwitch.setTextColor(0xFF172D47);timeSwitch.setPadding(0,dp(2),0,dp(2));timeSwitch.setMinHeight(dp(44));
        timeSwitch.setText("Sello de tiempo · Internet");timeSwitch.setTextSize(12);timeSwitch.setChecked(getSharedPreferences("appearance",MODE_PRIVATE).getBoolean("timestamp",true));timeSwitch.setOnCheckedChangeListener((v,value)->getSharedPreferences("appearance",MODE_PRIVATE).edit().putBoolean("timestamp",value).apply());time.addView(timeSwitch);addCard(controls,time);
        LinearLayout nav=row();Button previous=button("‹",()->{if(input!=null&&page>0){page--;render();}},false);previous.setContentDescription("Página anterior");nav.addView(previous,new LinearLayout.LayoutParams(dp(44),dp(44)));
        pageLabel=text("VISTA DEL DOCUMENTO",11);pageLabel.setGravity(Gravity.CENTER);nav.addView(pageLabel,new LinearLayout.LayoutParams(0,-2,1));
        Button next=button("›",()->{if(input!=null&&page+1<pages){page++;render();}},false);next.setContentDescription("Página siguiente");nav.addView(next,new LinearLayout.LayoutParams(dp(44),dp(44)));
        Button fit=button("Ajustar",()->preview.resetZoom(),false);nav.addView(fit,new LinearLayout.LayoutParams(dp(66),dp(44)));viewMode=button("Ampliar",()->{focusMode=!focusMode;controlsPanel.setVisibility(focusMode?View.GONE:View.VISIBLE);viewMode.setText(focusMode?"Volver":"Ampliar");},false);viewMode.setTag("focus-view");nav.addView(viewMode,new LinearLayout.LayoutParams(dp(66),dp(44)));workspacePanel.addView(nav);
        preview=new PdfSelectionView(this);preview.setAppearance(appearance);preview.setTag("pdf-preview");workspacePanel.addView(preview,new LinearLayout.LayoutParams(-1,0,1));
        TextView help=text("Arrastra la firma · ajusta su esquina · pellizca para ampliar",10);help.setGravity(Gravity.CENTER);help.setTextColor(0xFF66758E);workspacePanel.addView(help);
        Button sign=button("Firmar y guardar copia  →",this::authorize,true);LinearLayout.LayoutParams primary=new LinearLayout.LayoutParams(-1,dp(54));primary.topMargin=dp(4);workspacePanel.addView(sign,primary);
        status=text("Los documentos y las claves permanecen en tu móvil.",10);status.setTextColor(0xFF66758E);status.setMaxLines(2);workspacePanel.addView(status);updateOrientation();setContentView(root);        Uri uri=incoming(getIntent());if(uri!=null)openPdf(uri);
    }
    private void updateOrientation(){boolean landscape=getResources().getConfiguration().orientation==android.content.res.Configuration.ORIENTATION_LANDSCAPE;rootLayout.setOrientation(landscape?LinearLayout.HORIZONTAL:LinearLayout.VERTICAL);controlsPanel.setLayoutParams(new LinearLayout.LayoutParams(landscape?dp(290):-1,landscape?-1:-2));LinearLayout.LayoutParams params=new LinearLayout.LayoutParams(landscape?0:-1,landscape?-1:0,1);params.leftMargin=landscape?dp(14):0;workspacePanel.setLayoutParams(params);}
    @Override public void onConfigurationChanged(android.content.res.Configuration configuration){super.onConfigurationChanged(configuration);updateOrientation();}    private int dp(int value){return Math.round(value*getResources().getDisplayMetrics().density);}
    private TextView text(String value,int size){TextView t=new TextView(this);t.setText(value);t.setTextSize(size);t.setPadding(0,dp(3),0,dp(3));t.setTextColor(Color.rgb(25,47,74));return t;}
    private LinearLayout row(){LinearLayout row=new LinearLayout(this);row.setGravity(Gravity.CENTER_VERTICAL);return row;}
    private GradientDrawable background(int color,int radius){GradientDrawable shape=new GradientDrawable();shape.setColor(color);shape.setCornerRadius(dp(radius));return shape;}
    private LinearLayout card(){LinearLayout card=new LinearLayout(this);card.setOrientation(LinearLayout.VERTICAL);card.setPadding(dp(14),dp(8),dp(12),dp(8));card.setBackground(background(Color.WHITE,16));return card;}
    private void addCard(LinearLayout root,View card){LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);p.topMargin=dp(10);root.addView(card,p);}
    private Button button(String title,Runnable action,boolean primary){Button button=new Button(this);button.setText(title);button.setTextSize(primary?15:12);button.setAllCaps(false);button.setTypeface(null,Typeface.BOLD);button.setTextColor(primary?Color.WHITE:0xFF3155D9);button.setBackground(background(primary?0xFF3155D9:0xFFF0F3FC,12));button.setPadding(dp(6),0,dp(6),0);button.setOnClickListener(v->{if(!busy)action.run();});buttons.add(button);return button;}
    private void setBusy(boolean value,String message){busy=value;if(appearanceEditor!=null&&appearanceEditor.isShowing())appearanceEditor.setBusy(value);for(Button b:buttons)b.setEnabled(!value);timeSwitch.setEnabled(!value);preview.setEnabled(!value);status.setText(message);}
    private void moreMenu(){new AlertDialog.Builder(this).setTitle("Tu documento").setItems(new String[]{"Guardar última copia firmada","Compartir última copia firmada","Comprobar firmas del PDF abierto"},(d,index)->{if(index==0){if(signed!=null)savePicker();else error("Primero firma un PDF.");}else if(index==1){if(signed!=null)share();else error("Primero firma un PDF.");}else verify();}).show();}
    private void editAppearance(){appearanceEditor=new AppearanceEditor(this,appearance,identity==null?"Tu nombre y apellidos":identity.name(),identity==null?"Emisor de tu certificado":identity.certificate().getIssuerX500Principal().getName(),()->pick(OPEN_IMAGE,"image/*"),value->work("Guardando tu diseño…",()->{AppearanceSettings.save(this,value);return value;},saved->{appearance=saved;preview.setAppearance(saved);appearanceEditor.dismiss();status.setText("Diseño guardado. Coloca la firma en tu PDF.");}));appearanceEditor.show();}
    private void importImage(Uri uri){if(!"content".equals(uri.getScheme())){error("Selecciona la imagen desde el selector de archivos.");return;}
        work("Preparando imagen…",()->{try(InputStream in=getContentResolver().openInputStream(uri)){if(in==null)throw new IOException("No se pudo abrir la imagen.");return AppearanceSettings.importImage(in);}},bytes->{if(appearanceEditor!=null&&appearanceEditor.isShowing())appearanceEditor.setImage(bytes);});}    private <T> void work(String message,Callable<T> action,Consumer<T> success){
        if(busy)return;
        setBusy(true,message);
        executor.execute(()->{
            try{T value=action.call();runOnUiThread(()->{if(isDestroyed())return;setBusy(false,"Listo.");success.accept(value);});}
            catch(Exception e){runOnUiThread(()->{if(isDestroyed())return;setBusy(false,"Operación no completada.");error(e.getMessage()==null?"No se pudo completar la operación.":e.getMessage());});}
        });
    }
    private void error(String message){new AlertDialog.Builder(this).setTitle("FirmaPDF").setMessage(message).setPositiveButton("Aceptar",null).show();}
    private void pick(int request,String mime){Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT);intent.addCategory(Intent.CATEGORY_OPENABLE);intent.setType(mime);startActivityForResult(intent,request);}
    private Uri incoming(Intent intent){
        if(Intent.ACTION_SEND.equals(intent.getAction()))return intent.getParcelableExtra(Intent.EXTRA_STREAM);
        if(Intent.ACTION_VIEW.equals(intent.getAction()))return intent.getData();return null;
    }
    @Override protected void onNewIntent(Intent intent){super.onNewIntent(intent);if(busy){error("Espera a que termine la operación antes de abrir otro PDF.");return;}Uri uri=incoming(intent);if(uri!=null)openPdf(uri);}
    @Override protected void onActivityResult(int request,int result,Intent data){
        super.onActivityResult(request,result,data);if(result!=RESULT_OK||data==null||data.getData()==null)return;
        Uri uri=data.getData();
        if(request==OPEN_PDF)openPdf(uri);else if(request==OPEN_CERT)password(uri);else if(request==SAVE_PDF)export(uri);else if(request==OPEN_IMAGE)importImage(uri);
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
        },files->{if(displayInput!=null&&displayInput!=input)displayInput.delete();if(input!=null)input.delete();input=files[0];displayInput=files[1];originalName=name;documentLabel.setText(name);page=0;render();});
    }
    private void render(){
        File current=displayInput;int index=page;
        work("Preparando página…",()->{
            try(ParcelFileDescriptor fd=ParcelFileDescriptor.open(current,ParcelFileDescriptor.MODE_READ_ONLY);PdfRenderer renderer=new PdfRenderer(fd);PdfRenderer.Page pdf=renderer.openPage(index)){
                pages=renderer.getPageCount();float scale=Math.min(2f,1600f/Math.max(pdf.getWidth(),pdf.getHeight()));
                previewPageWidth=pdf.getWidth();previewPageHeight=pdf.getHeight();
                Bitmap bitmap=Bitmap.createBitmap(Math.max(1,Math.round(pdf.getWidth()*scale)),Math.max(1,Math.round(pdf.getHeight()*scale)),Bitmap.Config.ARGB_8888);
                bitmap.eraseColor(Color.WHITE);pdf.render(bitmap,null,null,PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);return bitmap;
            }
        },bitmap->{preview.setBitmap(bitmap,previewPageWidth,previewPageHeight);pageLabel.setText((page+1)+" / "+pages);status.setText("Coloca el recuadro y selecciona tu certificado.");});
    }
    private void certificateMenu(){new AlertDialog.Builder(this).setTitle("Certificado para firmar").setItems(new String[]{"Elegir certificado de Android","Abrir .p12 / .pfx (solo esta sesión)","Olvidar certificado"},(dialog,index)->{
        if(index==0)KeyChain.choosePrivateKeyAlias(this,alias->{if(alias!=null)runOnUiThread(()->work("Leyendo certificado…",()->new Identity(KeyChain.getPrivateKey(this,alias),KeyChain.getCertificateChain(this,alias)),this::setIdentity));},new String[]{"RSA","EC"},null,null,-1,null);
        else if(index==1)pick(OPEN_CERT,"*/*");else{identity=null;identityLabel.setText("Sin certificado");preview.setSigner("Selecciona un certificado");}
    }).show();}
    private void setIdentity(Identity value){identity=value;identityLabel.setText(value.description());preview.setSigner(value.name(),value.certificate().getIssuerX500Principal().getName());}
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
        Identity selected=identity;File source=input;String sourceName=originalName;SignatureAppearance style=appearance;boolean timestamp=timeSwitch.isChecked();
        new AlertDialog.Builder(this).setTitle("Autorizar firma").setMessage("Vas a firmar el PDF con:\n"+selected.description()+"\n\nSe creará una copia con tu diseño. "+(timestamp?"Se solicitará un sello de tiempo a DigiCert enviando solo la huella de la firma, sin el PDF ni claves. Si falla, no se guardará una copia sin sello.":"Sin sello de tiempo: la fecha procede del móvil."))
            .setNegativeButton("Cancelar",null).setPositiveButton("Firmar",(d,w)->work("Firmando y comprobando integridad…",()->{
                File folder=new File(getCacheDir(),"signed");if(!folder.isDirectory()&&!folder.mkdirs())throw new IOException("No se pudo crear la carpeta de salida.");
                File result=new File(folder,"firmado-"+UUID.randomUUID()+".pdf");PdfEngine.sign(source,result,placement,selected,style,timestamp?Rfc3161Client.digicert():null);return new SignedCopy(result,PdfEngine.verify(result));
            },result->{if(signed!=null)signed.delete();signed=result.file;signedName=sourceName.replaceFirst("(?i)\\.pdf$","")+"_firmado.pdf";
                new AlertDialog.Builder(this).setTitle("PDF firmado").setMessage(PdfEngine.VERIFICATION_NOTICE+"\n\n"+(timestamp?"Sello de tiempo RFC3161 incorporado. Hora acreditada: "+java.time.format.DateTimeFormatter.ofPattern("dd/MM/yyyy HH:mm:ss XXX").withZone(java.time.ZoneId.systemDefault()).format(result.verification.latestTimestamp.toInstant())+". Consulta la confianza en el panel de firmas del PDF.":"Sin sello de tiempo. Fecha del móvil."))
                    .setPositiveButton("Guardar copia",(dialog,which)->savePicker()).setNeutralButton("Compartir",(dialog,which)->share()).setNegativeButton("Cerrar",null).show();
                status.setText("PDF firmado. Puedes guardar o compartir la última copia.");
            })).show();
    }
    private static final class SignedCopy{final File file;final PdfEngine.Verification verification;SignedCopy(File file,PdfEngine.Verification verification){this.file=file;this.verification=verification;}}
    private void savePicker(){Intent intent=new Intent(Intent.ACTION_CREATE_DOCUMENT);intent.addCategory(Intent.CATEGORY_OPENABLE);intent.setType("application/pdf");intent.putExtra(Intent.EXTRA_TITLE,signedName);startActivityForResult(intent,SAVE_PDF);}
    private void export(Uri uri){File result=signed;if(result==null){error("La copia temporal ya no está disponible. Vuelve a firmar el documento.");return;}
        work("Guardando copia…",()->{try(InputStream in=new FileInputStream(result);OutputStream out=getContentResolver().openOutputStream(uri,"wt")){if(out==null)throw new IOException("No se pudo abrir el destino.");copy(in,out,Long.MAX_VALUE);}return true;},done->new AlertDialog.Builder(this).setTitle("Copia guardada").setMessage("El PDF firmado se ha guardado en la ubicación elegida.").setPositiveButton("Aceptar",null).setNeutralButton("Compartir",(d,w)->share()).show());
    }
    private void share(){if(signed==null)return;Uri uri=FileProvider.getUriForFile(this,getPackageName()+".files",signed);Intent intent=new Intent(Intent.ACTION_SEND);intent.setType("application/pdf");intent.putExtra(Intent.EXTRA_STREAM,uri);intent.setClipData(ClipData.newRawUri("PDF firmado",uri));intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);startActivity(Intent.createChooser(intent,"Compartir PDF firmado"));}
    private void verify(){if(input==null)return;work("Comprobando integridad…",()->PdfEngine.verify(input),result->error(result.signatures==0?"El PDF no contiene firmas digitales.":result.toString()));}
    private static void copy(InputStream in,OutputStream out,long limit)throws IOException{byte[] buffer=new byte[32768];int n;long size=0;while((n=in.read(buffer))!=-1){size+=n;if(size>limit)throw new IOException("Archivo demasiado grande para esta versión.");out.write(buffer,0,n);}}
    @Override protected void onDestroy(){
        identity=null;if(appearanceEditor!=null)appearanceEditor.dismiss();preview.release();File folder=session;
        executor.execute(()->{File[] files=folder.listFiles();if(files!=null)for(File file:files)file.delete();folder.delete();});executor.shutdown();super.onDestroy();
    }
}
