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
    private File session,input,displayInput,signed,signedSource;
    private String originalName="documento.pdf";
    private String signedName="documento_firmado.pdf";
    private int page,pages,previewPageWidth,previewPageHeight;
    private boolean busy;
    private SignatureAppearance appearance;
    private AppearanceEditor appearanceEditor;
    private SignatureStyles styles;
    private Spinner styleSelector,settingsStyleSelector;
    private Dialog settingsDialog;
    private boolean refreshingStyles;
    private boolean timestampEnabled,confirmBeforeSigning,notifyAfterSaving;
    private TextView documentLabel;
    private LinearLayout rootLayout,workspacePanel,placementRow,signingRow;
    private Button placeButton;
    private ScrollView controlsPanel;
    private Button viewMode;
    private boolean focusMode;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);PDFBoxResourceLoader.init(getApplicationContext());
        session=new File(getCacheDir(),"session-"+UUID.randomUUID());
        if(!session.mkdirs())throw new IllegalStateException("No se pudo crear el espacio temporal.");
        timestampEnabled=getSharedPreferences("appearance",MODE_PRIVATE).getBoolean("timestamp",true);
        confirmBeforeSigning=getSharedPreferences("appearance",MODE_PRIVATE).getBoolean("confirmBeforeSigning",false);
        notifyAfterSaving=getSharedPreferences("appearance",MODE_PRIVATE).getBoolean("notifyAfterSaving",false);
        try{styles=new SignatureStyles(this);appearance=styles.appearance(styles.selected());}
        catch(IOException e){styles=null;appearance=AppearanceSettings.load(this);new AlertDialog.Builder(this).setTitle("Estilos guardados").setMessage(e.getMessage()+" No se han borrado tus estilos. Cierra y vuelve a abrir la aplicación para reintentarlo.").setPositiveButton("Aceptar",null).show();}
        LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setBackgroundColor(0xFFF5F7FC);root.setPadding(dp(18),dp(8),dp(18),dp(8));
        root.setOnApplyWindowInsetsListener((v,insets)->{v.setPadding(dp(18)+insets.getSystemWindowInsetLeft(),dp(8)+insets.getSystemWindowInsetTop(),dp(18)+insets.getSystemWindowInsetRight(),dp(8)+insets.getSystemWindowInsetBottom());return insets;});
        rootLayout=root;LinearLayout controls=new LinearLayout(this);controls.setOrientation(LinearLayout.VERTICAL);
        controlsPanel=new ScrollView(this){@Override protected void onMeasure(int width,int height){if(getResources().getConfiguration().orientation!=android.content.res.Configuration.ORIENTATION_LANDSCAPE){int available=rootLayout.getHeight()>0?rootLayout.getHeight():getResources().getDisplayMetrics().heightPixels;height=View.MeasureSpec.makeMeasureSpec(Math.round(available*.49f),View.MeasureSpec.AT_MOST);}super.onMeasure(width,height);}};controlsPanel.setVerticalScrollBarEnabled(false);controlsPanel.addView(controls);root.addView(controlsPanel);workspacePanel=new LinearLayout(this);workspacePanel.setOrientation(LinearLayout.VERTICAL);root.addView(workspacePanel);
        LinearLayout header=row();LinearLayout heading=new LinearLayout(this);heading.setOrientation(LinearLayout.VERTICAL);
        TextView title=text("FirmaPDF",27);title.setTypeface(null,Typeface.BOLD);heading.addView(title);
        header.addView(heading,new LinearLayout.LayoutParams(0,-2,1));
        Button more=button("⚙",this::settings,false);more.setTag("settings");more.setTextSize(24);more.setContentDescription("Configuración");header.addView(more,new LinearLayout.LayoutParams(dp(48),dp(48)));controls.addView(header);
        LinearLayout document=card();LinearLayout documentRow=row();
        LinearLayout fileInfo=new LinearLayout(this);fileInfo.setOrientation(LinearLayout.VERTICAL);
        documentLabel=text("Abre el PDF que quieres firmar",13);documentLabel.setSingleLine(true);documentLabel.setEllipsize(android.text.TextUtils.TruncateAt.MIDDLE);fileInfo.addView(documentLabel);
        documentRow.addView(fileInfo,new LinearLayout.LayoutParams(0,-2,1));Button open=button("Abrir",()->pick(OPEN_PDF,"application/pdf"),false);documentRow.addView(open,new LinearLayout.LayoutParams(dp(72),dp(46)));document.addView(documentRow);
        LinearLayout certificateRow=row();identityLabel=text("Sin certificado",12);identityLabel.setSingleLine(true);identityLabel.setEllipsize(android.text.TextUtils.TruncateAt.MIDDLE);identityLabel.setTextColor(0xFF66758E);certificateRow.addView(identityLabel,new LinearLayout.LayoutParams(0,-2,1));
        Button certificate=button("Certificado",this::certificateMenu,false);certificateRow.addView(certificate,new LinearLayout.LayoutParams(dp(110),dp(44)));document.addView(certificateRow);addCard(controls,document);
        LinearLayout design=row();design.addView(text("Estilo",12));styleSelector=new Spinner(this);styleSelector.setTag("style-selector");styleSelector.setContentDescription("Estilo de firma");design.addView(styleSelector,new LinearLayout.LayoutParams(0,dp(44),1));document.addView(design);
        LinearLayout nav=row();Button previous=button("‹",()->{if(input!=null&&page>0){page--;render();}},false);previous.setContentDescription("Página anterior");nav.addView(previous,new LinearLayout.LayoutParams(dp(44),dp(44)));
        pageLabel=text("VISTA DEL DOCUMENTO",11);pageLabel.setGravity(Gravity.CENTER);nav.addView(pageLabel,new LinearLayout.LayoutParams(0,-2,1));
        Button next=button("›",()->{if(input!=null&&page+1<pages){page++;render();}},false);next.setContentDescription("Página siguiente");nav.addView(next,new LinearLayout.LayoutParams(dp(44),dp(44)));
        Button fit=button("Ajustar",()->preview.resetZoom(),false);nav.addView(fit,new LinearLayout.LayoutParams(dp(66),dp(44)));viewMode=button("Ampliar",()->{focusMode=!focusMode;controlsPanel.setVisibility(focusMode?View.GONE:View.VISIBLE);viewMode.setText(focusMode?"Volver":"Ampliar");},false);viewMode.setTag("focus-view");nav.addView(viewMode,new LinearLayout.LayoutParams(dp(66),dp(44)));workspacePanel.addView(nav);
        preview=new PdfSelectionView(this);preview.setAppearance(appearance);preview.setTag("pdf-preview");workspacePanel.addView(preview,new LinearLayout.LayoutParams(-1,0,1));
        placementRow=row();TextView help=text("Arrastra o ajusta el recuadro",10);help.setTextColor(0xFF66758E);placementRow.addView(help,new LinearLayout.LayoutParams(0,-2,1));placeButton=button("Poner firma",()->{if(input!=null)preview.resetBox();else status.setText("Abre un PDF para colocar la firma.");},false);placeButton.setTag("place-signature");placementRow.addView(placeButton,new LinearLayout.LayoutParams(dp(112),dp(40)));workspacePanel.addView(placementRow);
        signingRow=row();Button sign=button("Firmar",this::authorize,true);sign.setTag("sign");signingRow.addView(sign,new LinearLayout.LayoutParams(0,-1,1));LinearLayout.LayoutParams primary=new LinearLayout.LayoutParams(-1,dp(54));primary.topMargin=dp(4);workspacePanel.addView(signingRow,primary);
        status=text("",10);status.setTextColor(0xFF66758E);status.setMaxLines(2);workspacePanel.addView(status);refreshStyles();updateOrientation();setContentView(root);        Uri uri=incoming(getIntent());if(uri!=null)openPdf(uri);
    }
    private void updateOrientation(){boolean landscape=getResources().getConfiguration().orientation==android.content.res.Configuration.ORIENTATION_LANDSCAPE;rootLayout.setOrientation(landscape?LinearLayout.HORIZONTAL:LinearLayout.VERTICAL);controlsPanel.setLayoutParams(new LinearLayout.LayoutParams(landscape?dp(270):-1,landscape?-1:-2));LinearLayout.LayoutParams params=new LinearLayout.LayoutParams(landscape?0:-1,landscape?-1:0,1);params.leftMargin=landscape?dp(12):0;workspacePanel.setLayoutParams(params);
        if(landscape&&placeButton.getParent()!=signingRow){placementRow.removeView(placeButton);LinearLayout.LayoutParams place=new LinearLayout.LayoutParams(dp(108),-1);place.rightMargin=dp(6);signingRow.addView(placeButton,0,place);}
        else if(!landscape&&placeButton.getParent()!=placementRow){signingRow.removeView(placeButton);placementRow.addView(placeButton,new LinearLayout.LayoutParams(dp(112),dp(40)));}placementRow.setVisibility(landscape?View.GONE:View.VISIBLE);
    }
    @Override public void onConfigurationChanged(android.content.res.Configuration configuration){super.onConfigurationChanged(configuration);updateOrientation();}    private int dp(int value){return Math.round(value*getResources().getDisplayMetrics().density);}
    private TextView text(String value,int size){TextView t=new TextView(this);t.setText(value);t.setTextSize(size);t.setPadding(0,dp(3),0,dp(3));t.setTextColor(Color.rgb(25,47,74));return t;}
    private LinearLayout row(){LinearLayout row=new LinearLayout(this);row.setGravity(Gravity.CENTER_VERTICAL);return row;}
    private GradientDrawable background(int color,int radius){GradientDrawable shape=new GradientDrawable();shape.setColor(color);shape.setCornerRadius(dp(radius));return shape;}
    private LinearLayout card(){LinearLayout card=new LinearLayout(this);card.setOrientation(LinearLayout.VERTICAL);card.setPadding(dp(14),dp(8),dp(12),dp(8));card.setBackground(background(Color.WHITE,16));return card;}
    private void addCard(LinearLayout root,View card){LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);p.topMargin=dp(10);root.addView(card,p);}
    private Button button(String title,Runnable action,boolean primary){Button button=new Button(this);button.setText(title);button.setTextSize(primary?15:12);button.setAllCaps(false);button.setTypeface(null,Typeface.BOLD);button.setTextColor(primary?Color.WHITE:0xFF3155D9);button.setBackground(background(primary?0xFF3155D9:0xFFF0F3FC,12));button.setPadding(dp(6),0,dp(6),0);button.setOnClickListener(v->{if(!busy)action.run();});buttons.add(button);return button;}
    private void setBusy(boolean value,String message){busy=value;if(appearanceEditor!=null&&appearanceEditor.isShowing())appearanceEditor.setBusy(value);for(Button b:buttons)b.setEnabled(!value);styleSelector.setEnabled(!value&&styles!=null);if(settingsDialog!=null&&settingsDialog.isShowing()){settingsDialog.setCancelable(!value);if(value)settingsDialog.getWindow().addFlags(WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE);else settingsDialog.getWindow().clearFlags(WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE);}preview.setEnabled(!value);status.setText(message);}
    private void refreshStyles(){
        if(styles==null){styleSelector.setEnabled(false);return;}
        refreshingStyles=true;
        try{for(Spinner selector:new Spinner[]{styleSelector,settingsStyleSelector}){
            if(selector==null)continue;
            ArrayAdapter<SignatureStyles.Style> adapter=new ArrayAdapter<>(this,android.R.layout.simple_spinner_item,new ArrayList<>(styles.list()));adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);selector.setAdapter(adapter);
            int index=styles.list().indexOf(styles.selected());selector.setSelection(index,false);
            selector.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> parent){}public void onItemSelected(AdapterView<?> parent,View view,int position,long id){
                if(refreshingStyles||busy)return;SignatureStyles.Style selected=(SignatureStyles.Style)parent.getItemAtPosition(position);if(selected.id.equals(styles.selected().id))return;
                work("Cargando estilo…",()->{SignatureAppearance value=styles.appearance(selected);styles.select(selected.id);return value;},value->{appearance=value;preview.setAppearance(value);refreshStyles();status.setText("Estilo: "+styles.selected().name);});
            }});
        }}finally{refreshingStyles=false;}
    }
    private Switch settingSwitch(LinearLayout parent,String label,String tag,boolean initial,Consumer<Boolean> change){
        Switch value=new Switch(this);value.setText(label);value.setTag(tag);value.setTextSize(14);value.setTextColor(0xFF172D47);value.setMinHeight(dp(52));value.setChecked(initial);value.setOnCheckedChangeListener((button,checked)->{if(!busy)change.accept(checked);});parent.addView(value);return value;
    }
    private void preference(String key,boolean value){getSharedPreferences("appearance",MODE_PRIVATE).edit().putBoolean(key,value).apply();}
    private void settings(){
        settingsDialog=new Dialog(this);settingsDialog.setTitle("Configuración");LinearLayout content=new LinearLayout(this);content.setOrientation(LinearLayout.VERTICAL);content.setPadding(dp(20),dp(14),dp(20),dp(18));content.setBackgroundColor(0xFFF5F7FC);
        LinearLayout header=row();TextView title=text("Configuración",24);title.setTypeface(null,Typeface.BOLD);header.addView(title,new LinearLayout.LayoutParams(0,-2,1));Button close=button("Cerrar",()->settingsDialog.dismiss(),false);close.setTag("close-settings");header.addView(close,new LinearLayout.LayoutParams(dp(76),dp(44)));content.addView(header);
        LinearLayout designs=card();TextView label=text("Firmas personalizadas",17);label.setTypeface(null,Typeface.BOLD);designs.addView(label);settingsStyleSelector=new Spinner(this);settingsStyleSelector.setTag("settings-style-selector");settingsStyleSelector.setContentDescription("Estilo que quieres editar");designs.addView(settingsStyleSelector,new LinearLayout.LayoutParams(-1,dp(48)));
        LinearLayout actions=row();Button add=button("Nuevo",()->nameStyle(false),false);add.setTag("new-style");actions.addView(add,new LinearLayout.LayoutParams(0,dp(44),1));Button edit=button("Editar",this::editAppearance,false);edit.setTag("customize");actions.addView(edit,new LinearLayout.LayoutParams(0,dp(44),1));Button rename=button("Renombrar",()->nameStyle(true),false);rename.setTag("rename-style");actions.addView(rename,new LinearLayout.LayoutParams(0,dp(44),1));designs.addView(actions);
        LinearLayout extra=row();Button duplicate=button("Duplicar",this::duplicateStyle,false);extra.addView(duplicate,new LinearLayout.LayoutParams(0,dp(44),1));Button remove=button("Eliminar",this::removeStyle,false);remove.setTag("delete-style");extra.addView(remove,new LinearLayout.LayoutParams(0,dp(44),1));designs.addView(extra);designs.addView(text("Hasta 20 estilos. Se recuerda el último elegido; texto e imagen se guardan solo en este móvil.",11));addCard(content,designs);
        LinearLayout signing=card();signing.addView(text("Opciones de firma",17));settingSwitch(signing,"Sello de tiempo · requiere Internet","timestamp",timestampEnabled,value->{timestampEnabled=value;preference("timestamp",value);});signing.addView(text("DigiCert recibe una huella de la firma, sin el PDF ni las claves. Si falla el sello solicitado, no se genera una copia sin él.",11));
        settingSwitch(signing,"Pedir confirmación antes de firmar","confirm-before-signing",confirmBeforeSigning,value->{confirmBeforeSigning=value;preference("confirmBeforeSigning",value);});
        settingSwitch(signing,"Mostrar aviso al terminar de guardar","notify-after-saving",notifyAfterSaving,value->{notifyAfterSaving=value;preference("notifyAfterSaving",value);});signing.addView(text("Después de firmar siempre podrás elegir Guardar como o Compartir.",11));addCard(content,signing);
        LinearLayout checks=card();checks.addView(text("Comprobaciones",17));checks.addView(button("Comprobar firmas del PDF",this::verify,false));checks.addView(button("Ver certificado seleccionado",()->{if(identity==null)error("Selecciona un certificado.");else report("Certificado seleccionado",identity.description()+"\n\nEmisor: "+identity.certificate().getIssuerX500Principal().getName()+"\nDesde: "+identity.certificate().getNotBefore()+"\nHasta: "+identity.certificate().getNotAfter());},false));checks.addView(text("Android comprueba la integridad criptográfica. La confianza y revocación se consultan en un validador externo; no se muestran aquí como comprobadas.",11));addCard(content,checks);
        LinearLayout copies=card();copies.addView(text("Última firma",17));copies.addView(button("Guardar como…",()->{if(signed!=null)savePicker();else error("Primero firma un PDF.");},false));copies.addView(button("Compartir",()->{if(signed!=null)share();else error("Primero firma un PDF.");},false));addCard(content,copies);
        LinearLayout about=card();about.addView(text("FirmaPDF · 0.3.0",17));about.addView(button("Código fuente y licencias",this::licenses,false));about.addView(button("Privacidad",()->report("Privacidad","Los documentos y las claves se procesan en tu móvil. Los diseños se guardan en el almacenamiento privado de FirmaPDF. No se guardan contraseñas ni claves con los estilos. El certificado .p12/.pfx se usa solo en esta sesión.\n\nEl sello de tiempo opcional envía a DigiCert una huella de la firma y un nonce; no envía el documento ni la clave privada. Guardar y compartir utiliza el destino que elijas en Android.\n\nLos PDF contienen el certificado público del firmante y el texto e imagen que hayas seleccionado."),false));addCard(content,about);
        ScrollView scroll=new ScrollView(this);scroll.addView(content);scroll.setOnApplyWindowInsetsListener((v,insets)->{v.setPadding(insets.getSystemWindowInsetLeft(),insets.getSystemWindowInsetTop(),insets.getSystemWindowInsetRight(),insets.getSystemWindowInsetBottom());return insets;});settingsDialog.setContentView(scroll);settingsDialog.setOnDismissListener(d->{settingsStyleSelector=null;buttons.removeIf(b->!b.isAttachedToWindow());});refreshStyles();settingsDialog.show();if(settingsDialog.getWindow()!=null){settingsDialog.getWindow().setBackgroundDrawableResource(android.R.color.transparent);settingsDialog.getWindow().setLayout(-1,-1);}
    }
    private void nameStyle(boolean rename){
        if(styles==null){error("No se pudieron cargar los estilos.");return;}String id=styles.selected().id;EditText name=new EditText(this);name.setSingleLine(true);name.setHint("Nombre del estilo");name.setFilters(new android.text.InputFilter[]{new android.text.InputFilter.LengthFilter(40)});if(rename)name.setText(styles.selected().name);
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle(rename?"Renombrar estilo":"Nuevo estilo").setView(name).setNegativeButton("Cancelar",null).setPositiveButton("Guardar",null).create();dialog.setOnShowListener(d->dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v->{
            try{if(rename)styles.rename(id,name.getText().toString());else styles.add(name.getText().toString(),SignatureAppearance.standard());appearance=styles.appearance(styles.selected());preview.setAppearance(appearance);refreshStyles();dialog.dismiss();if(!rename)editAppearance();}catch(IOException e){name.setError(e.getMessage());}
        }));dialog.show();
    }
    private void duplicateStyle(){if(styles==null)return;String base=styles.selected().name;int number=2;String candidate;do{String suffix=" ("+(number++)+")";candidate=base.substring(0,Math.min(base.length(),40-suffix.length()))+suffix;}while(styleNameExists(candidate));final String name=candidate;
        work("Duplicando estilo…",()->{styles.add(name,appearance);return true;},done->refreshStyles());
    }
    private boolean styleNameExists(String name){for(SignatureStyles.Style style:styles.list())if(style.name.equalsIgnoreCase(name))return true;return false;}
    private void removeStyle(){if(styles==null)return;String id=styles.selected().id;new AlertDialog.Builder(this).setTitle("Eliminar estilo").setMessage("¿Eliminar «"+styles.selected().name+"» de este móvil?").setNegativeButton("Cancelar",null).setPositiveButton("Eliminar",(d,w)->work("Eliminando estilo…",()->{styles.remove(id);return styles.appearance(styles.selected());},value->{appearance=value;preview.setAppearance(value);refreshStyles();})).show();}
    private void report(String title,String message){TextView body=text(message,14);body.setTextIsSelectable(true);body.setPadding(dp(20),dp(12),dp(20),dp(12));ScrollView scroll=new ScrollView(this);scroll.addView(body);new AlertDialog.Builder(this).setTitle(title).setView(scroll).setPositiveButton("Cerrar",null).show();}
    private void licenses(){new AlertDialog.Builder(this).setTitle("Código fuente y licencias").setItems(new String[]{"Código fuente en GitHub","Apache 2.0 · FirmaPDF y PDFBox","Avisos PDFBox","PDFBox Android","Bouncy Castle"},(d,index)->{
        if(index==0){try{startActivity(new Intent(Intent.ACTION_VIEW,Uri.parse("https://github.com/eduardocvk/firmar_certificado")));}catch(ActivityNotFoundException e){report("Código fuente","https://github.com/eduardocvk/firmar_certificado");}return;}
        String[] files={"","Apache-2.0.txt","PDFBox-NOTICE.txt","PdfBox-Android.txt","BouncyCastle.html"};try(InputStream input=getAssets().open("licenses/"+files[index])){ByteArrayOutputStream out=new ByteArrayOutputStream();copy(input,out,1024*1024);String text=out.toString("UTF-8");if(index==4)text=android.text.Html.fromHtml(text,android.text.Html.FROM_HTML_MODE_LEGACY).toString();report("Licencias",text);}catch(IOException e){error("No se pudo abrir el texto de la licencia.");}
    }).show();}
    private void editAppearance(){if(styles==null){error("No se pudieron cargar los estilos. Vuelve a abrir la aplicación.");return;}String styleId=styles.selected().id;appearanceEditor=new AppearanceEditor(this,appearance,identity==null?"Tu nombre y apellidos":identity.name(),identity==null?"Emisor de tu certificado":identity.certificate().getIssuerX500Principal().getName(),()->pick(OPEN_IMAGE,"image/*"),value->work("Guardando tu diseño…",()->{styles.update(styleId,value);return value;},saved->{appearance=saved;preview.setAppearance(saved);appearanceEditor.dismiss();refreshStyles();status.setText("Estilo guardado en este móvil.");}));appearanceEditor.show();}
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
        Identity selected=identity;File source=input;String sourceName=originalName;SignatureAppearance style=appearance;boolean timestamp=timestampEnabled;
        Runnable signing=()->work("Firmando y comprobando integridad…",()->{
                File folder=new File(getCacheDir(),"signed");if(!folder.isDirectory()&&!folder.mkdirs())throw new IOException("No se pudo crear la carpeta de salida.");
                File result=new File(folder,"firmado-"+UUID.randomUUID()+".pdf");PdfEngine.sign(source,result,placement,selected,style,timestamp?Rfc3161Client.digicert():null);return new SignedCopy(result,PdfEngine.verify(result));
            },result->{if(signed!=null)signed.delete();signed=result.file;signedSource=source;signedName=sourceName.replaceFirst("(?i)\\.pdf$","")+"_firmado.pdf";
                new AlertDialog.Builder(this).setTitle("PDF firmado").setMessage(signedName)
                    .setPositiveButton("Guardar como…",(dialog,which)->savePicker()).setNeutralButton("Compartir",(dialog,which)->share()).setNegativeButton("Cerrar",null).show();
                status.setText("PDF firmado. Puedes guardar o compartir la última copia.");
            });
        if(confirmBeforeSigning)new AlertDialog.Builder(this).setTitle("Autorizar firma").setMessage("Vas a firmar el PDF con:\n"+selected.description()+"\n\n"+(timestamp?"Se solicitará un sello de tiempo a DigiCert enviando solo la huella de la firma, sin el PDF ni las claves.":"Sin sello de tiempo: la fecha procede del móvil.")).setNegativeButton("Cancelar",null).setPositiveButton("Firmar",(d,w)->signing.run()).show();else signing.run();
    }
    private static final class SignedCopy{final File file;final PdfEngine.Verification verification;SignedCopy(File file,PdfEngine.Verification verification){this.file=file;this.verification=verification;}}
    private void savePicker(){Intent intent=new Intent(Intent.ACTION_CREATE_DOCUMENT);intent.addCategory(Intent.CATEGORY_OPENABLE);intent.setType("application/pdf");intent.putExtra(Intent.EXTRA_TITLE,signedName);startActivityForResult(intent,SAVE_PDF);}
    private void export(Uri uri){File result=signed;if(result==null){error("La copia temporal ya no está disponible. Vuelve a firmar el documento.");return;}
        work("Guardando copia…",()->{try(InputStream in=new FileInputStream(result);OutputStream out=getContentResolver().openOutputStream(uri,"wt")){if(out==null)throw new IOException("No se pudo abrir el destino.");copy(in,out,Long.MAX_VALUE);}return true;},done->{status.setText("PDF firmado guardado.");if(notifyAfterSaving)new AlertDialog.Builder(this).setTitle("Copia guardada").setMessage("El PDF firmado se ha guardado en la ubicación elegida.").setPositiveButton("Aceptar",null).show();});
    }
    private void share(){if(signed==null)return;Uri uri=FileProvider.getUriForFile(this,getPackageName()+".files",signed);Intent intent=new Intent(Intent.ACTION_SEND);intent.setType("application/pdf");intent.putExtra(Intent.EXTRA_STREAM,uri);intent.setClipData(ClipData.newRawUri("PDF firmado",uri));intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);startActivity(Intent.createChooser(intent,"Compartir PDF firmado"));}
    private void verify(){if(input==null){error("Abre un PDF para comprobar sus firmas.");return;}File target=signed!=null&&input.equals(signedSource)?signed:input;work("Comprobando integridad…",()->PdfEngine.verify(target),result->report("Comprobación de firmas",result.signatures==0?"El PDF no contiene firmas digitales.":result.toString()));}
    private static void copy(InputStream in,OutputStream out,long limit)throws IOException{byte[] buffer=new byte[32768];int n;long size=0;while((n=in.read(buffer))!=-1){size+=n;if(size>limit)throw new IOException("Archivo demasiado grande para esta versión.");out.write(buffer,0,n);}}
    @Override protected void onDestroy(){
        identity=null;if(appearanceEditor!=null)appearanceEditor.dismiss();if(settingsDialog!=null)settingsDialog.dismiss();preview.release();File folder=session;
        executor.execute(()->{File[] files=folder.listFiles();if(files!=null)for(File file:files)file.delete();folder.delete();});executor.shutdown();super.onDestroy();
    }
}
